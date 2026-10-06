namespace OrderManagement.Contracts;

/// <summary>
/// Health check tags, shared so a check's registration (Infrastructure) and its endpoint (ServiceDefaults) can't drift:
/// <c>/healthz</c> runs <see cref="Live"/> checks and <c>/readiness</c> runs <see cref="Ready"/> checks.
/// </summary>
public static class HealthTags
{
    /// <summary>The process is up. Must not depend on anything external.</summary>
    public const string Live = "live";

    /// <summary>The instance can serve traffic: database and message broker reachable. MassTransit tags its checks "ready" too.</summary>
    public const string Ready = "ready";
}
