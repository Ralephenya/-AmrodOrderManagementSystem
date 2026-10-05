using System.Collections.Frozen;
using System.Diagnostics.CodeAnalysis;
using ErrorOr;

namespace OrderManagement.Domain.Sadc;

/// <summary>
/// The SADC member states (ISO 3166-1 alpha-2) and the ISO 4217 currencies each may order in.
/// Lookups are case-insensitive and ignore surrounding whitespace. Returned codes are always upper case.
/// </summary>
/// <remarks>
/// <para>
/// <b>Common Monetary Area (CMA).</b> ZAR, NAD, LSL and SZL are pegged 1:1, and the rand is legal tender in
/// Namibia, Lesotho and Eswatini. Customers in those three countries may therefore order in their local
/// currency <i>or</i> ZAR. South African customers may only use ZAR, because NAD, LSL and SZL are not legal
/// tender in South Africa. Amounts are never converted: an order's total stays in the order's currency, and
/// "at par" does not make the codes interchangeable in storage or reporting.
/// </para>
/// <para>
/// <b>Zimbabwe.</b> The brief specifies ZWL and USD. In April 2024 the Reserve Bank of Zimbabwe replaced ZWL
/// with ZWG (Zimbabwe Gold). ZWL is kept to match the brief; adding ZWG is a one-line change here.
/// </para>
/// </remarks>
public static class SadcCatalogue
{
    private static readonly FrozenDictionary<string, Currency> CurrencyByCode = new Currency[]
    {
        new("AOA", "Angolan kwanza", 2),
        new("BWP", "Botswana pula", 2),
        new("CDF", "Congolese franc", 2),
        new("KMF", "Comorian franc", 0),
        new("LSL", "Lesotho loti", 2),
        new("MGA", "Malagasy ariary", 2),
        new("MUR", "Mauritian rupee", 2),
        new("MWK", "Malawian kwacha", 2),
        new("MZN", "Mozambican metical", 2),
        new("NAD", "Namibian dollar", 2),
        new("SCR", "Seychellois rupee", 2),
        new("SZL", "Swazi lilangeni", 2),
        new("TZS", "Tanzanian shilling", 2),
        new("USD", "United States dollar", 2),
        new("ZAR", "South African rand", 2),
        new("ZMW", "Zambian kwacha", 2),
        new("ZWL", "Zimbabwean dollar", 2),
    }.ToFrozenDictionary(c => c.Code, StringComparer.OrdinalIgnoreCase);

    private static readonly FrozenDictionary<string, SadcCountry> CountryByCode = new[]
    {
        Country("AO", "Angola", "AOA"),
        Country("BW", "Botswana", "BWP"),
        Country("CD", "Democratic Republic of the Congo", "CDF"),
        Country("KM", "Comoros", "KMF"),
        CmaCountry("LS", "Lesotho", "LSL", "ZAR"),
        Country("MG", "Madagascar", "MGA"),
        Country("MU", "Mauritius", "MUR"),
        Country("MW", "Malawi", "MWK"),
        Country("MZ", "Mozambique", "MZN"),
        CmaCountry("NA", "Namibia", "NAD", "ZAR"),
        Country("SC", "Seychelles", "SCR"),
        CmaCountry("SZ", "Eswatini", "SZL", "ZAR"),
        Country("TZ", "Tanzania", "TZS"),
        CmaCountry("ZA", "South Africa", "ZAR"),
        Country("ZM", "Zambia", "ZMW"),
        Country("ZW", "Zimbabwe", "ZWL", "USD"),
    }.ToFrozenDictionary(c => c.Code, StringComparer.OrdinalIgnoreCase);

    /// <summary>The Common Monetary Area currencies, pegged at par to the rand.</summary>
    public static FrozenSet<string> CommonMonetaryAreaCurrencies { get; } =
        new[] { "ZAR", "NAD", "LSL", "SZL" }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>All 16 SADC member states, ordered by code.</summary>
    public static IReadOnlyList<SadcCountry> Countries { get; } =
        [.. CountryByCode.Values.OrderBy(c => c.Code, StringComparer.Ordinal)];

    /// <summary>Every currency accepted somewhere in the catalogue, ordered by code.</summary>
    public static IReadOnlyList<Currency> Currencies { get; } =
        [.. CurrencyByCode.Values.OrderBy(c => c.Code, StringComparer.Ordinal)];

    public static bool TryGetCountry(string? countryCode, [NotNullWhen(true)] out SadcCountry? country)
    {
        country = null;
        return countryCode is not null && CountryByCode.TryGetValue(countryCode.Trim(), out country);
    }

    public static bool TryGetCurrency(string? currencyCode, [NotNullWhen(true)] out Currency? currency)
    {
        currency = null;
        return currencyCode is not null && CurrencyByCode.TryGetValue(currencyCode.Trim(), out currency);
    }

    /// <summary>Resolves a SADC country by its ISO 3166-1 alpha-2 code.</summary>
    public static ErrorOr<SadcCountry> FindCountry(string? countryCode) =>
        TryGetCountry(countryCode, out var country)
            ? country
            : SadcErrors.CountryNotSupported(countryCode);

    /// <summary>
    /// Checks that <paramref name="currencyCode"/> is permitted for customers in <paramref name="countryCode"/>
    /// and returns the canonical currency when it is.
    /// </summary>
    public static ErrorOr<Currency> ValidateCurrencyForCountry(string? countryCode, string? currencyCode)
    {
        if (!TryGetCountry(countryCode, out var country))
        {
            return SadcErrors.CountryNotSupported(countryCode);
        }

        if (!TryGetCurrency(currencyCode, out var currency))
        {
            return SadcErrors.CurrencyNotSupported(currencyCode);
        }

        return country.Accepts(currency.Code)
            ? currency
            : SadcErrors.CurrencyNotAllowedForCountry(country, currency);
    }

    private static SadcCountry Country(string code, string name, params string[] currencyCodes) =>
        new(code, name, [.. currencyCodes.Select(c => CurrencyByCode[c])], IsCommonMonetaryArea: false);

    private static SadcCountry CmaCountry(string code, string name, params string[] currencyCodes) =>
        new(code, name, [.. currencyCodes.Select(c => CurrencyByCode[c])], IsCommonMonetaryArea: true);
}
