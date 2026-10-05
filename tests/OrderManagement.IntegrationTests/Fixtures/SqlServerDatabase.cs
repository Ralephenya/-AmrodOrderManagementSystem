using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using OrderManagement.Infrastructure.Persistence;
using Testcontainers.MsSql;

namespace OrderManagement.IntegrationTests.Fixtures;

/// <summary>
/// A real SQL Server database with every migration applied, created once per test run.
/// </summary>
/// <remarks>
/// Selected with the <c>ORDERS_TEST_SQL</c> environment variable:
/// <list type="bullet">
/// <item><c>container</c>: a throwaway SQL Server 2022 container (Testcontainers). Used in CI.</item>
/// <item><c>localdb</c>: a uniquely named LocalDB database, dropped afterwards. Windows without Docker.</item>
/// <item><c>auto</c> (default): container if Docker is reachable, otherwise LocalDB on Windows.</item>
/// </list>
/// </remarks>
public sealed class SqlServerDatabase : IAsyncDisposable
{
    private const string ContainerImage = "mcr.microsoft.com/mssql/server:2022-latest";

    private readonly MsSqlContainer? _container;

    private SqlServerDatabase(string connectionString, string provider, MsSqlContainer? container)
    {
        ConnectionString = connectionString;
        Provider = provider;
        _container = container;
    }

    public string ConnectionString { get; }

    /// <summary><c>container</c> or <c>localdb</c>, so test output shows what was used.</summary>
    public string Provider { get; }

    public static async Task<SqlServerDatabase> StartAsync()
    {
        var mode = (Environment.GetEnvironmentVariable("ORDERS_TEST_SQL") ?? "auto").ToLowerInvariant();

        var database = mode switch
        {
            "container" => await StartContainerAsync(),
            "localdb" => CreateLocalDb(),
            _ => await TryStartContainerAsync() ?? CreateLocalDb(),
        };

        await using var db = database.CreateDbContext();
        await db.Database.MigrateAsync();

        return database;
    }

    public AppDbContext CreateDbContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(ConnectionString).Options);

    public async ValueTask DisposeAsync()
    {
        if (_container is not null)
        {
            await _container.DisposeAsync();
            return;
        }

        await using var db = CreateDbContext();
        await db.Database.EnsureDeletedAsync();
    }

    private static async Task<SqlServerDatabase?> TryStartContainerAsync()
    {
        try
        {
            return await StartContainerAsync();
        }
        catch (Exception ex) when (OperatingSystem.IsWindows() && IsDockerUnavailable(ex))
        {
            return null;
        }
    }

    private static async Task<SqlServerDatabase> StartContainerAsync()
    {
        var container = new MsSqlBuilder(ContainerImage).Build();
        await container.StartAsync();

        var connectionString = new SqlConnectionStringBuilder(container.GetConnectionString())
        {
            InitialCatalog = "OrderManagementTests",
        }.ConnectionString;

        return new SqlServerDatabase(connectionString, "container", container);
    }

    private static SqlServerDatabase CreateLocalDb()
    {
        var connectionString = new SqlConnectionStringBuilder
        {
            DataSource = "(localdb)\\MSSQLLocalDB",
            InitialCatalog = $"OrderManagement_IT_{Guid.NewGuid():N}",
            IntegratedSecurity = true,
            TrustServerCertificate = true,
            ConnectTimeout = 60, // the LocalDB instance may need to start
        }.ConnectionString;

        return new SqlServerDatabase(connectionString, "localdb", container: null);
    }

    private static bool IsDockerUnavailable(Exception ex) =>
        ex.GetType().Name.Contains("Docker", StringComparison.Ordinal)
        || ex.Message.Contains("Docker", StringComparison.OrdinalIgnoreCase);
}
