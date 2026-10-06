namespace OrderManagement.Contracts;

/// <summary>Transport headers shared by publishers and consumers.</summary>
public static class MessageHeaderNames
{
    /// <summary>
    /// The HTTP request's <c>X-Correlation-ID</c>, carried on every message the request causes, so one ID ties the
    /// API log lines to the worker log lines. (W3C trace context travels alongside it automatically.)
    /// </summary>
    public const string CorrelationId = "X-Correlation-ID";
}
