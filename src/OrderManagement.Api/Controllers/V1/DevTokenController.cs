using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using OrderManagement.Api.Auth;
using OrderManagement.Api.Common;
using OrderManagement.Api.Contracts.Auth;

namespace OrderManagement.Api.Controllers.V1;

/// <summary>
/// Mints mock-Entra access tokens so Swagger, the web app and tests can call the API without a tenant.
/// Exists only when <c>Auth:Mode</c> is <c>Mock</c> in Development or Testing; everywhere else it is a 404.
/// </summary>
[ApiVersion(ApiVersions.V1)]
[Route("api/v{version:apiVersion}/dev/token")]
[AllowAnonymous]
public sealed class DevTokenController(IOptions<AuthOptions> options, IHostEnvironment environment, IServiceProvider services)
    : ApiControllerBase
{
    /// <summary>Issue a development token with the requested roles.</summary>
    [HttpPost]
    [ProducesResponseType<DevTokenResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    public IActionResult Issue(DevTokenRequest request)
    {
        var enabled = options.Value.Mode == AuthMode.Mock
            && (environment.IsDevelopment() || environment.IsEnvironment("Testing"));
        if (!enabled || services.GetService<MockTokenIssuer>() is not { } issuer)
        {
            return NotFound();
        }

        var roles = request.Roles!.Distinct(StringComparer.Ordinal).ToArray();
        var token = issuer.Issue(string.IsNullOrWhiteSpace(request.Name) ? "Developer" : request.Name.Trim(), roles);

        return Ok(new DevTokenResponse(token, "Bearer", (int)issuer.Lifetime.TotalSeconds, roles));
    }
}
