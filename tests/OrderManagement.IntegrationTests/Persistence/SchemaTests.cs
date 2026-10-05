using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using OrderManagement.Infrastructure.Persistence;
using OrderManagement.IntegrationTests.Fixtures;

namespace OrderManagement.IntegrationTests.Persistence;

[Collection(IntegrationTestCollection.Name)]
public class SchemaTests(IntegrationTestFixture fixture)
{
    [Fact]
    public async Task AllMigrations_AreApplied()
    {
        await using var db = fixture.CreateDbContext();

        (await db.Database.GetPendingMigrationsAsync()).ShouldBeEmpty();
        (await db.Database.GetAppliedMigrationsAsync()).Select(m => m[15..]).ShouldBe(
            ["InitialCreate", "AddOrderRowVersion", "AddOrderAllocatedAt", "AddIdempotencyKeys"]);
    }

    [Fact]
    public async Task Model_HasNoChangesMissingAMigration()
    {
        // Fails when someone changes an entity or configuration but forgets `dotnet ef migrations add`.
        await using var db = fixture.CreateDbContext();

        var snapshot = db.GetService<IMigrationsAssembly>().ModelSnapshot?.Model;
        snapshot.ShouldNotBeNull();
        if (snapshot is IMutableModel mutable)
        {
            snapshot = mutable.FinalizeModel();
        }

        snapshot = db.GetService<IModelRuntimeInitializer>().Initialize(snapshot);
        var current = db.GetService<IDesignTimeModel>().Model;

        db.GetService<IMigrationsModelDiffer>()
            .HasDifferences(snapshot.GetRelationalModel(), current.GetRelationalModel())
            .ShouldBeFalse();
    }

    [Fact]
    public async Task ListingIndex_CoversTheOrderListQuery()
    {
        await using var db = fixture.CreateDbContext();

        var columns = await db.Database.SqlQueryRaw<IndexColumn>(
            """
            SELECT COL_NAME(ic.object_id, ic.column_id) AS [Column], ic.is_included_column AS IsIncluded
            FROM sys.indexes i
            JOIN sys.index_columns ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id
            WHERE i.name = 'IX_Orders_CustomerId_Status_CreatedAt'
            ORDER BY ic.is_included_column, ic.key_ordinal
            """).ToListAsync();

        columns.ShouldBe(
        [
            new IndexColumn("CustomerId", false),
            new IndexColumn("Status", false),
            new IndexColumn("CreatedAt", false),
            new IndexColumn("TotalAmount", true),
            new IndexColumn("CurrencyCode", true),
        ]);
    }

    [Fact]
    public async Task Api_IsWiredToTheTestDatabase()
    {
        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        (await db.Database.CanConnectAsync()).ShouldBeTrue();
        db.Database.GetConnectionString().ShouldBe(fixture.Database.ConnectionString);
    }

    public sealed record IndexColumn(string Column, bool IsIncluded);
}
