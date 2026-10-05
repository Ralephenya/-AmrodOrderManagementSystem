using ErrorOr;
using Microsoft.AspNetCore.Mvc;
using OrderManagement.Domain.Common;

namespace OrderManagement.Api.Common.Errors;

/// <summary>Turns domain <see cref="Error"/>s into ProblemDetails responses.</summary>
internal static class ErrorOrProblems
{
    /// <summary>
    /// All validation errors become one 400 with a per-field <c>errors</c> dictionary, so a form can highlight
    /// every problem at once. Anything else maps the first error's type to its status code, and its
    /// description becomes the <c>detail</c> shown to the user.
    /// </summary>
    public static ProblemDetails Create(HttpContext http, IReadOnlyList<Error> errors)
    {
        ArgumentNullException.ThrowIfNull(errors);

        ProblemDetails problem;
        if (errors.Count > 0 && errors.All(e => e.Type == ErrorType.Validation))
        {
            problem = Validation(ToFieldErrors(errors));
        }
        else
        {
            var first = errors.Count > 0 ? errors[0] : Error.Unexpected();
            var status = StatusFor(first.Type);
            var entry = ProblemCatalog.For(status);
            problem = new ProblemDetails
            {
                Status = status,
                Title = entry.Title,
                // Never leak internal descriptions for unexpected failures.
                Detail = status >= 500 ? entry.Detail : first.Description,
                Extensions = { [ProblemCatalog.CodeKey] = status >= 500 ? entry.Code : ProblemCatalog.ToWireCode(first.Code) },
            };
        }

        ProblemDetailsEnricher.Enrich(http, problem);
        return problem;
    }

    public static ValidationProblemDetails Validation(IDictionary<string, string[]> fieldErrors) => new(fieldErrors)
    {
        Status = StatusCodes.Status400BadRequest,
        Title = ProblemCatalog.ValidationTitle,
        Detail = ProblemCatalog.ValidationDetail,
        Extensions = { [ProblemCatalog.CodeKey] = ProblemCatalog.ValidationCode },
    };

    internal static Dictionary<string, string[]> ToFieldErrors(IEnumerable<Error> errors) =>
        errors
            .GroupBy(FieldFor, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Select(e => e.Description).Distinct().ToArray(), StringComparer.Ordinal);

    internal static int StatusFor(ErrorType type) => type switch
    {
        ErrorType.Validation => StatusCodes.Status400BadRequest,
        ErrorType.Unauthorized => StatusCodes.Status403Forbidden, // authenticated, but not allowed to do this
        ErrorType.Forbidden => StatusCodes.Status403Forbidden,
        ErrorType.NotFound => StatusCodes.Status404NotFound,
        ErrorType.Conflict => StatusCodes.Status409Conflict,
        ErrorType.Failure => StatusCodes.Status422UnprocessableEntity,
        _ => StatusCodes.Status500InternalServerError,
    };

    private static string FieldFor(Error error) =>
        error.Metadata?.TryGetValue(ErrorMetadata.FieldKey, out var field) == true && field is string name
            ? name
            : CamelCase(error.Code[(error.Code.LastIndexOf('.') + 1)..]);

    private static string CamelCase(string value) =>
        value.Length == 0 ? value : char.ToLowerInvariant(value[0]) + value[1..];
}
