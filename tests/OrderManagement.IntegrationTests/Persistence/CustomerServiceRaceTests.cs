using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using OrderManagement.Api.Contracts.Customers;
using OrderManagement.Api.Services;
using OrderManagement.Domain.Customers;
using OrderManagement.Infrastructure.Persistence;
using OrderManagement.IntegrationTests.Fixtures;

namespace OrderManagement.IntegrationTests.Persistence;

/// <summary>
/// The duplicate-email check runs before the insert, so two concurrent requests can both pass it. These tests force
/// that interleaving deterministically: another "request" inserts the same email between the check and the save.
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public class CustomerServiceRaceTests(IntegrationTestFixture fixture)
{
    [Fact]
    public async Task LosingTheRace_OnTheUniqueIndex_IsStillAClean409()
    {
        var email = $"race.{Guid.NewGuid():N}@example.co.za";
        var competitor = new InsertCompetitorBeforeSave(fixture, email);
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(fixture.Database.ConnectionString)
            .AddInterceptors(competitor)
            .Options);
        var service = new CustomerService(db, TimeProvider.System);

        var result = await service.CreateAsync(new CreateCustomerRequest("Late Arrival", email, "ZA"), CancellationToken.None);

        competitor.Inserted.ShouldBeTrue();
        result.IsError.ShouldBeTrue();
        result.FirstError.Code.ShouldBe("Customer.EmailAlreadyExists");
    }

    /// <summary>Plays the request that wins: commits the same email just before our SaveChanges reaches the database.</summary>
    private sealed class InsertCompetitorBeforeSave(IntegrationTestFixture fixture, string email) : SaveChangesInterceptor
    {
        public bool Inserted { get; private set; }

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (!Inserted)
            {
                Inserted = true;
                await using var other = fixture.CreateDbContext();
                other.Customers.Add(Customer.Create("First Arrival", email, "BW", TimeProvider.System).Value);
                await other.SaveChangesAsync(cancellationToken);
            }

            return await base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }
}
