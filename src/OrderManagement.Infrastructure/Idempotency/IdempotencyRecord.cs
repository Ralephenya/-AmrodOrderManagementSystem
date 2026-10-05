namespace OrderManagement.Infrastructure.Idempotency;

/// <summary>
/// The stored outcome of a request made with an <c>Idempotency-Key</c>. A retry with the same key and the same request
/// gets this outcome back instead of being executed again. A different request under the same key is rejected.
/// </summary>
public sealed class IdempotencyRecord
{
    public const int ClientIdMaxLength = 64;
    public const int KeyMaxLength = 128;

    /// <summary>Who sent the request (the token's <c>oid</c>). Keys are scoped per client so clients can't collide.</summary>
    public string ClientId { get; set; } = string.Empty;

    public string Key { get; set; } = string.Empty;

    /// <summary>SHA-256 (hex) of method, route, resource ID and body, used to detect key reuse for a different request.</summary>
    public string RequestHash { get; set; } = string.Empty;

    public IdempotencyOutcome Outcome { get; set; }

    /// <summary>JSON of the stored response (success payload or errors), replayed verbatim.</summary>
    public string Payload { get; set; } = string.Empty;

    /// <summary>UTC.</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>UTC. Expired records are ignored and replaced; the key can then be reused.</summary>
    public DateTime ExpiresAt { get; set; }
}

public enum IdempotencyOutcome
{
    Succeeded = 0,
    Failed = 1,
}
