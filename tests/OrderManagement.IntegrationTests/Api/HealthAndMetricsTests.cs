using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.Metrics.Testing;
using OrderManagement.Infrastructure.Observability;
using OrderManagement.IntegrationTests.Fixtures;

namespace OrderManagement.IntegrationTests.Api;

[Collection(IntegrationTestCollection.Name)]
public class HealthAndMetricsTests(IntegrationTestFixture fixture)
{
    [Fact]
    public async Task Liveness_IsAnonymous_AndHasNoDependencies()
    {
        var response = await fixture.Factory.CreateClient().GetAsync("/healthz");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("status").GetString().ShouldBe("Healthy");
        CheckNames(body).ShouldBe(["self"]);
    }

    [Fact]
    public async Task Readiness_ChecksTheDatabaseAndTheMessageBus()
    {
        var response = await fixture.Factory.CreateClient().GetAsync("/readiness");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("status").GetString().ShouldBe("Healthy");
        CheckNames(body).ShouldContain("sql");
        CheckNames(body).ShouldContain(name => name.StartsWith("masstransit", StringComparison.OrdinalIgnoreCase));
        response.Headers.CacheControl!.NoStore.ShouldBeTrue();
    }

    [Fact]
    public async Task OutsideDevelopment_ProbesReturnOnlyTheStatus()
    {
        using var staging = fixture.Factory.WithWebHostBuilder(b => b.UseEnvironment("Staging"));

        var response = await staging.CreateClient().GetAsync("/readiness");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.EnumerateObject().Select(p => p.Name).ShouldBe(["status"]); // no dependency names or timings
        body.GetProperty("status").GetString().ShouldBe("Healthy");
    }

    [Fact]
    public async Task DatabaseDown_FailsReadinessQuickly_ButNotLiveness()
    {
        using var broken = fixture.Factory.WithWebHostBuilder(b => b.UseSetting(
            "ConnectionStrings:OrdersDb", "Server=tcp:127.0.0.1,1;Database=Nope;User Id=x;Password=y;Connect Timeout=2;TrustServerCertificate=True"));
        var client = broken.CreateClient();

        var stopwatch = Stopwatch.StartNew();
        var readiness = await client.GetAsync("/readiness");
        stopwatch.Stop();

        readiness.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        var body = await readiness.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("checks").EnumerateArray().Single(c => c.GetProperty("name").GetString() == "sql")
            .GetProperty("status").GetString().ShouldBe("Unhealthy");
        body.GetRawText().ShouldNotContain("Password"); // no connection details or exception text in the response
        stopwatch.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(10)); // no retry storm behind the probe
        (await client.GetAsync("/healthz")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task OrderLifecycle_IsCounted_ButReplaysAreNot()
    {
        var meters = fixture.Factory.Services.GetRequiredService<IMeterFactory>();
        using var created = new MetricCollector<long>(meters, OrderMetrics.MeterName, "orders.created");
        using var statusChanges = new MetricCollector<long>(meters, OrderMetrics.MeterName, "orders.status_changes");
        var client = fixture.Factory.CreateClientWithRoles("Orders.Write");

        var customer = await (await client.PostAsJsonAsync("/api/v1/customers",
            new { name = "Metrics Customer", email = $"metrics.{Guid.NewGuid():N}@example.co.sz", countryCode = "SZ" }))
            .Content.ReadFromJsonAsync<JsonElement>();
        var order = await (await client.PostAsJsonAsync("/api/v1/orders", new
        {
            customerId = customer.GetProperty("id").GetGuid(),
            currencyCode = "SZL",
            lineItems = new[] { new { productSku = "PEN-001", quantity = 1, unitPrice = 10m } },
        })).Content.ReadFromJsonAsync<JsonElement>();
        var orderId = order.GetProperty("id").GetGuid();
        var key = Guid.NewGuid().ToString();
        (await PutStatus(client, orderId, "Paid", key)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await PutStatus(client, orderId, "Paid", key)).Headers.Contains("Idempotent-Replayed").ShouldBeTrue();

        created.GetMeasurementSnapshot().Where(m => Equals(m.Tags["currency"], "SZL")).Sum(m => m.Value).ShouldBe(1);
        statusChanges.GetMeasurementSnapshot()
            .Count(m => Equals(m.Tags["from"], "Pending") && Equals(m.Tags["to"], "Paid")).ShouldBe(1); // replay not counted
    }

    private static Task<HttpResponseMessage> PutStatus(HttpClient client, Guid id, string status, string key)
    {
        var request = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/orders/{id}/status") { Content = JsonContent.Create(new { status }) };
        request.Headers.Add("Idempotency-Key", key);
        return client.SendAsync(request);
    }

    private static string[] CheckNames(JsonElement body) =>
        [.. body.GetProperty("checks").EnumerateArray().Select(c => c.GetProperty("name").GetString()!)];
}
