using Microsoft.AspNetCore.Mvc;
using OrderManagement.Api.Common.Errors;

namespace OrderManagement.Api.Common.Validation;

/// <summary>
/// Replaces MVC's automatic 400 (malformed JSON, wrong types, missing body) with our ProblemDetails contract
/// and plain-language messages. The raw serializer messages ("could not be converted to System.Int32. Path: …")
/// mean nothing to a user.
/// </summary>
internal static class ModelBindingProblems
{
    public static IActionResult Create(ActionContext context)
    {
        var fieldErrors = context.ModelState
            .Where(entry => entry.Value?.Errors.Count > 0)
            .ToDictionary(
                entry => FieldName(entry.Key),
                entry => entry.Value!.Errors.Select(e => FriendlyMessage(entry.Key, e.ErrorMessage)).Distinct().ToArray(),
                StringComparer.Ordinal);

        var problem = ErrorOrProblems.Validation(fieldErrors);
        ProblemDetailsEnricher.Enrich(context.HttpContext, problem);
        return new BadRequestObjectResult(problem);
    }

    private static string FieldName(string key) => key switch
    {
        "" or "$" => "body",
        _ when key.StartsWith("$.", StringComparison.Ordinal) => key[2..],
        _ => FluentValidationFilter.JsonFieldPath(key),
    };

    private static string FriendlyMessage(string key, string message)
    {
        if (key is "" or "$" || message.Contains("field is required", StringComparison.OrdinalIgnoreCase))
        {
            return key is "" or "$" || !key.Contains('.', StringComparison.Ordinal)
                ? "The request body is missing or isn't valid JSON."
                : "This field is required.";
        }

        return key.StartsWith('$')
            ? "This value isn't in the expected format."
            : message;
    }
}
