using OrderManagement.Domain.Sadc;

namespace OrderManagement.Domain.Common;

/// <summary>
/// Money rules shared by the domain and persistence.
/// </summary>
/// <remarks>
/// Amounts are <see cref="decimal"/>, never floating point, and are stored as <c>decimal(18,2)</c>.
/// Unit prices are rejected if they have more decimal places than their currency allows (e.g. 10.005 ZAR, or
/// 10.5 KMF). That makes every line total (<c>Quantity × UnitPrice</c>) and the order total <b>exact</b>, so no
/// rounding step exists that could drift between the API, the database and reports.
/// </remarks>
public static class Money
{
    public const int Precision = 18;
    public const int Scale = 2;

    /// <summary>The largest amount a <c>decimal(18,2)</c> column can hold.</summary>
    public const decimal MaxAmount = 9_999_999_999_999_999.99m;

    /// <summary>Number of significant decimal places, ignoring trailing zeros (10.50 → 1, 10.500 → 1).</summary>
    public static int DecimalPlaces(decimal amount)
    {
        // Dividing by 1.000…0 (28 zeros) strips trailing zeros from the scale without changing the value.
        var normalised = amount / 1.0000000000000000000000000000m;
        return decimal.GetBits(normalised)[3] >> 16 & 0xFF;
    }

    /// <summary>True when <paramref name="amount"/> can be represented exactly in <paramref name="currency"/>.</summary>
    public static bool FitsCurrency(decimal amount, Currency currency) =>
        DecimalPlaces(amount) <= currency.MinorUnits;
}
