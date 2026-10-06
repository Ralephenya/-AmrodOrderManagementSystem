using OrderManagement.Domain.Orders;

namespace OrderManagement.Api.Services.Interfaces;

/// <summary>
/// Publishes order integration events. Call it <b>before</b> <c>SaveChanges</c>: the events are written to the
/// transactional outbox in the same DbContext, so they commit (or roll back) together with the change that caused them.
/// </summary>
public interface IOrderEventPublisher
{
    Task OrderCreatedAsync(Order order, CancellationToken ct);

    Task OrderPaidAsync(Order order, CancellationToken ct);
}
