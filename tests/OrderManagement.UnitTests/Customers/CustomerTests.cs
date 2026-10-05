using ErrorOr;
using OrderManagement.Domain.Common;
using OrderManagement.Domain.Customers;

namespace OrderManagement.UnitTests.Customers;

public class CustomerTests
{
    [Fact]
    public void Create_ValidInput_NormalisesFieldsAndStampsUtcTime()
    {
        var result = Customer.Create("  Thandi Nkosi ", " Thandi@Example.CO.ZA ", "za", TestData.Clock());

        result.IsError.ShouldBeFalse();
        var customer = result.Value;
        customer.Name.ShouldBe("Thandi Nkosi");
        customer.Email.ShouldBe("thandi@example.co.za");
        customer.CountryCode.ShouldBe("ZA");
        customer.CreatedAt.ShouldBe(TestData.Now.UtcDateTime);
        customer.CreatedAt.Kind.ShouldBe(DateTimeKind.Utc);
    }

    [Fact]
    public void Create_EverythingMissing_ReportsEveryProblemWithItsField()
    {
        var result = Customer.Create(" ", null, "KE", TestData.Clock());

        result.IsError.ShouldBeTrue();
        result.Errors.Select(e => e.Code).ShouldBe(
            ["Customer.NameRequired", "Customer.EmailRequired", "Sadc.CountryNotSupported"]);
        result.Errors.Select(e => e.Metadata![ErrorMetadata.FieldKey]).ShouldBe(["name", "email", "countryCode"]);
        result.Errors.ShouldAllBe(e => e.Type == ErrorType.Validation);
    }
}
