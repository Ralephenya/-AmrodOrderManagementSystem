using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OrderManagement.Infrastructure.Persistence;

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

        return services;
    }
}
