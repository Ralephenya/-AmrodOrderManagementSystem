using Asp.Versioning;
using ErrorOr;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using OrderManagement.Api.Common.Paging;
using OrderManagement.Api.Common;
using OrderManagement.Api.Controllers;
using OrderManagement.Domain.Common;

namespace OrderManagement.IntegrationTests.Api.Probes;

/// <summary>
/// Test-only endpoints, loaded into the API by <see cref="Fixtures.ApiFactory"/> and never shipped. They let tests drive
/// the cross-cutting pipeline (exceptions, validation, paging, rate limiting) independently of any feature.
/// </summary>
[ApiVersion(ApiVersions.V1)]
[Route("api/v{version:apiVersion}/probes")]
[AllowAnonymous]
public sealed class ProbesController : ApiControllerBase
{
    public const string SecretMessage = "connection string: Server=prod;Password=hunter2";

    [HttpGet("boom")]
    public IActionResult Boom() => throw new InvalidOperationException(SecretMessage);

    [HttpGet("concurrency")]
    public IActionResult Concurrency() => throw new DbUpdateConcurrencyException("stale row");

    [HttpGet("domain-error/{kind}")]
    public IActionResult DomainError(string kind) => Problem(kind switch
    {
        "not-found" => [Error.NotFound("Order.NotFound", "We couldn't find an order with ID 42.")],
        "conflict" => [Error.Conflict("Order.InvalidStatusTransition", "A Fulfilled order can't be changed any more.")],
        "validation" =>
        [
            Error.Validation("Sadc.CurrencyNotAllowedForCountry", "Customers in South Africa can only order in ZAR, not BWP.",
                ErrorMetadata.ForField("currencyCode")),
            Error.Validation("Order.QuantityMustBePositive", "Line 1: quantity must be at least 1.",
                ErrorMetadata.ForField("lineItems[0].quantity")),
        ],
        _ => [Error.Unexpected("Internal.Detail", SecretMessage)],
    });

    [HttpPost("validate")]
    public IActionResult Validate(ProbeRequest request) => NoContent();

    [HttpGet("paged")]
    public IActionResult Paged([FromQuery] ProbePageQuery query) =>
        Ok(new { query.Page, query.PageSize, Sort = SortSpec.Parse(query.Sort, new SortSpec("createdAt", true)) });

    [HttpPost("write")]
    [EnableRateLimiting("writes")]
    public IActionResult Write() => NoContent();
}

public sealed record ProbeRequest(string? Name, IReadOnlyList<ProbeLine>? LineItems);

public sealed record ProbeLine(string? ProductSku, int Quantity);

public sealed class ProbeRequestValidator : AbstractValidator<ProbeRequest>
{
    public ProbeRequestValidator()
    {
        RuleFor(r => r.Name).NotEmpty().WithMessage("Please enter a name.");
        RuleForEach(r => r.LineItems).ChildRules(line =>
            line.RuleFor(l => l.Quantity).GreaterThan(0).WithMessage("Quantity must be at least 1."));
    }
}

public sealed class ProbePageQuery : PageQuery
{
    public string? Sort { get; init; }
}

public sealed class ProbePageQueryValidator : AbstractValidator<ProbePageQuery>
{
    public ProbePageQueryValidator()
    {
        Include(new PageQueryValidator());
        RuleFor(q => q.Sort).MustBeSortableBy("createdAt", "total");
    }
}

/// <summary>No [Authorize] or [AllowAnonymous]: proves the fallback policy protects forgotten endpoints.</summary>
[ApiVersion(ApiVersions.V1)]
[Route("api/v{version:apiVersion}/probes/unannotated")]
public sealed class UnannotatedProbeController : ApiControllerBase
{
    [HttpGet]
    public IActionResult Get() => Ok();
}
