using ErrorOr;
using OrderManagement.Domain.Common;

namespace OrderManagement.Domain.Sadc;

/// <summary>Validation errors for country and currency rules, worded for end users.</summary>
public static class SadcErrors
{
    public static Error CountryNotSupported(string? countryCode) => Error.Validation(
        code: "Sadc.CountryNotSupported",
        description: string.IsNullOrWhiteSpace(countryCode)
            ? "Country is required. Use a two-letter SADC country code such as ZA, BW or NA."
            : $"'{countryCode.Trim()}' isn't a SADC country we serve. Use a two-letter code such as ZA, BW or NA.",
        metadata: ErrorMetadata.ForField("countryCode"));

    public static Error CurrencyNotSupported(string? currencyCode) => Error.Validation(
        code: "Sadc.CurrencyNotSupported",
        description: string.IsNullOrWhiteSpace(currencyCode)
            ? "Currency is required. Use a three-letter currency code such as ZAR."
            : $"'{currencyCode.Trim()}' isn't a currency we accept. Use a SADC currency code such as ZAR, BWP or NAD.",
        metadata: ErrorMetadata.ForField("currencyCode"));

    public static Error CurrencyNotAllowedForCountry(SadcCountry country, Currency currency) => Error.Validation(
        code: "Sadc.CurrencyNotAllowedForCountry",
        description: $"Customers in {country.Name} can only order in {JoinCodes(country.Currencies)}, not {currency.Code}.",
        metadata: ErrorMetadata.ForField("currencyCode"));

    private static string JoinCodes(IReadOnlyList<Currency> currencies) => currencies.Count == 1
        ? currencies[0].Code
        : string.Join(", ", currencies.Take(currencies.Count - 1).Select(c => c.Code)) + " or " + currencies[^1].Code;
}
