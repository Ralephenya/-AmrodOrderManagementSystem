using System.Text.Json;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace OrderManagement.Api.OpenApi;

/// <summary>
/// Names query parameters bound from a query class (<c>PageSize</c>) in camelCase (<c>pageSize</c>), matching the
/// JSON bodies and the documented URLs. Binding is case-insensitive, so both spellings keep working.
/// </summary>
internal sealed class CamelCaseQueryParametersOperationFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        foreach (var parameter in operation.Parameters.Where(p => p.In == ParameterLocation.Query))
        {
            parameter.Name = JsonNamingPolicy.CamelCase.ConvertName(parameter.Name);
        }
    }
}
