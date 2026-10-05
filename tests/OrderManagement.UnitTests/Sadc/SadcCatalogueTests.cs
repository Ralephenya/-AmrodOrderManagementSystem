using ErrorOr;
using OrderManagement.Domain.Sadc;

namespace OrderManagement.UnitTests.Sadc;

public class SadcCatalogueTests
{
    // The country -> currency table from the brief (ZW allows both ZWL and USD).
    public static TheoryData<string, string> BriefPairs => new()
    {
        { "AO", "AOA" }, { "BW", "BWP" }, { "KM", "KMF" }, { "CD", "CDF" },
        { "SZ", "SZL" }, { "LS", "LSL" }, { "MG", "MGA" }, { "MW", "MWK" },
        { "MU", "MUR" }, { "MZ", "MZN" }, { "NA", "NAD" }, { "SC", "SCR" },
        { "ZA", "ZAR" }, { "TZ", "TZS" }, { "ZM", "ZMW" }, { "ZW", "ZWL" },
        { "ZW", "USD" },
    };

    [Fact]
    public void Countries_ContainsAllSixteenSadcMemberStates()
    {
        SadcCatalogue.Countries.Select(c => c.Code).ShouldBe(
            ["AO", "BW", "CD", "KM", "LS", "MG", "MU", "MW", "MZ", "NA", "SC", "SZ", "TZ", "ZA", "ZM", "ZW"]);
    }

    [Theory]
    [MemberData(nameof(BriefPairs))]
    public void ValidateCurrencyForCountry_BriefPair_ReturnsCurrency(string country, string currency)
    {
        var result = SadcCatalogue.ValidateCurrencyForCountry(country, currency);

        result.IsError.ShouldBeFalse();
        result.Value.Code.ShouldBe(currency);
    }

    [Theory]
    [InlineData("za", "zar")]
    [InlineData(" ZA ", " ZAR ")]
    [InlineData("Zw", "usd")]
    public void ValidateCurrencyForCountry_IsCaseAndWhitespaceInsensitive_AndReturnsCanonicalCode(string country, string currency)
    {
        var result = SadcCatalogue.ValidateCurrencyForCountry(country, currency);

        result.IsError.ShouldBeFalse();
        result.Value.Code.ShouldBe(currency.Trim().ToUpperInvariant());
    }

    [Theory]
    [InlineData("ZA", "BWP")]
    [InlineData("BW", "ZAR")]
    [InlineData("ZW", "ZAR")]
    [InlineData("MZ", "USD")]
    public void ValidateCurrencyForCountry_SadcCurrencyFromAnotherCountry_IsRejected(string country, string currency)
    {
        var result = SadcCatalogue.ValidateCurrencyForCountry(country, currency);

        result.IsError.ShouldBeTrue();
        result.FirstError.Type.ShouldBe(ErrorType.Validation);
        result.FirstError.Code.ShouldBe("Sadc.CurrencyNotAllowedForCountry");
    }

    [Theory]
    [InlineData("KE")] // Kenya: not SADC
    [InlineData("US")]
    [InlineData("ZAF")] // alpha-3, not alpha-2
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void ValidateCurrencyForCountry_UnknownCountry_IsRejected(string? country)
    {
        var result = SadcCatalogue.ValidateCurrencyForCountry(country, "ZAR");

        result.IsError.ShouldBeTrue();
        result.FirstError.Code.ShouldBe("Sadc.CountryNotSupported");
    }

    [Theory]
    [InlineData("EUR")]
    [InlineData("ZWG")] // not accepted until added deliberately; see SadcCatalogue remarks
    [InlineData("RAND")]
    [InlineData("")]
    [InlineData(null)]
    public void ValidateCurrencyForCountry_UnknownCurrency_IsRejected(string? currency)
    {
        var result = SadcCatalogue.ValidateCurrencyForCountry("ZA", currency);

        result.IsError.ShouldBeTrue();
        result.FirstError.Code.ShouldBe("Sadc.CurrencyNotSupported");
    }

    [Fact]
    public void ValidateCurrencyForCountry_WrongCurrency_HasAFriendlyMessage()
    {
        var result = SadcCatalogue.ValidateCurrencyForCountry("NA", "BWP");

        result.FirstError.Description.ShouldBe("Customers in Namibia can only order in NAD or ZAR, not BWP.");
    }

    [Fact]
    public void ValidateCurrencyForCountry_ZimbabweWrongCurrency_ListsBothAllowedCurrencies()
    {
        var result = SadcCatalogue.ValidateCurrencyForCountry("ZW", "ZAR");

        result.FirstError.Description.ShouldBe("Customers in Zimbabwe can only order in ZWL or USD, not ZAR.");
    }

    [Theory]
    [InlineData("NA")]
    [InlineData("LS")]
    [InlineData("SZ")]
    public void CmaMembersOutsideSouthAfrica_MayAlsoOrderInRand(string country)
    {
        SadcCatalogue.ValidateCurrencyForCountry(country, "ZAR").IsError.ShouldBeFalse();
    }

    [Theory]
    [InlineData("NAD")]
    [InlineData("LSL")]
    [InlineData("SZL")]
    public void SouthAfrica_DoesNotAcceptOtherCmaCurrencies(string currency)
    {
        SadcCatalogue.ValidateCurrencyForCountry("ZA", currency).IsError.ShouldBeTrue();
    }

    [Fact]
    public void CommonMonetaryArea_FlagsExactlyTheFourMemberStates()
    {
        SadcCatalogue.Countries.Where(c => c.IsCommonMonetaryArea).Select(c => c.Code)
            .ShouldBe(["LS", "NA", "SZ", "ZA"]);
        SadcCatalogue.CommonMonetaryAreaCurrencies.Order().ShouldBe(["LSL", "NAD", "SZL", "ZAR"]);
    }

    [Fact]
    public void ComorianFranc_HasNoMinorUnits()
    {
        SadcCatalogue.TryGetCurrency("KMF", out var kmf).ShouldBeTrue();
        kmf!.MinorUnits.ShouldBe(0);
    }

    [Fact]
    public void EveryCurrencyExceptKmf_HasTwoMinorUnits()
    {
        SadcCatalogue.Currencies.Where(c => c.Code != "KMF").ShouldAllBe(c => c.MinorUnits == 2);
    }

    [Fact]
    public void FindCountry_KnownCode_ReturnsCountryWithLocalCurrencyFirst()
    {
        var result = SadcCatalogue.FindCountry("na");

        result.IsError.ShouldBeFalse();
        result.Value.Name.ShouldBe("Namibia");
        result.Value.Currencies.Select(c => c.Code).ShouldBe(["NAD", "ZAR"]);
    }

    [Fact]
    public void FindCountry_UnknownCode_ReturnsFriendlyValidationError()
    {
        var result = SadcCatalogue.FindCountry("KE");

        result.FirstError.Type.ShouldBe(ErrorType.Validation);
        result.FirstError.Description.ShouldBe("'KE' isn't a SADC country we serve. Use a two-letter code such as ZA, BW or NA.");
    }
}
