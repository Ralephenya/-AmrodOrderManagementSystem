using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using OrderManagement.Domain.Orders;
using OrderManagement.IntegrationTests.Fixtures;
using static OrderManagement.IntegrationTests.TestData;

namespace OrderManagement.IntegrationTests.Persistence;

[Collection(IntegrationTestCollection.Name)]
public class OrderPersistenceTests(IntegrationTestFixture fixture)
{
    [Fact]
    public async Task Order_RoundTrips_WithLineItems_ExactMoney_AndUtcDates()
    {
        Guid orderId;
        await using (var db = fixture.CreateDbContext())
        {
            var customer = NewCustomer();
            db.Customers.Add(customer); // generates the customer's sequential GUID
            var order = NewOrder(
                customer,
                "ZAR",
                new NewOrderLine("PEN-001", 3, 19.99m),
                new NewOrderLine("MUG-002", 2, 85.50m),
                new NewOrderLine("CAP-003", 1, 0.01m));
            db.Orders.Add(order);
            await db.SaveChangesAsync();
            orderId = order.Id;
        }

        await using (var db = fixture.CreateDbContext())
        {
            var loaded = await db.Orders.AsNoTracking().Include(o => o.LineItems).SingleAsync(o => o.Id == orderId);

            loaded.TotalAmount.ShouldBe(230.98m);
            loaded.LineItems.Count.ShouldBe(3);
            loaded.LineItems.Sum(li => li.LineTotal).ShouldBe(loaded.TotalAmount);
            loaded.LineItems.ShouldAllBe(li => li.Id != Guid.Empty && li.OrderId == orderId);
            loaded.Status.ShouldBe(OrderStatus.Pending);
            loaded.CurrencyCode.ShouldBe("ZAR");
            loaded.CreatedAt.ShouldBe(Now.UtcDateTime);
            loaded.CreatedAt.Kind.ShouldBe(DateTimeKind.Utc);
            loaded.AllocatedAt.ShouldBeNull();
            loaded.RowVersion.Length.ShouldBe(8);
        }
    }

    [Fact]
    public async Task Status_IsStoredAsReadableText()
    {
        await using var db = fixture.CreateDbContext();
        var customer = NewCustomer();
        db.Customers.Add(customer);
        var order = NewOrder(customer);
        order.TransitionTo(OrderStatus.Paid);
        db.Orders.Add(order);
        await db.SaveChangesAsync();

        var stored = await db.Database
            .SqlQuery<string>($"SELECT [Status] AS [Value] FROM [Orders] WHERE [Id] = {order.Id}")
            .SingleAsync();

        stored.ShouldBe("Paid");
    }

    [Fact]
    public async Task NullableUtcDate_RoundTripsAsUtc()
    {
        Guid orderId;
        await using (var db = fixture.CreateDbContext())
        {
            var customer = NewCustomer();
            db.Customers.Add(customer);
            var order = NewOrder(customer);
            order.Allocate(Clock());
            db.Orders.Add(order);
            await db.SaveChangesAsync();
            orderId = order.Id;
        }

        await using (var db = fixture.CreateDbContext())
        {
            var allocatedAt = await db.Orders.Where(o => o.Id == orderId).Select(o => o.AllocatedAt).SingleAsync();

            allocatedAt.ShouldBe(Now.UtcDateTime);
            allocatedAt!.Value.Kind.ShouldBe(DateTimeKind.Utc);
        }
    }

    [Fact]
    public async Task ConcurrentUpdates_SecondWriterGetsAConcurrencyException()
    {
        Guid orderId;
        await using (var setup = fixture.CreateDbContext())
        {
            var customer = NewCustomer();
            setup.Customers.Add(customer);
            var order = NewOrder(customer);
            setup.Orders.Add(order);
            await setup.SaveChangesAsync();
            orderId = order.Id;
        }

        await using var first = fixture.CreateDbContext();
        await using var second = fixture.CreateDbContext();
        var firstCopy = await first.Orders.SingleAsync(o => o.Id == orderId);
        var secondCopy = await second.Orders.SingleAsync(o => o.Id == orderId);

        firstCopy.TransitionTo(OrderStatus.Paid);
        await first.SaveChangesAsync();

        secondCopy.TransitionTo(OrderStatus.Cancelled);
        await Should.ThrowAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync());
    }

    [Fact]
    public async Task RowVersion_ChangesOnEveryUpdate()
    {
        await using var db = fixture.CreateDbContext();
        var customer = NewCustomer();
        db.Customers.Add(customer);
        var order = NewOrder(customer);
        db.Orders.Add(order);
        await db.SaveChangesAsync();
        var before = order.RowVersion.ToArray();

        order.TransitionTo(OrderStatus.Paid);
        await db.SaveChangesAsync();

        order.RowVersion.ShouldNotBe(before);
    }

    [Fact]
    public async Task DeletingACustomerWithOrders_IsRestricted()
    {
        await using var db = fixture.CreateDbContext();
        var customer = NewCustomer();
        db.Customers.Add(customer);
        db.Orders.Add(NewOrder(customer));
        await db.SaveChangesAsync();

        // Bypass EF's change tracker so the database itself enforces the rule.
        var ex = await Should.ThrowAsync<SqlException>(() =>
            db.Database.ExecuteSqlAsync($"DELETE FROM [Customers] WHERE [Id] = {customer.Id}"));

        ex.Message.ShouldContain("FK_Orders_Customers_CustomerId");
    }

    [Fact]
    public async Task DeletingAnOrder_CascadesToItsLineItems()
    {
        await using var db = fixture.CreateDbContext();
        var customer = NewCustomer();
        db.Customers.Add(customer);
        var order = NewOrder(customer, "ZAR", new NewOrderLine("A", 1, 1m), new NewOrderLine("B", 1, 1m));
        db.Orders.Add(order);
        await db.SaveChangesAsync();

        await db.Database.ExecuteSqlAsync($"DELETE FROM [Orders] WHERE [Id] = {order.Id}");

        var remaining = await db.Database
            .SqlQuery<int>($"SELECT COUNT(*) AS [Value] FROM [OrderLineItems] WHERE [OrderId] = {order.Id}")
            .SingleAsync();
        remaining.ShouldBe(0);
    }

    [Theory]
    [InlineData("UPDATE [OrderLineItems] SET [Quantity] = 0 WHERE [OrderId] = @p0", "CK_OrderLineItems_Quantity")]
    [InlineData("UPDATE [OrderLineItems] SET [UnitPrice] = -1 WHERE [OrderId] = @p0", "CK_OrderLineItems_UnitPrice")]
    [InlineData("UPDATE [Orders] SET [Status] = 'Shipped' WHERE [Id] = @p0", "CK_Orders_Status")]
    [InlineData("UPDATE [Orders] SET [TotalAmount] = -0.01 WHERE [Id] = @p0", "CK_Orders_TotalAmount")]
    public async Task CheckConstraints_RejectInvalidData_EvenWhenTheDomainIsBypassed(string sql, string constraint)
    {
        await using var db = fixture.CreateDbContext();
        var customer = NewCustomer();
        db.Customers.Add(customer);
        var order = NewOrder(customer);
        db.Orders.Add(order);
        await db.SaveChangesAsync();

        var ex = await Should.ThrowAsync<SqlException>(() => db.Database.ExecuteSqlRawAsync(sql, order.Id));

        ex.Message.ShouldContain(constraint);
    }

    [Fact]
    public async Task DuplicateSkuOnTheSameOrder_IsRejectedByTheDatabase()
    {
        await using var db = fixture.CreateDbContext();
        var customer = NewCustomer();
        db.Customers.Add(customer);
        var order = NewOrder(customer, "ZAR", new NewOrderLine("A", 1, 1m), new NewOrderLine("B", 1, 1m));
        db.Orders.Add(order);
        await db.SaveChangesAsync();

        var ex = await Should.ThrowAsync<SqlException>(() => db.Database.ExecuteSqlAsync(
            $"UPDATE [OrderLineItems] SET [ProductSku] = 'A' WHERE [OrderId] = {order.Id} AND [ProductSku] = 'B'"));

        ex.Message.ShouldContain("IX_OrderLineItems_OrderId_ProductSku");
    }

    [Fact]
    public async Task DuplicateCustomerEmail_IsRejectedByTheDatabase()
    {
        await using var db = fixture.CreateDbContext();
        var first = NewCustomer();
        db.Customers.Add(first);
        await db.SaveChangesAsync();

        var duplicate = Domain.Customers.Customer.Create("Someone Else", first.Email, "BW", Clock()).Value;
        db.Customers.Add(duplicate);

        var ex = await Should.ThrowAsync<DbUpdateException>(() => db.SaveChangesAsync());
        ex.InnerException!.Message.ShouldContain("IX_Customers_Email");
    }
}
