using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using OrderManagement.Api.Auth;
using OrderManagement.IntegrationTests.Fixtures;

namespace OrderManagement.IntegrationTests.Api;

[Collection(IntegrationTestCollection.Name)]
public class HttpHardeningTests(IntegrationTestFixture fixture)
{
    private const string Countries = "/api/v1/reference/countries";

    [Fact]
    public async Task CorrelationId_FromTheCaller_IsEchoedAndPutInProblems()
    {
        var client = fixture.Factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Correlation-ID", "web-1234_abc.5");

        var response = await client.GetAsync(Countries); // 401: no token

        response.Headers.GetValues("X-Correlation-ID").Single().ShouldBe("web-1234_abc.5");
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("correlationId").GetString().ShouldBe("web-1234_abc.5");
    }

    [Fact]
    public async Task CorrelationId_IsGenerated_WhenMissing()
    {
        var response = await fixture.Factory.CreateClientWithRoles("Orders.Read").GetAsync(Countries);

        response.Headers.GetValues("X-Correlation-ID").Single().ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task GeneratedCorrelationId_IsTheSameInTheHeaderAndTheProblemBody()
    {
        var response = await fixture.Factory.CreateClient().GetAsync(Countries); // 401: no token

        var header = response.Headers.GetValues("X-Correlation-ID").Single();
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("correlationId").GetString().ShouldBe(header);
    }

    [Fact]
    public async Task CorrelationId_WithUnsafeCharacters_IsReplaced()
    {
        var client = fixture.Factory.CreateClientWithRoles("Orders.Read");
        client.DefaultRequestHeaders.TryAddWithoutValidation("X-Correlation-ID", "abc\" OR 1=1; <script>");

        var response = await client.GetAsync(Countries);

        response.Headers.GetValues("X-Correlation-ID").Single().ShouldNotContain("<script>");
    }

    [Fact]
    public async Task SecurityHeaders_AreOnEveryResponse()
    {
        var response = await fixture.Factory.CreateClientWithRoles("Orders.Read").GetAsync(Countries);

        response.Headers.GetValues("X-Content-Type-Options").Single().ShouldBe("nosniff");
        response.Headers.GetValues("X-Frame-Options").Single().ShouldBe("DENY");
        response.Headers.GetValues("Referrer-Policy").Single().ShouldBe("no-referrer");
        response.Headers.GetValues("Content-Security-Policy").Single().ShouldContain("default-src 'none'");
        response.Headers.Contains("Server").ShouldBeFalse();
    }

    [Fact]
    public async Task Cors_AllowsTheConfiguredWebOrigin()
    {
        using var preflight = Preflight(ApiFactory.AllowedOrigin);

        var response = await fixture.Factory.CreateClient().SendAsync(preflight);

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        response.Headers.GetValues("Access-Control-Allow-Origin").Single().ShouldBe(ApiFactory.AllowedOrigin);
        response.Headers.GetValues("Access-Control-Allow-Headers").Single().ShouldContain("idempotency-key",
            Case.Insensitive);
    }

    [Fact]
    public async Task Cors_RejectsAnyOtherOrigin()
    {
        using var preflight = Preflight("https://evil.example.com");

        var response = await fixture.Factory.CreateClient().SendAsync(preflight);

        response.Headers.Contains("Access-Control-Allow-Origin").ShouldBeFalse();
    }

    [Fact]
    public async Task WriteRateLimit_Returns429ProblemWithRetryAfter()
    {
        using var limited = fixture.Factory.WithWebHostBuilder(b => b.UseSetting("RateLimiting:WritesPerMinute", "2"));
        var client = limited.CreateClient();

        (await client.PostAsync("/api/v1/probes/write", null)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await client.PostAsync("/api/v1/probes/write", null)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var third = await client.PostAsync("/api/v1/probes/write", null);

        await third.ShouldBeProblemAsync(HttpStatusCode.TooManyRequests, "rate_limited");
        third.Headers.RetryAfter.ShouldNotBeNull();
    }

    [Fact]
    public async Task GlobalRateLimit_CoversReadsAndGraphQL_PerUser()
    {
        using var limited = fixture.Factory.WithWebHostBuilder(b => b.UseSetting("RateLimiting:RequestsPerMinute", "2"));
        var steve = ClientAs(limited, "Rate Limit Steve");

        (await steve.GetAsync(Countries)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await steve.PostAsJsonAsync("/graphql", new { query = "{ __typename }" })).StatusCode.ShouldBe(HttpStatusCode.OK);
        var third = await steve.GetAsync(Countries);

        await third.ShouldBeProblemAsync(HttpStatusCode.TooManyRequests, "rate_limited");
        third.Headers.RetryAfter.ShouldNotBeNull();

        // Another user has a budget of their own.
        (await ClientAs(limited, "Rate Limit Thandi").GetAsync(Countries)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GlobalRateLimit_NeverAppliesToHealthProbes()
    {
        using var limited = fixture.Factory.WithWebHostBuilder(b => b.UseSetting("RateLimiting:RequestsPerMinute", "1"));
        var client = limited.CreateClient();

        for (var i = 0; i < 3; i++)
        {
            (await client.GetAsync("/healthz")).StatusCode.ShouldBe(HttpStatusCode.OK);
        }
    }

    private static HttpClient ClientAs(WebApplicationFactory<Program> factory, string userName)
    {
        var client = factory.CreateClient();
        var token = factory.Services.GetRequiredService<MockTokenIssuer>().Issue(userName, ["Orders.Read"]);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static HttpRequestMessage Preflight(string origin)
    {
        var request = new HttpRequestMessage(HttpMethod.Options, "/api/v1/reference/countries");
        request.Headers.Add("Origin", origin);
        request.Headers.Add("Access-Control-Request-Method", "PUT");
        request.Headers.Add("Access-Control-Request-Headers", "authorization,idempotency-key");
        return request;
    }
}
