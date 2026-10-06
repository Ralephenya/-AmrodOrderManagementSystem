using MassTransit;
using Microsoft.EntityFrameworkCore;
using OrderManagement.Contracts.Orders;
using OrderManagement.Infrastructure.Persistence;

namespace OrderManagement.Worker.Consumers;

/// <summary>Fulfils a paid order whose stock is already allocated. If allocation is still running, it fulfils the order itself.</summary>
public sealed partial class OrderPaidConsumer(AppDbContext db, ILogger<OrderPaidConsumer> logger) : IConsumer<OrderPaid>
{
    public async Task Consume(ConsumeContext<OrderPaid> context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var ct = context.CancellationToken;

        var order = await db.Orders.SingleOrDefaultAsync(o => o.Id == context.Message.OrderId, ct)
            ?? throw new OrderNotFoundException(context.Message.OrderId);

        if (!order.FulfilIfReady())
        {
            LogWaiting(logger, order.Id, order.Status, order.AllocatedAt is not null);
            return;
        }

        await db.SaveChangesAsync(ct);
        LogFulfilled(logger, order.Id);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Order {OrderId} paid and fulfilled")]
    private static partial void LogFulfilled(ILogger logger, Guid orderId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Order {OrderId} not fulfilled yet (status {Status}, allocated {Allocated})")]
    private static partial void LogWaiting(ILogger logger, Guid orderId, Domain.Orders.OrderStatus status, bool allocated);
}
