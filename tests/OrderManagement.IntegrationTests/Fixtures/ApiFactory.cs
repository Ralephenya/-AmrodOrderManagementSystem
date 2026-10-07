using System.Net.Http.Headers;
using FluentValidation;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using OrderManagement.Api.Auth;
using OrderManagement.Infrastructure;
using OrderManagement.IntegrationTests.Api.Probes;

namespace OrderManagement.IntegrationTests.Fixtures;

/// <summary>Hosts the real API in memory against the test database, with mock Entra auth.</summary>
public sealed class ApiFactory(SqlServerDatabase database) : WebApplicationFactory<Program>
{
    public const string AllowedOrigin = "http://localhost:5173";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting($"ConnectionStrings:{DependencyInjection.ConnectionStringName}", database.ConnectionString);

        // The fixture already applied migrations; the app must not try again.
        builder.UseSetting("Database:ApplyMigrationsOnStartup", "false");

        builder.UseSetting("Auth:Mode", "Mock");
        builder.UseSetting("Auth:Mock:SigningKey", "integration-tests-signing-key-0123456789abcdef");
        builder.UseSetting("Cors:AllowedOrigins:0", AllowedOrigin);

        // Every test shares one mock user, so the per-user limits are raised; HttpHardeningTests lowers them again.
        builder.UseSetting("RateLimiting:RequestsPerMinute", "100000");
        builder.UseSetting("RateLimiting:WritesPerMinute", "100000");

        // No broker in tests: the production messaging registration (including the EF bus outbox) runs on the in-memory
        // transport. Deliberately NOT AddMassTransitTestHarness(): it replaces the scoped publisher and silently bypasses
        // the outbox. Deliveries are observed through PublishedEvents instead. A short query delay keeps them fast.
        builder.UseSetting("Messaging:Transport", "InMemory");
        builder.UseSetting("Messaging:OutboxQueryDelay", "00:00:00.100");

        // Test-only endpoints that exercise the cross-cutting behaviour (see Api/Probes).
        builder.ConfigureTestServices(services =>
        {
            services.AddControllers().AddApplicationPart(typeof(ProbesController).Assembly);
            services.AddValidatorsFromAssemblyContaining<ProbesController>(ServiceLifetime.Singleton);
        });
    }

    /// <summary>A client carrying a mock-Entra token with <paramref name="roles"/>, or anonymous when none are given.</summary>
    public HttpClient CreateClientWithRoles(params string[] roles) => CreateClientAs("Integration Test", roles);

    /// <summary>A client for a specific user: different names get different <c>oid</c> claims.</summary>
    public HttpClient CreateClientAs(string userName, params string[] roles)
    {
        var client = CreateClient();
        if (roles.Length > 0)
        {
            var token = Services.GetRequiredService<MockTokenIssuer>().Issue(userName, roles);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return client;
    }
}
