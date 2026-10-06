using MassTransit;
using Microsoft.Extensions.Logging;
using OrderManagement.Contracts;

namespace OrderManagement.Infrastructure.Messaging;

/// <summary>
/// Opens a log scope with the originating HTTP request's correlation ID around every consumer, so worker log lines can be
/// found with the same ID the user saw in the API's error response.
/// </summary>
public sealed class CorrelationIdConsumeFilter<T>(ILogger<CorrelationIdConsumeFilter<T>> logger) : IFilter<ConsumeContext<T>>
    where T : class
{
    public async Task Send(ConsumeContext<T> context, IPipe<ConsumeContext<T>> next)
    {
        // Any publisher can set this header, so apply the same rule the API applies to the HTTP header.
        var header = context.Headers.Get<string>(MessageHeaderNames.CorrelationId);
        var correlationId = CorrelationIds.IsSafe(header)
            ? header
            : context.CorrelationId?.ToString("N") ?? context.MessageId?.ToString("N");

        using (logger.BeginScope(new Dictionary<string, object?>
        {
            ["CorrelationId"] = correlationId,
            ["MessageId"] = context.MessageId,
            ["MessageType"] = typeof(T).Name,
        }))
        {
            await next.Send(context);
        }
    }

    public void Probe(ProbeContext context) => context.CreateFilterScope("correlationId");
}
