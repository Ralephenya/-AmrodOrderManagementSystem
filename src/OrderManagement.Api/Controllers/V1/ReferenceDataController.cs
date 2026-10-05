using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OrderManagement.Api.Auth;
using OrderManagement.Api.Common;
using OrderManagement.Api.Contracts.ReferenceData;
using OrderManagement.Domain.Sadc;

namespace OrderManagement.Api.Controllers.V1;

/// <summary>Reference data the UI needs to build forms (countries and the currencies each may order in).</summary>
[ApiVersion(ApiVersions.V1)]
[Route("api/v{version:apiVersion}/reference")]
[Authorize(Policy = AuthPolicies.OrdersRead)]
public sealed class ReferenceDataController : ApiControllerBase
{
    /// <summary>The 16 SADC countries we serve and the ISO 4217 currencies permitted for each.</summary>
    /// <remarks>
    /// Namibia, Lesotho and Eswatini (Common Monetary Area) also accept ZAR. Zimbabwe accepts ZWL and USD.
    /// Use <c>minorUnits</c> to format amounts and to limit decimal places in price inputs.
    /// </remarks>
    [HttpGet("countries")]
    [ResponseCache(Duration = 3600, Location = ResponseCacheLocation.Client)]
    [ProducesResponseType<IReadOnlyList<CountryResponse>>(StatusCodes.Status200OK)]
    public IActionResult GetCountries() => Ok(CountryResponses);

    private static readonly IReadOnlyList<CountryResponse> CountryResponses =
    [
        .. SadcCatalogue.Countries.Select(c => new CountryResponse(
            c.Code,
            c.Name,
            c.IsCommonMonetaryArea,
            [.. c.Currencies.Select(cur => new CurrencyResponse(cur.Code, cur.Name, cur.MinorUnits))])),
    ];
}
