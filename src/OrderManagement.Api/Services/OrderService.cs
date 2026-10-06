using System.Text.Json;
using ErrorOr;
using Microsoft.EntityFrameworkCore;
using OrderManagement.Api.Common.Http;
using OrderManagement.Api.Common.Idempotency;
using OrderManagement.Api.Common.Paging;
using OrderManagement.Api.Contracts.Orders;
using OrderManagement.Api.Services.Interfaces;
using OrderManagement.Domain.Common;
using OrderManagement.Domain.Orders;
using OrderManagement.Infrastructure.Idempotency;
using OrderManagement.Infrastructure.Observability;
using OrderManagement.Infrastructure.Persistence;

namespace OrderManagement.Api.Services;

public sealed class OrderService(AppDbContext db, TimeProvider clock, IOrderEventPublisher events, OrderMetrics metrics)
    : IOrderService
{
    private const string IdempotencyPrimaryKey = "PK_IdempotencyKeys";

    private static readonly JsonSerializerOptions PayloadJson = new(JsonSerializerDefaults.Web);

    public async Task<ErrorOr<OrderResult>> CreateAsync(CreateOrderRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        var customerId = request.CustomerId.GetValueOrDefault();
        var customer = await db.Customers.AsNoTracking().SingleOrDefaultAsync(c => c.Id == customerId, ct);
        if (customer is null)
        {
            return OrderErrors.CustomerNotFound(customerId);
        }

        // The aggregate validates the currency against the customer's country, price precision and duplicate SKUs,
        // and computes the total. Client-supplied totals don't exist in the contract.
        var lines = request.LineItems?.Select(l => new NewOrderLine(l.ProductSku, l.Quantity, l.UnitPrice)).ToList();
        var created = Order.Create(customer, request.CurrencyCode, lines, clock);
        if (created.IsError)
        {
            return created.Errors;
        }

        var order = created.Value;
        db.Orders.Add(order); // assigns the sequential ID the event carries

        // Outbox: the event row and the order commit in one transaction, or neither does.
        await events.OrderCreatedAsync(order, ct);
        await db.SaveChangesAsync(ct);
        metrics.OrderCreated(order.CurrencyCode);

        return new OrderResult(ToResponse(order), ETags.From(order.RowVersion));
    }

    public async Task<ErrorOr<OrderResult>> GetByIdAsync(Guid id, CancellationToken ct)
    {
        // One round trip: Orders joined to OrderLineItems. No lazy loading, so no N+1.
        var order = await db.Orders.AsNoTracking().Include(o => o.LineItems).SingleOrDefaultAsync(o => o.Id == id, ct);

        return order is null
            ? OrderErrors.NotFound(id)
            : new OrderResult(ToResponse(order), ETags.From(order.RowVersion));
    }

    public async Task<string?> GetETagAsync(Guid id, CancellationToken ct)
    {
        var rowVersion = await db.Orders.AsNoTracking().Where(o => o.Id == id).Select(o => o.RowVersion).SingleOrDefaultAsync(ct);
        return rowVersion is null ? null : ETags.From(rowVersion);
    }

    public Task<PagedResult<OrderSummaryResponse>> ListAsync(OrderListQuery query, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);

        var orders = db.Orders.AsNoTracking();

        if (query.CustomerId is { } customerId)
        {
            orders = orders.Where(o => o.CustomerId == customerId);
        }

        if (OrderStatusParser.TryParse(query.Status, out var status))
        {
            orders = orders.Where(o => o.Status == status);
        }

        // (CustomerId, Status, CreatedAt) INCLUDE (TotalAmount, CurrencyCode) serves the filtered list,
        // and the customer name is a primary-key lookup.
        var sort = SortSpec.Parse(query.Sort, new SortSpec(OrderListQuery.SortByCreatedAt, Descending: true));
        var ordered = sort.Is(OrderListQuery.SortByTotal)
            ? (sort.Descending ? orders.OrderByDescending(o => o.TotalAmount) : orders.OrderBy(o => o.TotalAmount))
            : (sort.Descending ? orders.OrderByDescending(o => o.CreatedAt) : orders.OrderBy(o => o.CreatedAt));

        return ordered
            .ThenBy(o => o.Id)
            .Select(o => new OrderSummaryResponse(
                o.Id, o.CustomerId, o.Customer!.Name, o.Status, o.CurrencyCode, o.TotalAmount, o.CreatedAt))
            .ToPagedResultAsync(query, ct);
    }

    public async Task<StatusChangeResult> ChangeStatusAsync(
        Guid id,
        OrderStatus target,
        string? ifMatch,
        IdempotencyRequest idempotency,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(idempotency);
        var now = clock.GetUtcNow().UtcDateTime;

        // 1. Seen this key before? Replay it, or reject reuse for a different request.
        var previous = await FindRecordAsync(idempotency, ct);
        if (previous is not null)
        {
            if (previous.ExpiresAt > now)
            {
                return Replay(previous, idempotency);
            }

            await db.IdempotencyKeys
                .Where(r => r.ClientId == previous.ClientId && r.Key == previous.Key)
                .ExecuteDeleteAsync(ct);
        }

        // 2. Decide the outcome against the current state.
        var order = await db.Orders.Include(o => o.LineItems).SingleOrDefaultAsync(o => o.Id == id, ct);
        var previousStatus = order?.Status;
        ErrorOr<OrderResult> outcome;
        if (order is null)
        {
            outcome = OrderErrors.NotFound(id);
        }
        else if (ETags.IfMatchSatisfied(ifMatch, ETags.From(order.RowVersion)) == false)
        {
            outcome = PreconditionFailed;
        }
        else
        {
            var transition = order.TransitionTo(target);
            outcome = transition.IsError
                ? transition.Errors
                : new OrderResult(ToResponse(order), ETag: null); // the ETag is known only after the save

            if (!transition.IsError && target == OrderStatus.Paid)
            {
                // Same transaction as the status change and the idempotency record: a replay publishes nothing.
                await events.OrderPaidAsync(order, ct);
            }
        }

        // 3. Record the outcome in the SAME SaveChanges as the status change, so "changed" and "remembered"
        //    commit together. A crash can't leave a change without a record, or the other way round.
        db.IdempotencyKeys.Add(new IdempotencyRecord
        {
            ClientId = idempotency.ClientId,
            Key = idempotency.Key,
            RequestHash = idempotency.RequestHash,
            Outcome = outcome.IsError ? IdempotencyOutcome.Failed : IdempotencyOutcome.Succeeded,
            Payload = outcome.IsError
                ? JsonSerializer.Serialize(outcome.Errors.Select(StoredError.From), PayloadJson)
                : JsonSerializer.Serialize(outcome.Value.Order, PayloadJson),
            CreatedAt = now,
            ExpiresAt = now.Add(IdempotencyRequest.RetentionPeriod),
        });

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex is DbUpdateConcurrencyException || ex.IsUniqueViolation(IdempotencyPrimaryKey))
        {
            // Someone else wrote first. If it was this same request under this same key (a client retry racing its
            // original), replay their outcome. Otherwise it was a different change to the order: 409, and nothing is
            // recorded, so a retry with this key is evaluated against the new state.
            db.ChangeTracker.Clear();
            var winner = await FindRecordAsync(idempotency, ct);
            return winner is not null
                ? Replay(winner, idempotency)
                : new StatusChangeResult(ConcurrencyConflict, Replayed: false);
        }

        if (outcome.IsError)
        {
            return new StatusChangeResult(outcome, Replayed: false);
        }

        metrics.StatusChanged(previousStatus!.Value, target); // committed, and not a replay
        return new StatusChangeResult(new OrderResult(outcome.Value.Order, ETags.From(order!.RowVersion)), Replayed: false);
    }

    private Task<IdempotencyRecord?> FindRecordAsync(IdempotencyRequest idempotency, CancellationToken ct) =>
        db.IdempotencyKeys.AsNoTracking()
            .SingleOrDefaultAsync(r => r.ClientId == idempotency.ClientId && r.Key == idempotency.Key, ct);

    private static StatusChangeResult Replay(IdempotencyRecord record, IdempotencyRequest request)
    {
        if (!string.Equals(record.RequestHash, request.RequestHash, StringComparison.Ordinal))
        {
            return new StatusChangeResult(KeyReused, Replayed: false);
        }

        ErrorOr<OrderResult> outcome = record.Outcome == IdempotencyOutcome.Succeeded
            ? new OrderResult(JsonSerializer.Deserialize<OrderResponse>(record.Payload, PayloadJson)!, ETag: null)
            : JsonSerializer.Deserialize<List<StoredError>>(record.Payload, PayloadJson)!.Select(e => e.ToError()).ToList();

        return new StatusChangeResult(outcome, Replayed: true);
    }

    private static OrderResponse ToResponse(Order order) => new(
        order.Id,
        order.CustomerId,
        order.Status,
        order.CurrencyCode,
        order.TotalAmount,
        order.CreatedAt,
        order.AllocatedAt,
        order.AllowedNextStatuses,
        [.. order.LineItems
            .OrderBy(li => li.ProductSku, StringComparer.Ordinal)
            .Select(li => new OrderLineItemResponse(li.Id, li.ProductSku, li.Quantity, li.UnitPrice, li.LineTotal))]);

    private static readonly Error PreconditionFailed = Error.Custom(
        StatusCodes.Status412PreconditionFailed,
        "Order.PreconditionFailed",
        "This order changed since you loaded it. Refresh to see the latest version, then try again.");

    private static readonly Error ConcurrencyConflict = Error.Conflict(
        "Order.ConcurrencyConflict",
        "This order was changed by someone else at the same moment. Refresh and try again.");

    private static readonly Error KeyReused = Error.Custom(
        StatusCodes.Status422UnprocessableEntity,
        "Idempotency.IdempotencyKeyReused",
        "This Idempotency-Key was already used for a different request. Use a new key for each distinct change.");

    /// <summary>The serialisable form of an <see cref="Error"/> stored for replay.</summary>
    private sealed record StoredError(int Type, string Code, string Description, string? Field)
    {
        public static StoredError From(Error error) => new(
            error.NumericType,
            error.Code,
            error.Description,
            error.Metadata?.TryGetValue(ErrorMetadata.FieldKey, out var field) == true ? field as string : null);

        public Error ToError() => Error.Custom(
            Type,
            Code,
            Description,
            Field is null ? null : ErrorMetadata.ForField(Field));
    }
}
