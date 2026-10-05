using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using OrderManagement.Api.Common.Errors;

namespace OrderManagement.Api.Common.Validation;

/// <summary>
/// Runs the registered FluentValidation validator for every action argument (request bodies and query objects)
/// before the action executes. Failures come back as the same 400 ProblemDetails as domain validation errors,
/// keyed by the JSON field path (<c>lineItems[0].quantity</c>).
/// </summary>
internal sealed class FluentValidationFilter(IServiceProvider services) : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        Dictionary<string, List<string>> failures = new(StringComparer.Ordinal);

        foreach (var argument in context.ActionArguments.Values)
        {
            if (argument is null)
            {
                continue;
            }

            var validatorType = typeof(IValidator<>).MakeGenericType(argument.GetType());
            if (services.GetService(validatorType) is not IValidator validator)
            {
                continue;
            }

            var result = await validator.ValidateAsync(
                new ValidationContext<object>(argument),
                context.HttpContext.RequestAborted);

            foreach (var failure in result.Errors)
            {
                var field = JsonFieldPath(failure.PropertyName);
                if (!failures.TryGetValue(field, out var messages))
                {
                    failures[field] = messages = [];
                }

                messages.Add(failure.ErrorMessage);
            }
        }

        if (failures.Count > 0)
        {
            var problem = ErrorOrProblems.Validation(failures.ToDictionary(f => f.Key, f => f.Value.ToArray()));
            ProblemDetailsEnricher.Enrich(context.HttpContext, problem);
            context.Result = new BadRequestObjectResult(problem);
            return;
        }

        await next();
    }

    /// <summary><c>LineItems[0].UnitPrice</c> → <c>lineItems[0].unitPrice</c>, matching the JSON the client sent.</summary>
    internal static string JsonFieldPath(string propertyPath) => string.Join(
        '.',
        propertyPath.Split('.').Select(segment => segment.Length == 0
            ? segment
            : char.ToLowerInvariant(segment[0]) + segment[1..]));
}
