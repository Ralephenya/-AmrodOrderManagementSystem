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

        // The connection string is resolved when the context is first created, not at registration time,
        // so hosts and test factories can supply it through any configuration source.
        services.AddDbContext<AppDbContext>((sp, options) =>
        {
            var connectionString = sp.GetRequiredService<IConfiguration>().GetConnectionString(ConnectionStringName)
                ?? throw new InvalidOperationException(
                    $"Connection string '{ConnectionStringName}' is missing. See docs/ONBOARDING.md.");

            options.UseSqlServer(connectionString, sql => sql.EnableRetryOnFailure(maxRetryCount: 5));
        });

        services.AddScoped<IOrderReportQueries, OrderReportQueries>();

        services.AddSingleton<OrderMetrics>(); // IMeterFactory is registered by the .NET 8 host builders

        // Readiness only: a liveness probe must not depend on the database, or a DB outage would restart every pod.
        services.AddHealthChecks().AddCheck<SqlDatabaseHealthCheck>("sql", tags: [HealthTags.Ready], timeout: TimeSpan.FromSeconds(5));

        return services;
    }
}
