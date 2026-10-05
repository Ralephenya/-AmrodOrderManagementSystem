using FluentValidation;

namespace OrderManagement.Api.Common.Paging;

/// <summary>
/// A parsed <c>sort</c> query value: <c>createdAt</c> sorts ascending, <c>-createdAt</c> sorts descending.
/// Field names are matched case-insensitively against a per-endpoint allow-list, so clients can never sort by
/// an arbitrary (unindexed) column.
/// </summary>
public sealed record SortSpec(string Field, bool Descending)
{
    public static SortSpec Parse(string? value, SortSpec fallback)
    {
        ArgumentNullException.ThrowIfNull(fallback);

        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        var trimmed = value.Trim();
        return trimmed.StartsWith('-')
            ? new SortSpec(trimmed[1..], Descending: true)
            : new SortSpec(trimmed, Descending: false);
    }

    public bool Is(string field) => string.Equals(Field, field, StringComparison.OrdinalIgnoreCase);
}

public static class SortValidationExtensions
{
    /// <summary>Accepts an empty value or one of <paramref name="fields"/>, optionally prefixed with <c>-</c>.</summary>
    public static IRuleBuilderOptions<T, string?> MustBeSortableBy<T>(
        this IRuleBuilder<T, string?> rule,
        params string[] fields) =>
        rule
            .Must(value => string.IsNullOrWhiteSpace(value)
                || fields.Contains(value.Trim().TrimStart('-'), StringComparer.OrdinalIgnoreCase))
            .WithMessage($"You can sort by {string.Join(" or ", fields)}. Add a leading '-' for descending (e.g. -{fields[0]}).");
}
