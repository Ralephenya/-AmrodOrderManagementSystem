using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace OrderManagement.Infrastructure.Persistence;

/// <summary>
/// Used only by <c>dotnet ef</c>, so migrations can be added and scripted without booting the API.
/// The connection string is only needed for commands that talk to a database (<c>database update</c>);
/// override it with the <c>ConnectionStrings__OrdersDb</c> environment variable.
/// </summary>
internal sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    private const string LocalDb =
        "Server=(localdb)\\MSSQLLocalDB;Database=OrderManagement;Trusted_Connection=True;TrustServerCertificate=True";

    public AppDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__OrdersDb") ?? LocalDb;

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(connectionString, sql => sql.MigrationsHistoryTable("__EFMigrationsHistory"))
            .Options;

        return new AppDbContext(options);
    }
}
