using Microsoft.Extensions.Time.Testing;
using OrderManagement.Domain.Customers;
using OrderManagement.Domain.Orders;

namespace OrderManagement.UnitTests;

internal static class TestData
{
    public static readonly DateTimeOffset Now = new(2026, 10, 5, 8, 30, 0, TimeSpan.Zero);

    public static FakeTimeProvider Clock() => new(Now);

    public static Customer Customer(string countryCode = "ZA") =>
        Domain.Customers.Customer.Create("Thandi Nkosi", "thandi@example.co.za", countryCode, Clock()).Value;

    public static NewOrderLine Line(string sku = "PEN-001", int quantity = 1, decimal unitPrice = 10m) =>
        new(sku, quantity, unitPrice);

    public static Order PendingOrder(params NewOrderLine[] lines) =>
        Order.Create(Customer(), "ZAR", lines.Length == 0 ? [Line()] : lines, Clock()).Value;
}
