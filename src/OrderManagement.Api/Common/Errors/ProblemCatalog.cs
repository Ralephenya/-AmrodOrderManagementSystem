using System.Text;
using Microsoft.AspNetCore.WebUtilities;

namespace OrderManagement.Api.Common.Errors;

/// <summary>
/// Friendly wording and stable machine codes for every error status the API returns. ProblemDetails stays
/// RFC 7807-compliant for tools, and <c>title</c>/<c>detail</c> are written for the person reading them.
/// </summary>
internal static class ProblemCatalog
{
    public const string CodeKey = "code";
    public const string TraceIdKey = "traceId";
    public const string CorrelationIdKey = "correlationId";

    public const string ValidationCode = "validation_failed";
    public const string ValidationTitle = "Some details need fixing";
    public const string ValidationDetail = "Please correct the highlighted fields and try again.";

    private static readonly Dictionary<int, Entry> ByStatus = new()
    {
        [400] = new("bad_request", "We couldn't understand that request", "Please check the request and try again."),
        [401] = new("unauthorized", "You need to sign in", "Your session is missing or has expired. Sign in and try again."),
        [403] = new("forbidden", "You don't have access to this", "Your account doesn't have permission to do this. Ask an administrator if you need access."),
        [404] = new("not_found", "We couldn't find that", "The page or record you asked for doesn't exist."),
        [405] = new("method_not_allowed", "That action isn't supported here", "This address doesn't support that kind of request."),
        [409] = new("conflict", "That change conflicts with the current state", "Refresh to see the latest version, then try again."),
        [413] = new("payload_too_large", "That request is too large", "Send less data in one request and try again."),
        [415] = new("unsupported_media_type", "Unsupported content type", "Send the request body as JSON (application/json)."),
        [422] = new("unprocessable", "We couldn't process that request", "The request was understood but can't be completed."),
        [428] = new("precondition_required", "A required header is missing", "This request needs an extra header. See the API documentation."),
        [429] = new("rate_limited", "Too many requests", "You're sending requests too quickly. Wait a moment and try again."),
        [500] = new("server_error", "Something went wrong on our side", "Please try again. If it keeps happening, contact support and quote the reference below."),
        [503] = new("service_unavailable", "The service is temporarily unavailable", "Please try again in a moment."),
    };

    // RFC 9110 section for each status, matching the links ASP.NET Core puts on its own ProblemDetails.
    private static readonly Dictionary<int, string> TypeByStatus = new()
    {
        [400] = "https://tools.ietf.org/html/rfc9110#section-15.5.1",
        [401] = "https://tools.ietf.org/html/rfc9110#section-15.5.2",
        [403] = "https://tools.ietf.org/html/rfc9110#section-15.5.4",
        [404] = "https://tools.ietf.org/html/rfc9110#section-15.5.5",
        [405] = "https://tools.ietf.org/html/rfc9110#section-15.5.6",
        [409] = "https://tools.ietf.org/html/rfc9110#section-15.5.10",
        [413] = "https://tools.ietf.org/html/rfc9110#section-15.5.14",
        [415] = "https://tools.ietf.org/html/rfc9110#section-15.5.16",
        [422] = "https://tools.ietf.org/html/rfc9110#section-15.5.21",
        [428] = "https://tools.ietf.org/html/rfc6585#section-3",
        [429] = "https://tools.ietf.org/html/rfc6585#section-4",
        [500] = "https://tools.ietf.org/html/rfc9110#section-15.6.1",
        [503] = "https://tools.ietf.org/html/rfc9110#section-15.6.4",
    };

    /// <summary>The RFC section describing <paramref name="status"/>, used as ProblemDetails <c>type</c> when none is set.</summary>
    public static string TypeFor(int status) =>
        TypeByStatus.TryGetValue(status, out var type) ? type : "about:blank";

    public static Entry For(int status) =>
        ByStatus.TryGetValue(status, out var entry)
            ? entry
            : new Entry(ToSnakeCase(ReasonPhrases.GetReasonPhrase(status)), ReasonPhrases.GetReasonPhrase(status), null);

    /// <summary>Maps a domain error code (<c>Order.InvalidStatusTransition</c>) to a wire code (<c>invalid_status_transition</c>).</summary>
    public static string ToWireCode(string domainCode)
    {
        var lastSegment = domainCode[(domainCode.LastIndexOf('.') + 1)..];
        return ToSnakeCase(lastSegment);
    }

    internal static string ToSnakeCase(string value)
    {
        var builder = new StringBuilder(value.Length + 8);
        foreach (var c in value)
        {
            if (char.IsUpper(c))
            {
                if (builder.Length > 0 && builder[^1] != '_')
                {
                    builder.Append('_');
                }

                builder.Append(char.ToLowerInvariant(c));
            }
            else if (char.IsLetterOrDigit(c))
            {
                builder.Append(c);
            }
            else if (builder.Length > 0 && builder[^1] != '_')
            {
                builder.Append('_');
            }
        }

        return builder.ToString().Trim('_');
    }

    public sealed record Entry(string Code, string Title, string? Detail);
}
