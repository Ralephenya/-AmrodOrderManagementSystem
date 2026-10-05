using OrderManagement.Api.Common.Paging;
using OrderManagement.Domain.Orders;

namespace OrderManagement.Api.Contracts.Orders;

/// <param name="CustomerId">An existing customer. Their country decides which currencies are allowed.</param>
/// <param name="CurrencyCode">ISO 4217, e.g. <c>ZAR</c>. Must be permitted for the customer's country.</param>
/// <param name="LineItems">1 to 100 lines. Each SKU may appear once.</param>
public sealed record CreateOrderRequest(Guid? CustomerId, string? CurrencyCode, IReadOnlyList<CreateOrderLineRequest>? LineItems);

/// <param name="ProductSku">Up to 64 characters; stored upper case.</param>
/// <param name="Quantity">At least 1.</param>
/// <param name="UnitPrice">In the order's currency, ≥ 0, with no more decimals than the currency allows.</param>
public sealed record CreateOrderLineRequest(string? ProductSku, int Quantity, decimal UnitPrice);

/// <param name="Status">Target status: <c>Paid</c>, <c>Fulfilled</c> or <c>Cancelled</c>.</param>
public sealed record UpdateOrderStatusRequest(string? Status);

/// <param name="Id">Order ID.</param>
/// <param name="CustomerId">Owning customer.</param>
/// <param name="Status">Current status.</param>
/// <param name="CurrencyCode">ISO 4217.</param>
/// <param name="TotalAmount">Σ(quantity × unitPrice), computed by the server.</param>
/// <param name="CreatedAt">UTC.</param>
/// <param name="AllocatedAt">UTC time stock was allocated, or null.</param>
/// <param name="AllowedTransitions">Statuses this order can move to next, so clients only offer legal actions.</param>
/// <param name="LineItems">The order's lines.</param>
public sealed record OrderResponse(
    Guid Id,
    Guid CustomerId,
    OrderStatus Status,
    string CurrencyCode,
    decimal TotalAmount,
    DateTime CreatedAt,
    DateTime? AllocatedAt,
    IReadOnlyList<OrderStatus> AllowedTransitions,
    IReadOnlyList<OrderLineItemResponse> LineItems);

public sealed record OrderLineItemResponse(Guid Id, string ProductSku, int Quantity, decimal UnitPrice, decimal LineTotal);

/// <summary>A row in the order list (no line items, to keep list queries on the covering index).</summary>
public sealed record OrderSummaryResponse(
    Guid Id,
    Guid CustomerId,
    string CustomerName,
    OrderStatus Status,
    string CurrencyCode,
    decimal TotalAmount,
    DateTime CreatedAt);

/// <summary><c>GET /api/v1/orders?customerId=&amp;status=&amp;page=&amp;pageSize=&amp;sort=</c></summary>
public sealed class OrderListQuery : PageQuery
{
    public const string SortByCreatedAt = "createdAt";
    public const string SortByTotal = "total";

    /// <summary>Only this customer's orders.</summary>
    public Guid? CustomerId { get; init; }

    /// <summary>Pending, Paid, Fulfilled or Cancelled (case-insensitive).</summary>
    public string? Status { get; init; }

    /// <summary><c>createdAt</c> or <c>total</c>; prefix with <c>-</c> for descending. Default <c>-createdAt</c> (newest first).</summary>
    public string? Sort { get; init; }
}
