using MassTransit;
using OrderManagement.Api.Common.Http;
using OrderManagement.Api.Services.Interfaces;
using OrderManagement.Contracts;
using OrderManagement.Contracts.Orders;
using OrderManagement.Domain.Orders;

namespace OrderManagement.Api.Services;

/// <summary>
/// MassTransit's scoped <see cref="IPublishEndpoint"/> is backed by the EF Core bus outbox (<c>UseBusOutbox</c>), so
/// "publish" here means "add an OutboxMessage row to the current DbContext". The outbox delivery service sends it to
/// RabbitMQ once the transaction commits.
/// </summary>
public sealed class OutboxOrderEventPublisher(
    IPublishEndpoint publisher,
    IHttpContextAccessor httpContext,
    TimeProvider clock) : IOrderEventPublisher
{
    public Task OrderCreatedAsync(Order order, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(order);

        return Publish(new OrderCreated(
            order.Id,
            order.CustomerId,
            order.CurrencyCode,
            order.TotalAmount,
            order.CreatedAt,
            [.. order.LineItems.Select(li => new OrderCreatedLine(li.ProductSku, li.Quantity))]), ct);
    }

    public Task OrderPaidAsync(Order order, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(order);

        return Publish(new OrderPaid(order.Id, clock.GetUtcNow().UtcDateTime), ct);
    }

    private Task Publish<T>(T message, CancellationToken ct)
        where T : class
    {
        var correlationId = httpContext.HttpContext is { } http ? CorrelationIdMiddleware.Get(http) : null;

        return publisher.Publish(message, context =>
        {
            if (correlationId is not null)
            {
                context.Headers.Set(MessageHeaderNames.CorrelationId, correlationId);
            }
        }, ct);
    }
}
