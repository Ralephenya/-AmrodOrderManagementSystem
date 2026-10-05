using ErrorOr;
using OrderManagement.Api.Common.Idempotency;
using OrderManagement.Api.Common.Paging;
using OrderManagement.Api.Contracts.Orders;
using OrderManagement.Domain.Orders;

namespace OrderManagement.Api.Services.Interfaces;

public interface IOrderService
{
    Task<ErrorOr<OrderResult>> CreateAsync(CreateOrderRequest request, CancellationToken ct);

    Task<ErrorOr<OrderResult>> GetByIdAsync(Guid id, CancellationToken ct);

    /// <summary>Only the current ETag (one indexed lookup), so a conditional GET can answer 304 without loading lines.</summary>
    Task<string?> GetETagAsync(Guid id, CancellationToken ct);

    Task<PagedResult<OrderSummaryResponse>> ListAsync(OrderListQuery query, CancellationToken ct);

    /// <summary>
    /// Validated, idempotent status change. A repeat of the same request under the same key returns the original
    /// outcome (<see cref="StatusChangeResult.Replayed"/>) without changing anything.
    /// </summary>
    Task<StatusChangeResult> ChangeStatusAsync(
        Guid id,
        OrderStatus target,
        string? ifMatch,
        IdempotencyRequest idempotency,
        CancellationToken ct);
}

/// <param name="Order">The order representation.</param>
/// <param name="ETag">Its current ETag, or null for a replayed response (fetch the order for a fresh one).</param>
public sealed record OrderResult(OrderResponse Order, string? ETag);

public sealed record StatusChangeResult(ErrorOr<OrderResult> Outcome, bool Replayed);
