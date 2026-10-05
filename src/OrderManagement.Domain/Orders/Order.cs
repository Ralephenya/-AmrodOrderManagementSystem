using System.Collections.Frozen;
using ErrorOr;
using OrderManagement.Domain.Common;
using OrderManagement.Domain.Customers;
using OrderManagement.Domain.Sadc;

namespace OrderManagement.Domain.Orders;

/// <summary>
/// Order aggregate. Totals are computed here and nowhere else, and status changes go through
/// <see cref="TransitionTo"/> so the lifecycle rules can't be bypassed.
/// </summary>
public sealed class Order
{
    public const int MaxLineItems = 100;

    private static readonly FrozenDictionary<OrderStatus, OrderStatus[]> Transitions =
        new Dictionary<OrderStatus, OrderStatus[]>
        {
            [OrderStatus.Pending] = [OrderStatus.Paid, OrderStatus.Cancelled],
            [OrderStatus.Paid] = [OrderStatus.Fulfilled, OrderStatus.Cancelled],
            [OrderStatus.Fulfilled] = [],
            [OrderStatus.Cancelled] = [],
        }.ToFrozenDictionary();

    private readonly List<OrderLineItem> _lineItems = [];

    // For EF Core materialisation.
    private Order()
    {
    }

    public Guid Id { get; private set; }

    public Guid CustomerId { get; private set; }

    public Customer? Customer { get; private set; }

    public OrderStatus Status { get; private set; } = OrderStatus.Pending;

    /// <summary>UTC.</summary>
    public DateTime CreatedAt { get; private set; }

    /// <summary>ISO 4217, upper case. Validated against the customer's country when the order is created.</summary>
    public string CurrencyCode { get; private set; } = string.Empty;

    /// <summary>Σ(Quantity × UnitPrice), computed server-side and exact.</summary>
    public decimal TotalAmount { get; private set; }

    /// <summary>UTC time the worker allocated stock; null until then.</summary>
    public DateTime? AllocatedAt { get; private set; }

    /// <summary>SQL Server rowversion used for optimistic concurrency (and the HTTP ETag).</summary>
#pragma warning disable CA1819 // EF Core maps rowversion to byte[]; it's never mutated by callers.
    public byte[] RowVersion { get; private set; } = [];
#pragma warning restore CA1819

    public IReadOnlyCollection<OrderLineItem> LineItems => _lineItems.AsReadOnly();

    /// <summary>The statuses this order may move to next (empty once terminal).</summary>
    public IReadOnlyList<OrderStatus> AllowedNextStatuses => NextStatusesFrom(Status);

    public static IReadOnlyList<OrderStatus> NextStatusesFrom(OrderStatus status) =>
        Transitions.TryGetValue(status, out var next) ? next : [];

    /// <summary>
    /// Creates a Pending order for <paramref name="customer"/>. Every invalid field is reported at once, so the
    /// caller can show all problems in one round trip.
    /// </summary>
    public static ErrorOr<Order> Create(
        Customer customer,
        string? currencyCode,
        IReadOnlyList<NewOrderLine>? lines,
        TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(customer);
        ArgumentNullException.ThrowIfNull(clock);

        List<Error> errors = [];

        var currencyResult = SadcCatalogue.ValidateCurrencyForCountry(customer.CountryCode, currencyCode);
        var currency = currencyResult.IsError ? null : currencyResult.Value;
        if (currencyResult.IsError)
        {
            errors.AddRange(currencyResult.Errors);
        }

        lines ??= [];
        if (lines.Count == 0)
        {
            errors.Add(OrderErrors.LineItemsRequired);
        }
        else if (lines.Count > MaxLineItems)
        {
            errors.Add(OrderErrors.TooManyLineItems(MaxLineItems));
        }
        else
        {
            errors.AddRange(ValidateLines(lines, currency));
        }

        if (errors.Count > 0)
        {
            return errors;
        }

        var order = new Order
        {
            CustomerId = customer.Id,
            CurrencyCode = currency!.Code,
            CreatedAt = clock.GetUtcNow().UtcDateTime,
            Status = OrderStatus.Pending,
        };

        foreach (var line in lines)
        {
            order._lineItems.Add(new OrderLineItem(NormaliseSku(line.ProductSku), line.Quantity, line.UnitPrice));
        }

        // Quantity is an int and prices fit decimal(18,2), so the sum can't overflow decimal itself;
        // it can still exceed what the database column holds.
        order.TotalAmount = order._lineItems.Sum(li => li.LineTotal);
        if (order.TotalAmount > Money.MaxAmount)
        {
            return OrderErrors.TotalTooLarge;
        }

        return order;
    }

    /// <summary>
    /// Moves the order to <paramref name="target"/> if the lifecycle allows it.
    /// <c>Paid → Fulfilled</c> additionally requires stock to have been allocated.
    /// </summary>
    public ErrorOr<Success> TransitionTo(OrderStatus target)
    {
        if (!Enum.IsDefined(target))
        {
            return OrderErrors.InvalidStatus(target);
        }

        if (target == Status)
        {
            return OrderErrors.AlreadyInStatus(Status);
        }

        var allowed = AllowedNextStatuses;
        if (!allowed.Contains(target))
        {
            return OrderErrors.InvalidStatusTransition(Status, target, allowed);
        }

        if (target == OrderStatus.Fulfilled && AllocatedAt is null)
        {
            return OrderErrors.NotAllocated;
        }

        Status = target;
        return Result.Success;
    }

    /// <summary>
    /// Records that stock was allocated (called by the worker on <c>OrderCreated</c>), then fulfils the order if it
    /// is already Paid. Safe to call more than once: message redelivery must not fail or double-apply.
    /// </summary>
    public ErrorOr<Success> Allocate(TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(clock);

        if (Status == OrderStatus.Cancelled)
        {
            return OrderErrors.CancelledCannotAllocate;
        }

        AllocatedAt ??= clock.GetUtcNow().UtcDateTime;
        FulfilIfReady();
        return Result.Success;
    }

    /// <summary>
    /// Fulfils the order when it is both Paid and allocated, whichever happened last.
    /// Returns true if this call changed the status.
    /// </summary>
    public bool FulfilIfReady()
    {
        if (Status != OrderStatus.Paid || AllocatedAt is null)
        {
            return false;
        }

        Status = OrderStatus.Fulfilled;
        return true;
    }

    private static IEnumerable<Error> ValidateLines(IReadOnlyList<NewOrderLine> lines, Currency? currency)
    {
        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];

            if (string.IsNullOrWhiteSpace(line.ProductSku))
            {
                yield return OrderErrors.SkuRequired(i);
            }
            else if (line.ProductSku.Trim().Length > OrderLineItem.ProductSkuMaxLength)
            {
                yield return OrderErrors.SkuTooLong(i);
            }

            if (line.Quantity <= 0)
            {
                yield return OrderErrors.QuantityMustBePositive(i);
            }

            if (line.UnitPrice < 0)
            {
                yield return OrderErrors.UnitPriceNegative(i);
            }
            else if (currency is not null && !Money.FitsCurrency(line.UnitPrice, currency))
            {
                yield return OrderErrors.UnitPriceTooPrecise(i, currency);
            }
            else if (line.UnitPrice > Money.MaxAmount)
            {
                yield return OrderErrors.TotalTooLarge;
            }
        }

        var duplicates = lines
            .Where(l => !string.IsNullOrWhiteSpace(l.ProductSku))
            .GroupBy(l => NormaliseSku(l.ProductSku))
            .Where(g => g.Count() > 1)
            .Select(g => g.Key);

        foreach (var sku in duplicates)
        {
            yield return OrderErrors.DuplicateSku(sku);
        }
    }

    private static string NormaliseSku(string? sku) => sku!.Trim().ToUpperInvariant();
}
