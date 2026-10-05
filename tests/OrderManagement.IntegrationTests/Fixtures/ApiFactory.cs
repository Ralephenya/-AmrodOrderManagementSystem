using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using OrderManagement.Infrastructure;

namespace OrderManagement.IntegrationTests.Fixtures;

/// <summary>Hosts the real API in memory against the test database.</summary>
public sealed class ApiFactory(SqlServerDatabase database) : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting($"ConnectionStrings:{DependencyInjection.ConnectionStringName}", database.ConnectionString);

        // The fixture already applied migrations; the app must not try again.
        builder.UseSetting("Database:ApplyMigrationsOnStartup", "false");
    }
}
