using MassTransit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.Metrics.Testing;
using OrderManagement.Infrastructure.Observability;
using Microsoft.EntityFrameworkCore;
using OrderManagement.Contracts.Orders;
using OrderManagement.Domain.Orders;
using OrderManagement.IntegrationTests.Fixtures;
using static OrderManagement.IntegrationTests.TestData;

namespace OrderManagement.IntegrationTests.Messaging;

[Collection(IntegrationTestCollection.Name)]
public sealed class WorkerConsumerTests(IntegrationTestFixture fixture) : IAsyncLifetime
{
    private WorkerHarness _worker = null!;

    public async Task InitializeAsync() => _worker = await WorkerHarness.StartAsync(fixture.Database);

    public async Task DisposeAsync() => await _worker.DisposeAsync();

    [Fact]
    public async Task OrderCreated_AllocatesStock_AndLeavesAnUnpaidOrderPending()
    {
        var order = await SeedOrderAsync();

        await _worker.Publish(Created(order));

        var stored = await EventuallyAsync(order.Id, o => o.AllocatedAt is not null);
        stored.Status.ShouldBe(OrderStatus.Pending);
        stored.AllocatedAt!.Value.Kind.ShouldBe(DateTimeKind.Utc);
        _worker.Allocator.Calls.ShouldBe(1);
    }

    [Fact]
    public async Task OrderCreated_ForAnAlreadyPaidOrder_AllocatesAndFulfils_AndCountsBoth()
    {
        var meters = _worker.Services.GetRequiredService<System.Diagnostics.Metrics.IMeterFactory>();
        using var allocated = new MetricCollector<long>(meters, OrderMetrics.MeterName, "orders.allocated");
        using var fulfilled = new MetricCollector<long>(meters, OrderMetrics.MeterName, "orders.fulfilled");
        var order = await SeedOrderAsync(OrderStatus.Paid);

        await _worker.Publish(Created(order));

        (await EventuallyAsync(order.Id, o => o.Status == OrderStatus.Fulfilled)).AllocatedAt.ShouldNotBeNull();
        allocated.GetMeasurementSnapshot().Sum(m => m.Value).ShouldBe(1);
        fulfilled.GetMeasurementSnapshot().Single().Tags["source"].ShouldBe("worker");
    }

    [Fact]
    public async Task OrderPaid_AfterAllocation_Fulfils()
    {
        var order = await SeedOrderAsync();
        await _worker.Publish(Created(order));
        await EventuallyAsync(order.Id, o => o.AllocatedAt is not null);
        await MarkPaidAsync(order.Id);

        await _worker.Publish(new OrderPaid(order.Id, DateTime.UtcNow));

        await EventuallyAsync(order.Id, o => o.Status == OrderStatus.Fulfilled);
    }

    [Fact]
    public async Task OrderPaid_BeforeAllocation_LeavesItPaid_ForTheAllocationToFulfilLater()
    {
        var order = await SeedOrderAsync(OrderStatus.Paid);

        await _worker.Publish(new OrderPaid(order.Id, DateTime.UtcNow));
        (await _worker.Harness.Consumed.Any<OrderPaid>(m => m.Context.Message.OrderId == order.Id)).ShouldBeTrue();

        (await LoadAsync(order.Id)).Status.ShouldBe(OrderStatus.Paid);
    }

    [Fact]
    public async Task OrderCreated_ForACancelledOrder_IsAcknowledgedWithoutAllocating()
    {
        var order = await SeedOrderAsync(OrderStatus.Cancelled);

        await _worker.Publish(Created(order));

        (await _worker.Harness.Consumed.Any<OrderCreated>(m => m.Context.Message.OrderId == order.Id)).ShouldBeTrue();
        (await _worker.Harness.Published.Any<Fault<OrderCreated>>(f => f.Context.Message.Message.OrderId == order.Id)).ShouldBeFalse();
        _worker.Allocator.Calls.ShouldBe(0);
        (await LoadAsync(order.Id)).AllocatedAt.ShouldBeNull();
    }

    [Fact]
    public async Task RedeliveredMessage_SameMessageId_IsProcessedOnce()
    {
        var order = await SeedOrderAsync();
        var messageId = NewId.NextGuid();

        await _worker.Publish(Created(order), messageId);
        await EventuallyAsync(order.Id, o => o.AllocatedAt is not null);
        var firstAllocation = (await LoadAsync(order.Id)).AllocatedAt;

        // At-least-once delivery: the broker hands the same message over again.
        await _worker.Publish(Created(order), messageId);

        // The inbox records each receipt of a message ID. Once it has seen the second one, the duplicate has been handled.
        await Eventually.UntilAsync(async () =>
        {
            await using var db = fixture.CreateDbContext();
            return await db.Database
                .SqlQuery<int>($"SELECT ReceiveCount AS [Value] FROM InboxState WHERE MessageId = {messageId}")
                .SingleOrDefaultAsync() >= 2;
        }, "the inbox to record the redelivery");

        _worker.Allocator.Calls.ShouldBe(1); // the inbox skipped the duplicate before the consumer ran
        (await LoadAsync(order.Id)).AllocatedAt.ShouldBe(firstAllocation);
    }

    [Fact]
    public async Task TransientFailure_IsRetriedTheConfiguredNumberOfTimes_ThenDeadLettered()
    {
        var order = await SeedOrderAsync();
        _worker.Allocator.FailFor(order.Id);

        await _worker.Publish(Created(order));

        (await _worker.Harness.Published.Any<Fault<OrderCreated>>(f => f.Context.Message.Message.OrderId == order.Id)).ShouldBeTrue();
        // 1 attempt + Messaging:Retry:Limit (2) retries, each a fresh attempt through inbox and consumer.
        _worker.Allocator.CallsFor(order.Id).ShouldBe(1 + WorkerHarness.RetryLimit);
        (await LoadAsync(order.Id)).AllocatedAt.ShouldBeNull();
    }

    [Fact]
    public async Task UnknownOrder_IsRetried_ThenDeadLettered()
    {
        var unknown = Guid.NewGuid();

        await _worker.Publish(new OrderCreated(unknown, Guid.NewGuid(), "ZAR", 10m, DateTime.UtcNow, [new("PEN", 1)]));

        // After the retry limit, MassTransit publishes Fault<T> and moves the message to order-created_error.
        (await _worker.Harness.Published.Any<Fault<OrderCreated>>(f => f.Context.Message.Message.OrderId == unknown)).ShouldBeTrue();
        var fault = _worker.Harness.Published.Select<Fault<OrderCreated>>(f => f.Context.Message.Message.OrderId == unknown).First();
        fault.Context.Message.Exceptions.Single().ExceptionType.ShouldEndWith("OrderNotFoundException");
        _worker.Allocator.Calls.ShouldBe(0);
    }

    private static OrderCreated Created(Order order) => new(
        order.Id, order.CustomerId, order.CurrencyCode, order.TotalAmount, order.CreatedAt,
        [.. order.LineItems.Select(li => new OrderCreatedLine(li.ProductSku, li.Quantity))]);

    private async Task<Order> SeedOrderAsync(OrderStatus status = OrderStatus.Pending)
    {
        await using var db = fixture.CreateDbContext();
        var customer = NewCustomer();
        db.Customers.Add(customer);
        var order = NewOrder(customer);
        if (status is OrderStatus.Paid or OrderStatus.Cancelled)
        {
            order.TransitionTo(status).IsError.ShouldBeFalse();
        }

        db.Orders.Add(order);
        await db.SaveChangesAsync();
        return order;
    }

    private async Task MarkPaidAsync(Guid orderId)
    {
        await using var db = fixture.CreateDbContext();
        var order = await db.Orders.SingleAsync(o => o.Id == orderId);
        order.TransitionTo(OrderStatus.Paid).IsError.ShouldBeFalse();
        await db.SaveChangesAsync();
    }

    private async Task<Order> LoadAsync(Guid orderId)
    {
        await using var db = fixture.CreateDbContext();
        return await db.Orders.AsNoTracking().SingleAsync(o => o.Id == orderId);
    }

    /// <summary>Consumers commit asynchronously; poll the database until the expected state appears.</summary>
    private async Task<Order> EventuallyAsync(Guid orderId, Func<Order, bool> condition)
    {
        Order? last = null;
        await Eventually.UntilAsync(async () => condition(last = await LoadAsync(orderId)), $"order {orderId} to change");
        return last!;
    }
}
