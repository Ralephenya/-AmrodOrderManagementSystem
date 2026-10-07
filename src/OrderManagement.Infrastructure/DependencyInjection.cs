using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OrderManagement.Contracts;
using OrderManagement.Infrastructure.Observability;
using OrderManagement.Infrastructure.Persistence;
using OrderManagement.Infrastructure.Reports;

namespace OrderManagement.Infrastructure;

public static class DependencyInjection
{
    public const string ConnectionStringName = "OrdersDb";

    /// <summary>Registers the database and shared infrastructure used by both the API and the Worker.</summary>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services)
    {
        services.AddSingleton(TimeProvider.System);

        // One registration for both uses: a factory for code that needs a context of its own (GraphQL runs resolvers in
        // parallel, and a DbContext is not thread-safe), which also registers AppDbContext itself as a scoped service
        // for everything else (services, MassTransit's outbox, consumers). Scoped, so a context is per request as before.
        services.AddDbContextFactory<AppDbContext>(ConfigureDbContext, ServiceLifetime.Scoped);

        services.AddScoped<IOrderReportQueries, OrderReportQueries>();

        services.AddSingleton<OrderMetrics>(); // IMeterFactory is registered by the .NET 8 host builders

        // Readiness only: a liveness probe must not depend on the database, or a DB outage would restart every pod.
        services.AddHealthChecks().AddCheck<SqlDatabaseHealthCheck>("sql", tags: [HealthTags.Ready], timeout: TimeSpan.FromSeconds(5));

        return services;
    }

    /// <summary>
    /// The connection string is resolved when a context is first created, not at registration time, so hosts and test
    /// factories can supply it through any configuration source.
    /// </summary>
    private static void ConfigureDbContext(IServiceProvider sp, DbContextOptionsBuilder options)
    {
        var connectionString = sp.GetRequiredService<IConfiguration>().GetConnectionString(ConnectionStringName)
            ?? throw new InvalidOperationException(
                $"Connection string '{ConnectionStringName}' is missing. See docs/ONBOARDING.md.");

        options.UseSqlServer(connectionString, sql => sql.EnableRetryOnFailure(maxRetryCount: 5));
    }
}
