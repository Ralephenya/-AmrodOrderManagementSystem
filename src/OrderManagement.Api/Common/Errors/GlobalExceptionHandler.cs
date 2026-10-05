using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace OrderManagement.Api.Common.Errors;

/// <summary>
/// Last line of defence. Logs the exception with full detail, and returns a ProblemDetails that never contains
/// stack traces or internal messages, only a friendly explanation and the correlation ID to quote.
/// </summary>
internal sealed partial class GlobalExceptionHandler(
    IProblemDetailsService problemDetails,
    ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext http, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is OperationCanceledException && http.RequestAborted.IsCancellationRequested)
        {
            // The client went away; nobody is waiting for a response.
            http.Response.StatusCode = 499;
            return true;
        }

        var problem = exception switch
        {
            DbUpdateConcurrencyException => new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "This record was changed by someone else",
                Detail = "Refresh to see the latest version, then try again.",
                Extensions = { [ProblemCatalog.CodeKey] = "concurrency_conflict" },
            },
            BadHttpRequestException bad => new ProblemDetails { Status = bad.StatusCode },
            _ => new ProblemDetails { Status = StatusCodes.Status500InternalServerError },
        };

        if (problem.Status >= 500)
        {
            LogUnhandled(logger, exception, http.Request.Method, http.Request.Path);
        }
        else
        {
            LogHandled(logger, exception.GetType().Name, problem.Status ?? 0, http.Request.Method, http.Request.Path);
        }

        http.Response.StatusCode = problem.Status ?? 500;
        ProblemDetailsEnricher.Enrich(http, problem);

        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = http,
            ProblemDetails = problem,
            Exception = exception,
        });
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Unhandled exception for {Method} {Path}")]
    private static partial void LogUnhandled(ILogger logger, Exception exception, string method, string path);

    [LoggerMessage(Level = LogLevel.Warning, Message = "{ExceptionType} mapped to {Status} for {Method} {Path}")]
    private static partial void LogHandled(ILogger logger, string exceptionType, int status, string method, string path);
}
