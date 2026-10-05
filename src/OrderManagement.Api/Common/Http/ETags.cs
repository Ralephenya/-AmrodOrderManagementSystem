using Microsoft.Net.Http.Headers;

namespace OrderManagement.Api.Common.Http;

/// <summary>
/// Strong entity tags derived from SQL Server <c>rowversion</c>. The database changes the rowversion on every update,
/// so the ETag changes exactly when the resource changes. That gives cheap conditional GETs (304) and lets
/// <c>If-Match</c> reject writes based on a stale copy (412).
/// </summary>
public static class ETags
{
    public static string From(byte[] rowVersion) => $"\"{Convert.ToHexString(rowVersion)}\"";

    /// <summary>True when an <c>If-None-Match</c> header lists <paramref name="etag"/> or <c>*</c> (weak comparison, RFC 9110 §13.1.2).</summary>
    public static bool IfNoneMatchHits(HttpRequest request, string etag) =>
        Matches(request.Headers.IfNoneMatch, etag, weak: true);

    /// <summary>
    /// Null when the request has no <c>If-Match</c> header; otherwise whether it matches (strong comparison, RFC 9110 §13.1.1).
    /// </summary>
    public static bool? IfMatchSatisfied(HttpRequest request, string etag) =>
        IfMatchSatisfied(request.Headers.IfMatch.ToString(), etag);

    /// <inheritdoc cref="IfMatchSatisfied(HttpRequest, string)"/>
    public static bool? IfMatchSatisfied(string? ifMatchHeader, string etag) =>
        string.IsNullOrWhiteSpace(ifMatchHeader) ? null : Matches(ifMatchHeader, etag, weak: false);

    private static bool Matches(Microsoft.Extensions.Primitives.StringValues header, string etag, bool weak)
    {
        if (!EntityTagHeaderValue.TryParseList(header, out var tags))
        {
            return false;
        }

        var current = EntityTagHeaderValue.Parse(etag);
        return tags.Any(tag => tag.Equals(EntityTagHeaderValue.Any) || tag.Compare(current, useStrongComparison: !weak));
    }
}
