using OrderManagement.Domain.Common;
using OrderManagement.Domain.Sadc;

namespace OrderManagement.UnitTests.Common;

public class MoneyTests
{
    [Theory]
    [InlineData("10", 0)]
    [InlineData("10.5", 1)]
    [InlineData("10.50", 1)]
    [InlineData("10.500", 1)]
    [InlineData("10.05", 2)]
    [InlineData("10.005", 3)]
    [InlineData("0", 0)]
    [InlineData("0.00", 0)]
    public void DecimalPlaces_IgnoresTrailingZeros(string amount, int expected)
    {
        Money.DecimalPlaces(decimal.Parse(amount, System.Globalization.CultureInfo.InvariantCulture)).ShouldBe(expected);
    }

    [Theory]
    [InlineData("ZAR", "19.99", true)]
    [InlineData("ZAR", "19.990", true)]
    [InlineData("ZAR", "19.995", false)]
    [InlineData("KMF", "500", true)]
    [InlineData("KMF", "500.00", true)]
    [InlineData("KMF", "500.5", false)]
    public void FitsCurrency_RespectsMinorUnits(string currencyCode, string amount, bool expected)
    {
        SadcCatalogue.TryGetCurrency(currencyCode, out var currency).ShouldBeTrue();

        Money.FitsCurrency(decimal.Parse(amount, System.Globalization.CultureInfo.InvariantCulture), currency!)
            .ShouldBe(expected);
    }

    [Fact]
    public void MaxAmount_IsTheLargestDecimal18_2()
    {
        Money.MaxAmount.ToString(System.Globalization.CultureInfo.InvariantCulture)
            .Replace(".", string.Empty, StringComparison.Ordinal)
            .Length.ShouldBe(Money.Precision);
        Money.DecimalPlaces(Money.MaxAmount).ShouldBe(Money.Scale);
    }
}
