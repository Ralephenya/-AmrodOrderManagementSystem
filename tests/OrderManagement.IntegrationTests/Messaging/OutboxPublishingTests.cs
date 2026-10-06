using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MassTransit.EntityFrameworkCoreIntegration;
using Microsoft.Extensions.DependencyInjection;
using OrderManagement.Api.Services.Interfaces;
using OrderManagement.Contracts.Orders;
using OrderManagement.Infrastructure.Persistence;
using OrderManagement.IntegrationTests.Fixtures;

namespace OrderManagement.IntegrationTests.Messaging;

/// <summary>
/// The API's side: events leave through the transactional outbox, carrying the request's correlation ID.
/// Assertions use <see cref="PublishedEvents"/>, which only sees messages the outbox actually delivered after commit.
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public class OutboxPublishingTests(IntegrationTestFixture fixture)
{
    private readonly HttpClient _client = fixture.Factory.CreateClientWithRoles("Orders.Write");

    [Fact]
    public async Task CreatingAnOrder_DeliversOrderCreated_WithTheCorrelationId()
    {
        var customerId = await CreateCustomerAsync();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/orders")
        {
            Content = JsonContent.Create(new
            {
                customerId,
                currencyCode = "ZAR",
                lineItems = new[]
                {
                    new { productSku = "PEN-001", quantity = 3, unitPrice = 19.99m },
                    new { productSku = "MUG-002", quantity = 2, unitPrice = 85.50m },
                },
            }),
        };
        request.Headers.Add("X-Correlation-ID", "web-checkout-42");

        var response = await _client.SendAsync(request);
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var orderId = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var delivered = await fixture.Events.WaitForAsync<OrderCreated>(m => m.OrderId == orderId);
        var message = (OrderCreated)delivered.Message;
        message.CustomerId.ShouldBe(customerId);
        message.TotalAmount.ShouldBe(230.97m);
        message.CurrencyCode.ShouldBe("ZAR");
        message.Lines.Select(l => (l.ProductSku, l.Quantity)).ShouldBe([("PEN-001", 3), ("MUG-002", 2)], ignoreOrder: true);
        delivered.CorrelationId.ShouldBe("web-checkout-42");
    }

    [Fact]
    public async Task Publishing_StagesAnOutboxRowInTheSameDbContext_InsteadOfSendingDirectly()
    {
        // Regression guard: AddMassTransitTestHarness() once replaced the scoped publisher in tests and
        // silently bypassed the outbox. If publish doesn't stage a row here, the dual-write problem is back.
        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var events = scope.ServiceProvider.GetRequiredService<IOrderEventPublisher>();
        var customer = TestData.NewCustomer();
        db.Customers.Add(customer);
        var order = TestData.NewOrder(customer);
        db.Orders.Add(order);

        await events.OrderCreatedAsync(order, CancellationToken.None);

        db.ChangeTracker.Entries<OutboxMessage>().Count().ShouldBe(1);
    }

    [Fact]
    public async Task AnEventForAChangeThatIsNeverSaved_IsNeverDelivered()
    {
        var orderId = await StageOrderAndPublishAsync(save: false);
        await Task.Delay(1000); // several outbox polling cycles

        fixture.Events.Of<OrderCreated>().Where(m => m.OrderId == orderId).ShouldBeEmpty();
    }

    [Fact]
    public async Task AnEventForAChangeThatIsSaved_IsDeliveredAfterCommit()
    {
        var orderId = await StageOrderAndPublishAsync(save: true);

        await fixture.Events.WaitForAsync<OrderCreated>(m => m.OrderId == orderId);
    }

    [Fact]
    public async Task PayingAnOrder_DeliversOrderPaidOnce_EvenWhenTheRequestIsReplayed()
    {
        var orderId = await CreateOrderAsync();
        var key = Guid.NewGuid().ToString();

        (await PutStatus(orderId, "Paid", key)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await PutStatus(orderId, "Paid", key)).Headers.Contains("Idempotent-Replayed").ShouldBeTrue();

        await fixture.Events.WaitForAsync<OrderPaid>(m => m.OrderId == orderId);
        await Task.Delay(1000); // give a (wrong) second delivery time to arrive
        fixture.Events.Of<OrderPaid>().Count(m => m.OrderId == orderId).ShouldBe(1);
    }

    [Fact]
    public async Task CancellingAnOrder_DeliversNoPaymentEvent()
    {
        var orderId = await CreateOrderAsync();
        await fixture.Events.WaitForAsync<OrderCreated>(m => m.OrderId == orderId);

        (await PutStatus(orderId, "Cancelled", Guid.NewGuid().ToString())).StatusCode.ShouldBe(HttpStatusCode.OK);
        await Task.Delay(1000);

        fixture.Events.Of<OrderPaid>().Where(m => m.OrderId == orderId).ShouldBeEmpty();
    }

    private async Task<Guid> StageOrderAndPublishAsync(bool save)
    {
        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var events = scope.ServiceProvider.GetRequiredService<IOrderEventPublisher>();

        var customer = TestData.NewCustomer();
        db.Customers.Add(customer);
        var order = TestData.NewOrder(customer);
        db.Orders.Add(order);

        await events.OrderCreatedAsync(order, CancellationToken.None);
        if (save)
        {
            await db.SaveChangesAsync();
        }

        return order.Id;
    }

    private async Task<Guid> CreateCustomerAsync()
    {
        var response = await _client.PostAsJsonAsync("/api/v1/customers",
            new { name = "Outbox Customer", email = $"outbox.{Guid.NewGuid():N}@example.co.za", countryCode = "ZA" });
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private async Task<Guid> CreateOrderAsync()
    {
        var response = await _client.PostAsJsonAsync("/api/v1/orders", new
        {
            customerId = await CreateCustomerAsync(),
            currencyCode = "ZAR",
            lineItems = new[] { new { productSku = "PEN-001", quantity = 1, unitPrice = 10m } },
        });
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private Task<HttpResponseMessage> PutStatus(Guid id, string status, string key)
    {
        var request = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/orders/{id}/status") { Content = JsonContent.Create(new { status }) };
        request.Headers.Add("Idempotency-Key", key);
        return _client.SendAsync(request);
    }
}
