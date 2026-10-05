using OrderManagement.Infrastructure.Persistence;

namespace OrderManagement.IntegrationTests.Fixtures;

/// <summary>
/// Shared by every integration test class: one database and one in-memory API per run.
/// Tests isolate themselves with unique data (fresh IDs and emails) instead of resetting the database.
/// </summary>
public sealed class IntegrationTestFixture : IAsyncLifetime, IAsyncDisposable
{
    private SqlServerDatabase? _database;
    private ApiFactory? _factory;

    public SqlServerDatabase Database => _database ?? throw new InvalidOperationException("Fixture not initialised.");

    public ApiFactory Factory => _factory ?? throw new InvalidOperationException("Fixture not initialised.");

    public AppDbContext CreateDbContext() => Database.CreateDbContext();

    public async Task InitializeAsync()
    {
        _database = await SqlServerDatabase.StartAsync();
        _factory = new ApiFactory(_database);
    }

    async ValueTask IAsyncDisposable.DisposeAsync() => await DisposeAsync();

    public async Task DisposeAsync()
    {
        if (_factory is not null)
        {
            await _factory.DisposeAsync();
        }

        if (_database is not null)
        {
            await _database.DisposeAsync();
        }
    }
}

[CollectionDefinition(Name)]
#pragma warning disable CA1711 // xUnit's convention is to name collection definitions "...Collection".
public sealed class IntegrationTestCollection : ICollectionFixture<IntegrationTestFixture>
#pragma warning restore CA1711
{
    public const string Name = "Integration";
}
