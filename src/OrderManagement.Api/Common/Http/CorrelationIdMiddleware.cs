using System.Diagnostics;
using OrderManagement.Contracts;

namespace OrderManagement.Api.Common.Http;

/// <summary>
/// Gives every request a correlation ID: the caller's <c>X-Correlation-ID</c> if it is safe to log, otherwise
/// the W3C trace ID. The ID is echoed on the response, added to the log scope and the current trace, and
/// returned in every ProblemDetails, so one value ties together the UI, the API logs and (later) the worker.
/// </summary>
internal sealed class CorrelationIdMiddleware(RequestDelegate next, ILogger<CorrelationIdMiddleware> logger)
{
    private static readonly object ItemKey = new();

    public async Task InvokeAsync(HttpContext context)
    {
        var supplied = context.Request.Headers[ApiHeaders.CorrelationId].ToString();
        var correlationId = IsSafe(supplied)
            ? supplied
            : Activity.Current?.TraceId.ToString() ?? Guid.NewGuid().ToString("N");

        context.Items[ItemKey] = correlationId;
        Activity.Current?.SetTag("correlation.id", correlationId);
        context.Response.OnStarting(() =>
        {
            context.Response.Headers[ApiHeaders.CorrelationId] = correlationId;
            return Task.CompletedTask;
        });

        using (logger.BeginScope(new Dictionary<string, object> { ["CorrelationId"] = correlationId }))
        {
            await next(context);
        }
    }

    public static string? Get(HttpContext context) => context.Items.TryGetValue(ItemKey, out var id) ? id as string : null;

    internal static bool IsSafe(string? value) => CorrelationIds.IsSafe(value);
}
