using System.Globalization;
using Asp.Versioning;
using ErrorOr;
using Microsoft.AspNetCore.Mvc;
using OrderManagement.Api.Common.Errors;

namespace OrderManagement.Api.Controllers;

/// <summary>
/// Shared behaviour for API controllers: ErrorOr → ProblemDetails, and the error responses every endpoint can return.
/// (No [Produces] here: it would override the application/problem+json content type of error responses.)
/// </summary>
[ApiController]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests, "application/problem+json")]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status500InternalServerError, "application/problem+json")]
public abstract class ApiControllerBase : ControllerBase
{
    /// <summary>
    /// The requested API version in URL form (<c>1</c> for v1), for building <c>Location</c> headers that
    /// point back at the same version the client called.
    /// </summary>
    protected string RouteVersion =>
        (HttpContext.GetRequestedApiVersion() ?? Common.ApiVersions.Default).ToString("VVV", CultureInfo.InvariantCulture);

    /// <summary>Returns the ProblemDetails response for domain errors (see <see cref="ErrorOrProblems"/>).</summary>
    protected IActionResult Problem(IReadOnlyList<Error> errors)
    {
        var problem = ErrorOrProblems.Create(HttpContext, errors);
        return new ObjectResult(problem)
        {
            StatusCode = problem.Status,
            ContentTypes = { "application/problem+json" },
        };
    }
}
