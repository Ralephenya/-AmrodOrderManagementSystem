using MassTransit;
using Microsoft.EntityFrameworkCore;
using OrderManagement.Contracts.Orders;
using OrderManagement.Domain.Orders;
using OrderManagement.Infrastructure.Observability;
using OrderManagement.Infrastructure.Persistence;
using OrderManagement.Worker.Allocation;

namespace OrderManagement.Worker.Consumers;

/// <summary>
/// Allocates stock for a new order, then fulfils it straight away if it has already been paid.
/// </summary>
/// <remarks>
/// Idempotent in two layers: the inbox skips a redelivered message ID entirely, and <see cref="Order.Allocate"/>
/// is a no-op for an order that is already allocated. A concurrent change by the API (e.g. a cancellation while
/// stock was being allocated) fails the save on rowversion. The retry then re-reads the order and sees the new state.
/// </remarks>
public sealed partial class OrderCreatedConsumer(
    AppDbContext db,
    IStockAllocator allocator,
    TimeProvider clock,
    OrderMetrics metrics,
    ILogger<OrderCreatedConsumer> logger) : IConsumer<OrderCreated>
{
    public async Task Consume(ConsumeContext<OrderCreated> context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var message = context.Message;
        var ct = context.CancellationToken;

        var order = await db.Orders.SingleOrDefaultAsync(o => o.Id == message.OrderId, ct)
            ?? throw new OrderNotFoundException(message.OrderId); // retried, then dead-lettered to order-created_error

        if (order.Status == OrderStatus.Cancelled)
        {
            LogSkippedCancelled(logger, order.Id);
            return;
        }

        if (order.AllocatedAt is not null)
        {
            LogAlreadyAllocated(logger, order.Id);
            return;
        }

        await allocator.AllocateAsync(order.Id, message.Lines, ct);

        var allocated = order.Allocate(clock);
        if (allocated.IsError)
        {
            LogSkippedCancelled(logger, order.Id);
            return;
        }

        await db.SaveChangesAsync(ct);
        metrics.OrderAllocated();

        if (order.Status == OrderStatus.Fulfilled)
        {
            metrics.OrderFulfilled("worker");
            LogFulfilled(logger, order.Id);
        }
        else
        {
            LogAllocated(logger, order.Id, order.Status);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Order {OrderId} allocated; status {Status}, fulfilment waits for payment")]
    private static partial void LogAllocated(ILogger logger, Guid orderId, OrderStatus status);

    [LoggerMessage(Level = LogLevel.Information, Message = "Order {OrderId} allocated and fulfilled (already paid)")]
    private static partial void LogFulfilled(ILogger logger, Guid orderId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Order {OrderId} was already allocated; nothing to do")]
    private static partial void LogAlreadyAllocated(ILogger logger, Guid orderId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Order {OrderId} is cancelled; stock not allocated")]
    private static partial void LogSkippedCancelled(ILogger logger, Guid orderId);
}
