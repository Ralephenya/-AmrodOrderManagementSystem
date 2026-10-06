using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Time.Testing;
using OrderManagement.Domain.Customers;
using OrderManagement.Domain.Orders;
using OrderManagement.IntegrationTests.Fixtures;

namespace OrderManagement.IntegrationTests.Api;

/// <summary>
/// The Dapper reports against real SQL Server. Seeds Mauritian customers (MUR), a country no other test uses, so the
/// "every customer in a MUR-accepting country" ranking is fully known.
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public class ReportsApiTests(IntegrationTestFixture fixture) : IAsyncLifetime
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    // xUnit creates a new instance per test, but the database lives for the whole run: seed once, share the result.
    private static readonly SemaphoreSlim SeedLock = new(1, 1);
    private static Seeded? _seeded;

    private readonly HttpClient _admin = fixture.Factory.CreateClientWithRoles("Orders.Admin");

    private Seeded Data => _seeded!;

    public async Task InitializeAsync()
    {
        await SeedLock.WaitAsync();
        try
        {
            _seeded ??= await SeedAsync();
        }
        finally
        {
            SeedLock.Release();
        }
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<Seeded> SeedAsync()
    {
        await using var db = fixture.CreateDbContext();
        var bigSpender = Customer.Create("Port Louis Traders", $"pl.{Guid.NewGuid():N}@example.mu", "MU", TestData.Clock()).Value;
        var smallSpender = Customer.Create("Grand Baie Gifts", $"gb.{Guid.NewGuid():N}@example.mu", "MU", TestData.Clock()).Value;
        var neverOrdered = Customer.Create("Curepipe Cafe", $"cc.{Guid.NewGuid():N}@example.mu", "MU", TestData.Clock()).Value;
        db.Customers.AddRange(bigSpender, smallSpender, neverOrdered);

        var outsideWindow = Seed(db, bigSpender, 400m, daysAgo: 200, OrderStatus.Paid);    // counts for running totals, not top-spenders
        var fulfilled = Seed(db, bigSpender, 50m, daysAgo: 10, OrderStatus.Fulfilled);
        var paid = Seed(db, bigSpender, 100m, daysAgo: 5, OrderStatus.Paid);
        Seed(db, bigSpender, 999m, daysAgo: 3, OrderStatus.Cancelled);                     // never spend
        Seed(db, bigSpender, 77m, daysAgo: 2, OrderStatus.Pending);                        // not paid yet
        Seed(db, smallSpender, 20m, daysAgo: 1, OrderStatus.Paid);
        await db.SaveChangesAsync();

        return new Seeded(bigSpender.Id, neverOrdered.Id, [outsideWindow.Id, fulfilled.Id, paid.Id]);
    }

    private sealed record Seeded(Guid BigSpender, Guid NeverOrdered, IReadOnlyList<Guid> BigSpenderOrdersOldestFirst);

    [Fact]
    public async Task TopSpenders_RanksPaidAndFulfilledSpendInTheWindow_IncludingCustomersWhoSpentNothing()
    {
        var report = await _admin.GetFromJsonAsync<JsonElement>("/api/v1/reports/top-spenders?currency=MUR&days=90");

        report.GetProperty("currencyCode").GetString().ShouldBe("MUR");
        var rows = report.GetProperty("customers").EnumerateArray()
            .Select(c => (Rank: c.GetProperty("rank").GetInt32(), Name: c.GetProperty("name").GetString(),
                Spend: c.GetProperty("totalSpend").GetDecimal(), Orders: c.GetProperty("orderCount").GetInt32()))
            .ToList();

        rows.ShouldBe(
        [
            (1, "Port Louis Traders", 150.00m, 2), // 100 Paid + 50 Fulfilled; the 400 is outside 90 days; Cancelled/Pending excluded
            (2, "Grand Baie Gifts", 20.00m, 1),
            (3, "Curepipe Cafe", 0m, 0),           // no orders at all: still listed, with 0 (LEFT JOIN + COALESCE)
        ]);
    }

    [Fact]
    public async Task TopSpenders_WiderWindow_IncludesOlderOrders()
    {
        var report = await _admin.GetFromJsonAsync<JsonElement>("/api/v1/reports/top-spenders?currency=MUR&days=366&top=1");

        var top = report.GetProperty("customers").EnumerateArray().Single();
        top.GetProperty("name").GetString().ShouldBe("Port Louis Traders");
        top.GetProperty("totalSpend").GetDecimal().ShouldBe(550.00m);
    }

    [Fact]
    public async Task RunningTotals_AccumulateSpendInOrderOfCreation()
    {
        var report = await _admin.GetFromJsonAsync<JsonElement>(
            $"/api/v1/reports/customers/{Data.BigSpender}/running-totals?currency=mur");

        report.GetProperty("currencyCode").GetString().ShouldBe("MUR");
        var points = report.GetProperty("orders").EnumerateArray()
            .Select(o => (o.GetProperty("orderId").GetGuid(), o.GetProperty("totalAmount").GetDecimal(), o.GetProperty("runningTotal").GetDecimal()))
            .ToList();
        points.ShouldBe(
        [
            (Data.BigSpenderOrdersOldestFirst[0], 400m, 400m),
            (Data.BigSpenderOrdersOldestFirst[1], 50m, 450m),
            (Data.BigSpenderOrdersOldestFirst[2], 100m, 550m),
        ]);
        report.GetProperty("orders")[0].GetProperty("createdAt").GetString()!.ShouldEndWith("Z");
    }

    [Fact]
    public async Task RunningTotals_CustomerWithNoSpend_IsAnEmptyList()
    {
        var report = await _admin.GetFromJsonAsync<JsonElement>(
            $"/api/v1/reports/customers/{Data.NeverOrdered}/running-totals?currency=MUR");

        report.GetProperty("orders").GetArrayLength().ShouldBe(0);
    }

    [Fact]
    public async Task RunningTotals_CurrencyTheCustomerCannotUse_Returns400()
    {
        var response = await _admin.GetAsync($"/api/v1/reports/customers/{Data.BigSpender}/running-totals?currency=ZAR");

        (await response.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "validation_failed"))
            .FieldErrors("currencyCode").ShouldBe(["Customers in Mauritius can only order in MUR, not ZAR."]);
    }

    [Fact]
    public async Task RunningTotals_UnknownCustomer_Returns404()
    {
        var response = await _admin.GetAsync($"/api/v1/reports/customers/{Guid.NewGuid()}/running-totals?currency=MUR");

        await response.ShouldBeProblemAsync(HttpStatusCode.NotFound, "not_found");
    }

    [Theory]
    [InlineData("days=90", "currency", "Currency is required. Use a three-letter currency code such as ZAR.")]
    [InlineData("currency=EUR", "currency", "'EUR' isn't a currency we accept. Use a SADC currency code such as ZAR, BWP or NAD.")]
    [InlineData("currency=ZAR&days=0", "days", "Days must be between 1 and 366.")]
    [InlineData("currency=ZAR&top=101", "top", "Top must be between 1 and 100.")]
    public async Task TopSpenders_InvalidQuery_Returns400(string queryString, string field, string message)
    {
        var response = await _admin.GetAsync($"/api/v1/reports/top-spenders?{queryString}");

        (await response.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "validation_failed")).FieldErrors(field).ShouldBe([message]);
    }

    [Theory]
    [InlineData("Orders.Read")]
    [InlineData("Orders.Write")]
    public async Task Reports_RequireTheAdminRole(string role)
    {
        var response = await fixture.Factory.CreateClientWithRoles(role).GetAsync("/api/v1/reports/top-spenders?currency=ZAR");

        await response.ShouldBeProblemAsync(HttpStatusCode.Forbidden, "forbidden");
    }

    private static Order Seed(
        Infrastructure.Persistence.AppDbContext db, Customer customer, decimal amount, int daysAgo, OrderStatus status)
    {
        var clock = new FakeTimeProvider(Now.AddDays(-daysAgo));
        var order = Order.Create(customer, "MUR", [new NewOrderLine("SKU-1", 1, amount)], clock).Value;
        switch (status)
        {
            case OrderStatus.Paid:
                order.TransitionTo(OrderStatus.Paid);
                break;
            case OrderStatus.Fulfilled:
                order.TransitionTo(OrderStatus.Paid);
                order.Allocate(clock);
                break;
            case OrderStatus.Cancelled:
                order.TransitionTo(OrderStatus.Cancelled);
                break;
        }

        order.Status.ShouldBe(status);
        db.Orders.Add(order);
        return order;
    }
}
