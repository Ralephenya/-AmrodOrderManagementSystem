using Microsoft.Extensions.Time.Testing;
using OrderManagement.Domain.Customers;
using OrderManagement.Domain.Orders;

namespace OrderManagement.IntegrationTests;

internal static class TestData
{
    public static readonly DateTimeOffset Now = new(2026, 10, 5, 8, 30, 0, TimeSpan.Zero);

    public static FakeTimeProvider Clock() => new(Now);

    /// <summary>A valid customer with a unique email, so tests never collide in the shared database.</summary>
    public static Customer NewCustomer(string countryCode = "ZA") =>
        Customer.Create("Thandi Nkosi", $"thandi.{Guid.NewGuid():N}@example.co.za", countryCode, Clock()).Value;

    /// <summary>Requires <paramref name="customer"/> to have been added to a context (so its ID is generated).</summary>
    public static Order NewOrder(Customer customer, string currency = "ZAR", params NewOrderLine[] lines) =>
        Order.Create(customer, currency, lines.Length == 0 ? [new NewOrderLine("PEN-001", 1, 10m)] : lines, Clock()).Value;
}
