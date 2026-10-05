using System.Security.Cryptography;
using System.Text;
using OrderManagement.Api.Common.Http;
using OrderManagement.Infrastructure.Idempotency;

namespace OrderManagement.Api.Common.Idempotency;

/// <summary>
/// The idempotency identity of one request: who sent it, the key they chose, and a fingerprint of what they asked for.
/// </summary>
public sealed record IdempotencyRequest(string ClientId, string Key, string RequestHash)
{
    public static readonly TimeSpan RetentionPeriod = TimeSpan.FromHours(24);

    /// <summary>
    /// Builds the identity from the <c>Idempotency-Key</c> header. Keys are 1–128 visible ASCII characters
    /// (a UUID is ideal). <paramref name="fingerprint"/> must contain every input that changes the outcome
    /// (route, resource ID, body).
    /// </summary>
    public static bool TryCreate(HttpContext http, string fingerprint, out IdempotencyRequest? request, out string? error)
    {
        request = null;
        var key = http.Request.Headers[ApiHeaders.IdempotencyKey].ToString().Trim();

        if (key.Length == 0)
        {
            error = $"Send an {ApiHeaders.IdempotencyKey} header (for example a new UUID) so a retry can't apply this change twice.";
            return false;
        }

        if (key.Length > IdempotencyRecord.KeyMaxLength || !key.All(c => c is > ' ' and <= '~'))
        {
            error = $"{ApiHeaders.IdempotencyKey} must be 1–{IdempotencyRecord.KeyMaxLength} visible characters, such as a UUID.";
            return false;
        }

        // Scope keys to the caller: Entra's object ID, never the display name. Anonymous callers can't reach writes.
        var clientId = http.User.FindFirst("oid")?.Value ?? http.User.Identity?.Name ?? "anonymous";
        if (clientId.Length > IdempotencyRecord.ClientIdMaxLength)
        {
            clientId = Hash(clientId);
        }

        request = new IdempotencyRequest(clientId, key, Hash($"{http.Request.Method}\n{fingerprint}"));
        error = null;
        return true;
    }

    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
