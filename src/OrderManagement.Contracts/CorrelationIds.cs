namespace OrderManagement.Contracts;

/// <summary>The one rule for which correlation IDs are trusted, shared by the API (HTTP header) and the worker (message header).</summary>
public static class CorrelationIds
{
    public const int MaxLength = 64;

    /// <summary>Only short, plain identifiers are trusted, which keeps header values from injecting into logs.</summary>
    public static bool IsSafe(string? value) =>
        !string.IsNullOrEmpty(value)
        && value.Length <= MaxLength
        && value.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.');
}
