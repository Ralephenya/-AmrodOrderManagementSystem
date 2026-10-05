using ErrorOr;
using OrderManagement.Domain.Common;
using OrderManagement.Domain.Orders;
using static OrderManagement.UnitTests.TestData;

namespace OrderManagement.UnitTests.Orders;

public class OrderCreateTests
{
    [Fact]
    public void Create_ComputesTotalServerSide_AsSumOfQuantityTimesUnitPrice()
    {
        var result = Order.Create(
            Customer(),
            "ZAR",
            [Line("PEN-001", 3, 19.99m), Line("MUG-002", 2, 85.50m), Line("CAP-003", 1, 0m)],
            Clock());

        result.IsError.ShouldBeFalse();
        result.Value.TotalAmount.ShouldBe(230.97m); // 59.97 + 171.00 + 0.00
        result.Value.LineItems.Select(li => li.LineTotal).ShouldBe([59.97m, 171.00m, 0m]);
    }

    [Fact]
    public void Create_TotalIsExact_NoFloatingPointDrift()
    {
        // 0.1 + 0.2 is the classic double failure; decimal must be exact.
        var result = Order.Create(Customer(), "ZAR", [Line("A", 1, 0.10m), Line("B", 1, 0.20m)], Clock());

        result.Value.TotalAmount.ShouldBe(0.30m);
    }

    [Fact]
    public void Create_StartsPending_WithUtcCreatedAt_AndCanonicalCodes()
    {
        var customer = Customer("NA");

        var order = Order.Create(customer, " zar ", [Line(" pen-001 ")], Clock()).Value;

        order.Status.ShouldBe(OrderStatus.Pending);
        order.CustomerId.ShouldBe(customer.Id);
        order.CurrencyCode.ShouldBe("ZAR"); // CMA: a Namibian customer may order in rand
        order.CreatedAt.ShouldBe(Now.UtcDateTime);
        order.CreatedAt.Kind.ShouldBe(DateTimeKind.Utc);
        order.AllocatedAt.ShouldBeNull();
        order.LineItems.Single().ProductSku.ShouldBe("PEN-001");
    }

    [Fact]
    public void Create_CurrencyNotAllowedForCustomersCountry_IsRejected()
    {
        var result = Order.Create(Customer("ZA"), "BWP", [Line()], Clock());

        result.FirstError.Code.ShouldBe("Sadc.CurrencyNotAllowedForCountry");
        result.FirstError.Description.ShouldBe("Customers in South Africa can only order in ZAR, not BWP.");
    }

    [Fact]
    public void Create_ZimbabweanCustomer_MayOrderInUsd()
    {
        Order.Create(Customer("ZW"), "USD", [Line()], Clock()).IsError.ShouldBeFalse();
    }

    [Fact]
    public void Create_NoLines_IsRejected()
    {
        Order.Create(Customer(), "ZAR", [], Clock()).FirstError.Code.ShouldBe("Order.LineItemsRequired");
        Order.Create(Customer(), "ZAR", null, Clock()).FirstError.Code.ShouldBe("Order.LineItemsRequired");
    }

    [Fact]
    public void Create_TooManyLines_IsRejected()
    {
        var lines = Enumerable.Range(0, Order.MaxLineItems + 1).Select(i => Line($"SKU-{i}")).ToList();

        Order.Create(Customer(), "ZAR", lines, Clock()).FirstError.Code.ShouldBe("Order.TooManyLineItems");
    }

    [Fact]
    public void Create_InvalidLines_ReportsEveryProblemWithItsFieldPath()
    {
        var result = Order.Create(
            Customer(),
            "ZAR",
            [Line("", 1, 10m), Line("MUG", 0, 10m), Line("CAP", 1, -1m), Line("BAG", 1, 10.005m)],
            Clock());

        result.IsError.ShouldBeTrue();
        result.Errors.ShouldAllBe(e => e.Type == ErrorType.Validation);
        result.Errors.Select(e => (e.Code, e.Metadata![ErrorMetadata.FieldKey])).ShouldBe(
        [
            ("Order.SkuRequired", "lineItems[0].productSku"),
            ("Order.QuantityMustBePositive", "lineItems[1].quantity"),
            ("Order.UnitPriceNegative", "lineItems[2].unitPrice"),
            ("Order.UnitPriceTooPrecise", "lineItems[3].unitPrice"),
        ]);
        result.Errors[3].Description.ShouldBe("Line 4: ZAR prices can have at most 2 decimal places.");
    }

    [Fact]
    public void Create_KmfPriceWithDecimals_IsRejectedWithCurrencySpecificMessage()
    {
        var result = Order.Create(Customer("KM"), "KMF", [Line("PEN", 1, 500.5m)], Clock());

        result.FirstError.Description.ShouldBe("Line 1: KMF prices can't have decimals.");
    }

    [Fact]
    public void Create_SkuTooLong_IsRejected()
    {
        var sku = new string('X', OrderLineItem.ProductSkuMaxLength + 1);

        Order.Create(Customer(), "ZAR", [Line(sku)], Clock()).FirstError.Code.ShouldBe("Order.SkuTooLong");
    }

    [Fact]
    public void Create_SameSkuOnTwoLines_IgnoringCaseAndSpaces_IsRejected()
    {
        var result = Order.Create(Customer(), "ZAR", [Line("pen-001"), Line(" PEN-001 ")], Clock());

        result.FirstError.Code.ShouldBe("Order.DuplicateSku");
        result.FirstError.Description.ShouldStartWith("SKU PEN-001 appears on more than one line.");
    }

    [Fact]
    public void Create_TotalBeyondDatabasePrecision_IsRejected()
    {
        var result = Order.Create(Customer(), "ZAR", [Line("A", 2, Money.MaxAmount)], Clock());

        result.FirstError.Code.ShouldBe("Order.TotalTooLarge");
    }

    [Fact]
    public void Create_BadCurrencyAndBadLines_ReportsBothTogether()
    {
        var result = Order.Create(Customer(), "XYZ", [Line("PEN", 0)], Clock());

        result.Errors.Select(e => e.Code).ShouldBe(["Sadc.CurrencyNotSupported", "Order.QuantityMustBePositive"]);
    }
}
