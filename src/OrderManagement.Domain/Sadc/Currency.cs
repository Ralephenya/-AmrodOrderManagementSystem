namespace OrderManagement.Domain.Sadc;

/// <summary>An ISO 4217 currency.</summary>
/// <param name="Code">Three-letter ISO 4217 code, upper case (e.g. <c>ZAR</c>).</param>
/// <param name="Name">Display name.</param>
/// <param name="MinorUnits">Decimal places the currency uses (e.g. 2 for ZAR, 0 for KMF).</param>
public sealed record Currency(string Code, string Name, int MinorUnits);
