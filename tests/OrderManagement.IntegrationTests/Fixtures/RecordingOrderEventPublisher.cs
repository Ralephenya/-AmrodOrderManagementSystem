using OrderManagement.Api.Services.Interfaces;
using OrderManagement.Domain.Orders;

namespace OrderManagement.IntegrationTests.Fixtures;

/// <summary>Stands in for the outbox publisher when a test builds <c>OrderService</c> by hand.</summary>
internal sealed class RecordingOrderEventPublisher : IOrderEventPublisher
{
    public List<(string Event, Guid OrderId)> Published { get; } = [];

    public Task OrderCreatedAsync(Order order, CancellationToken ct)
    {
        Published.Add(("OrderCreated", order.Id));
        return Task.CompletedTask;
    }

    public Task OrderPaidAsync(Order order, CancellationToken ct)
    {
        Published.Add(("OrderPaid", order.Id));
        return Task.CompletedTask;
    }
}
