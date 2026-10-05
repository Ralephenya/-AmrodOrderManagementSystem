using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Time.Testing;
using OrderManagement.Api.Common.Idempotency;
using OrderManagement.Api.Services;
using OrderManagement.Domain.Orders;
using OrderManagement.Infrastructure.Persistence;
using OrderManagement.IntegrationTests.Fixtures;
using static OrderManagement.IntegrationTests.TestData;

namespace OrderManagement.IntegrationTests.Persistence;

/// <summary>
/// Forces the interleavings a concurrent HTTP test can only hope to hit: something else commits between our read of the
/// order and our SaveChanges.
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public class OrderStatusRaceTests(IntegrationTestFixture fixture)
{
    [Fact]
    public async Task RetryRacingItsOriginal_GetsTheOriginalsOutcomeReplayed()
    {
        var orderId = await SeedPendingOrderAsync();
        var request = NewIdempotencyRequest();

        // The "original" request runs to completion (same key, same request) just before our save.
        var interceptor = new BeforeSave(async () =>
        {
            await using var other = fixture.CreateDbContext();
            var result = await new OrderService(other, TimeProvider.System)
                .ChangeStatusAsync(orderId, OrderStatus.Paid, null, request, CancellationToken.None);
            result.Outcome.IsError.ShouldBeFalse();
        });

        var outcome = await ServiceWith(interceptor).ChangeStatusAsync(orderId, OrderStatus.Paid, null, request, CancellationToken.None);

        interceptor.Ran.ShouldBeTrue();
        outcome.Replayed.ShouldBeTrue();
        outcome.Outcome.IsError.ShouldBeFalse();
        outcome.Outcome.Value.Order.Status.ShouldBe(OrderStatus.Paid);
    }

    [Fact]
    public async Task ADifferentChangeWinningTheRace_Is409_AndNothingIsRecorded()
    {
        var orderId = await SeedPendingOrderAsync();
        var request = NewIdempotencyRequest();

        // Someone else cancels the order (no idempotency key involved) between our read and our write.
        var interceptor = new BeforeSave(async () =>
        {
            await using var other = fixture.CreateDbContext();
            var order = await other.Orders.SingleAsync(o => o.Id == orderId);
            order.TransitionTo(OrderStatus.Cancelled);
            await other.SaveChangesAsync();
        });

        var outcome = await ServiceWith(interceptor).ChangeStatusAsync(orderId, OrderStatus.Paid, null, request, CancellationToken.None);

        outcome.Replayed.ShouldBeFalse();
        outcome.Outcome.FirstError.Code.ShouldBe("Order.ConcurrencyConflict");
        await using var db = fixture.CreateDbContext();
        (await db.IdempotencyKeys.AnyAsync(r => r.ClientId == request.ClientId && r.Key == request.Key)).ShouldBeFalse();
        (await db.Orders.SingleAsync(o => o.Id == orderId)).Status.ShouldBe(OrderStatus.Cancelled);
    }

    [Fact]
    public async Task ExpiredKey_IsEvaluatedAgain_NotReplayed()
    {
        var orderId = await SeedPendingOrderAsync();
        var request = NewIdempotencyRequest();
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);

        await using (var db = fixture.CreateDbContext())
        {
            (await new OrderService(db, clock).ChangeStatusAsync(orderId, OrderStatus.Paid, null, request, CancellationToken.None))
                .Outcome.IsError.ShouldBeFalse();
        }

        clock.Advance(IdempotencyRequest.RetentionPeriod + TimeSpan.FromMinutes(1));

        await using (var db = fixture.CreateDbContext())
        {
            var again = await new OrderService(db, clock).ChangeStatusAsync(orderId, OrderStatus.Paid, null, request, CancellationToken.None);

            again.Replayed.ShouldBeFalse();
            again.Outcome.FirstError.Code.ShouldBe("Order.AlreadyInStatus");
        }
    }

    private OrderService ServiceWith(BeforeSave interceptor) => new(
        new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(fixture.Database.ConnectionString)
            .AddInterceptors(interceptor)
            .Options),
        TimeProvider.System);

    private async Task<Guid> SeedPendingOrderAsync()
    {
        await using var db = fixture.CreateDbContext();
        var customer = NewCustomer();
        db.Customers.Add(customer);
        var order = NewOrder(customer);
        db.Orders.Add(order);
        await db.SaveChangesAsync();
        return order.Id;
    }

    private static IdempotencyRequest NewIdempotencyRequest() =>
        new("race-test-client", Guid.NewGuid().ToString(), new string('A', 64));

    private sealed class BeforeSave(Func<Task> competitor) : SaveChangesInterceptor
    {
        public bool Ran { get; private set; }

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (!Ran)
            {
                Ran = true;
                await competitor();
            }

            return await base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }
}
