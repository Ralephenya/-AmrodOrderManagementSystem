namespace OrderManagement.Domain.Sadc;

/// <summary>A SADC member state and the currencies its customers may order in.</summary>
/// <param name="Code">Two-letter ISO 3166-1 alpha-2 code, upper case (e.g. <c>ZA</c>).</param>
/// <param name="Name">Display name.</param>
/// <param name="Currencies">Currencies permitted for orders from this country, local currency first.</param>
/// <param name="IsCommonMonetaryArea">True for Common Monetary Area members (ZA, NA, LS, SZ).</param>
public sealed record SadcCountry(
    string Code,
    string Name,
    IReadOnlyList<Currency> Currencies,
    bool IsCommonMonetaryArea)
{
    public bool Accepts(string? currencyCode) =>
        Currencies.Any(c => string.Equals(c.Code, currencyCode?.Trim(), StringComparison.OrdinalIgnoreCase));
}
