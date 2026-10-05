using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using OrderManagement.Api.Auth;
using OrderManagement.Api.Common;
using OrderManagement.Api.Common.Paging;
using OrderManagement.Api.Contracts.Customers;
using OrderManagement.Api.Services.Interfaces;

namespace OrderManagement.Api.Controllers.V1;

/// <summary>Customers in SADC member states.</summary>
[ApiVersion(ApiVersions.V1)]
[Route("api/v{version:apiVersion}/customers")]
public sealed class CustomersController(ICustomerService customers) : ApiControllerBase
{
    /// <summary>Create a customer.</summary>
    /// <remarks>
    /// The email is stored lower-cased and must be unique. The country must be a SADC member state; it decides which
    /// currencies the customer may order in.
    /// </remarks>
    [HttpPost]
    [Authorize(Policy = AuthPolicies.OrdersWrite)]
    [EnableRateLimiting(ApiSetup.WritesRateLimit)]
    [ProducesResponseType<CustomerResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")]
    public async Task<IActionResult> Create(CreateCustomerRequest request, CancellationToken ct)
    {
        var result = await customers.CreateAsync(request, ct);

        return result.Match(
            customer => CreatedAtAction(nameof(GetById), new { id = customer.Id, version = RouteVersion }, customer),
            errors => Problem(errors));
    }

    /// <summary>Get a customer by ID.</summary>
    [HttpGet("{id:guid}")]
    [Authorize(Policy = AuthPolicies.OrdersRead)]
    [ProducesResponseType<CustomerResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        var result = await customers.GetByIdAsync(id, ct);

        return result.Match(customer => Ok(customer), errors => Problem(errors));
    }

    /// <summary>Search customers by name or email prefix, paged.</summary>
    /// <remarks>
    /// <c>search</c> matches names or emails that start with the text. <c>pageSize</c> is at most 100.
    /// <c>sort</c> is <c>name</c> (default) or <c>createdAt</c>; prefix with <c>-</c> for descending.
    /// </remarks>
    [HttpGet]
    [Authorize(Policy = AuthPolicies.OrdersRead)]
    [ProducesResponseType<PagedResult<CustomerResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    public async Task<IActionResult> List([FromQuery] CustomerListQuery query, CancellationToken ct) =>
        Ok(await customers.ListAsync(query, ct));
}
