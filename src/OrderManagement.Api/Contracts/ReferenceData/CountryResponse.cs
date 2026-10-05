namespace OrderManagement.Api.Contracts.ReferenceData;

/// <param name="Code">ISO 3166-1 alpha-2, e.g. <c>ZA</c>.</param>
/// <param name="Name">Display name.</param>
/// <param name="IsCommonMonetaryArea">True for ZA, NA, LS and SZ.</param>
/// <param name="Currencies">Currencies customers in this country may order in, local currency first.</param>
public sealed record CountryResponse(
    string Code,
    string Name,
    bool IsCommonMonetaryArea,
    IReadOnlyList<CurrencyResponse> Currencies);

/// <param name="Code">ISO 4217, e.g. <c>ZAR</c>.</param>
/// <param name="Name">Display name.</param>
/// <param name="MinorUnits">Decimal places the currency uses (KMF uses 0).</param>
public sealed record CurrencyResponse(string Code, string Name, int MinorUnits);
