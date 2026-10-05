using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace OrderManagement.Infrastructure.Persistence;

/// <summary>
/// SQL Server <c>datetime2</c> has no time zone, so EF reads values back as <see cref="DateTimeKind.Unspecified"/>.
/// This converter normalises to UTC on write and stamps <see cref="DateTimeKind.Utc"/> on read.
/// </summary>
internal sealed class UtcDateTimeConverter() : ValueConverter<DateTime, DateTime>(
    v => ToUtc(v),
    v => DateTime.SpecifyKind(v, DateTimeKind.Utc))
{
    internal static DateTime ToUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
    };
}

internal sealed class NullableUtcDateTimeConverter() : ValueConverter<DateTime?, DateTime?>(
    v => v.HasValue ? UtcDateTimeConverter.ToUtc(v.Value) : v,
    v => v.HasValue ? DateTime.SpecifyKind(v.Value, DateTimeKind.Utc) : v);
