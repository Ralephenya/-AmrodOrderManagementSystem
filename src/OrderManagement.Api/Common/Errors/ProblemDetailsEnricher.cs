using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using OrderManagement.Api.Common.Http;

namespace OrderManagement.Api.Common.Errors;

/// <summary>
/// Runs on every ProblemDetails the API writes, whether it comes from our ErrorOr mapping, MVC, auth, routing,
/// API versioning or the exception handler. It guarantees one consistent contract: a stable <c>code</c>,
/// friendly <c>title</c>/<c>detail</c> in place of framework defaults, and <c>traceId</c> + <c>correlationId</c>.
/// </summary>
internal static class ProblemDetailsEnricher
{
    private const string FrameworkValidationTitle = "One or more validation errors occurred.";
    private const string VersioningProblemPrefix = "https://docs.api-versioning.org/problems#";

    public static void Enrich(HttpContext http, ProblemDetails problem)
    {
        var status = problem.Status ??= http.Response.StatusCode >= 400 ? http.Response.StatusCode : 500;
        var entry = ProblemCatalog.For(status);
        var isValidation = problem is ValidationProblemDetails;

        if (!problem.Extensions.ContainsKey(ProblemCatalog.CodeKey))
        {
            problem.Extensions[ProblemCatalog.CodeKey] = problem.Type?.StartsWith(VersioningProblemPrefix, StringComparison.Ordinal) == true
                ? ProblemCatalog.ToSnakeCase(problem.Type[VersioningProblemPrefix.Length..])
                : isValidation ? ProblemCatalog.ValidationCode : entry.Code;
        }

        // Replace framework boilerplate ("Not Found", "One or more validation errors occurred.") with plain language.
        // Titles we (or a library) chose deliberately are left alone.
        var hasDefaultTitle = problem.Title is null
            || problem.Title == ReasonPhrases.GetReasonPhrase(status)
            || problem.Title == FrameworkValidationTitle;
        if (hasDefaultTitle)
        {
            problem.Title = isValidation ? ProblemCatalog.ValidationTitle : entry.Title;
            problem.Detail ??= isValidation ? ProblemCatalog.ValidationDetail : entry.Detail;
        }

        problem.Instance ??= http.Request.Path;
        problem.Extensions.TryAdd(ProblemCatalog.TraceIdKey, Activity.Current?.Id ?? http.TraceIdentifier);

        var correlationId = CorrelationIdMiddleware.Get(http);
        if (correlationId is not null)
        {
            problem.Extensions.TryAdd(ProblemCatalog.CorrelationIdKey, correlationId);
        }
    }
}
