using HotChocolate;
using HotChocolate.Execution;
using OrderManagement.Api.Common.Http;

namespace OrderManagement.Api.GraphQL;

/// <summary>
/// Brings GraphQL errors in line with the REST error contract:
/// <list type="bullet">
/// <item>Every error carries the request's <c>correlationId</c> in its extensions, to quote to support.</item>
/// <item>An unexpected exception in a resolver is logged with that id. Hot Chocolate catches resolver exceptions
/// itself, so they never reach the global exception handler and would otherwise go unlogged.</item>
/// </list>
/// Expected errors (validation, authorization, page size) are not logged: they are the client's to fix.
/// </summary>
internal sealed partial class GraphQLErrorFilter(IHttpContextAccessor httpContextAccessor, ILogger<GraphQLErrorFilter> logger)
    : IErrorFilter
{
    public IError OnError(IError error)
    {
        ArgumentNullException.ThrowIfNull(error);

        var http = httpContextAccessor.HttpContext;
        var correlationId = http is null ? null : CorrelationIdMiddleware.Get(http);

        if (error.Exception is { } exception && exception is not OperationCanceledException)
        {
            LogResolverFailure(logger, exception, error.Path?.Print() ?? "(request)", correlationId);
        }

        return correlationId is null ? error : error.SetExtension("correlationId", correlationId);
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "GraphQL resolver failed at {Path} (correlation {CorrelationId})")]
    private static partial void LogResolverFailure(ILogger logger, Exception exception, string path, string? correlationId);
}
