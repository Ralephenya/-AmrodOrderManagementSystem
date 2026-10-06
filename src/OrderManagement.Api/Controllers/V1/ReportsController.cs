using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OrderManagement.Api.Auth;
using OrderManagement.Api.Common;
using OrderManagement.Api.Contracts.Reports;
using OrderManagement.Api.Services.Interfaces;

namespace OrderManagement.Api.Controllers.V1;

/// <summary>
/// Sales reports, run as hand-written SQL through Dapper. Spend means Paid and Fulfilled orders only, always within one
/// currency. Cross-customer financial data, so these require <c>Orders.Admin</c>.
/// </summary>
[ApiVersion(ApiVersions.V1)]
[Route("api/v{version:apiVersion}/reports")]
[Authorize(Policy = AuthPolicies.OrdersAdmin)]
public sealed class ReportsController(IReportService reports) : ApiControllerBase
{
    /// <summary>Top customers by spend over a recent window, including customers who spent nothing.</summary>
    /// <remarks>
    /// Ranks every customer whose country can order in <c>currency</c> (for ZAR that includes the CMA countries),
    /// highest spend first. Customers with no qualifying orders are listed with a total of 0.
    /// </remarks>
    [HttpGet("top-spenders")]
    [ProducesResponseType<TopSpendersResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    public async Task<IActionResult> TopSpenders([FromQuery] TopSpendersQuery query, CancellationToken ct)
    {
        var result = await reports.TopSpendersAsync(query, ct);

        return result.Match(report => Ok(report), errors => Problem(errors));
    }

    /// <summary>A customer's cumulative spend, order by order, in one currency.</summary>
    [HttpGet("customers/{customerId:guid}/running-totals")]
    [ProducesResponseType<RunningTotalsResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    public async Task<IActionResult> RunningTotals(Guid customerId, [FromQuery] RunningTotalsQuery query, CancellationToken ct)
    {
        var result = await reports.RunningTotalsAsync(customerId, query, ct);

        return result.Match(report => Ok(report), errors => Problem(errors));
    }
}
