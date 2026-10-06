using Microsoft.OpenApi.Models;
using OrderManagement.Api.Common.Paging;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace OrderManagement.Api.OpenApi;

/// <summary>
/// Marks every non-nullable property of a response type as <c>required</c>, so generated clients (the web app's
/// openapi-typescript types) see <c>id: string</c> rather than <c>id?: string</c>. The API always writes these
/// properties. Request types are left alone: their optionality is the validators' business.
/// </summary>
internal sealed class RequiredResponsePropertiesSchemaFilter : ISchemaFilter
{
    public void Apply(OpenApiSchema schema, SchemaFilterContext context)
    {
        if (!IsResponseType(context.Type) || schema.Properties is null)
        {
            return;
        }

        foreach (var (name, property) in schema.Properties)
        {
            // A $ref property (an enum or nested object) carries no nullability of its own; it's non-null here.
            if (!property.Nullable)
            {
                schema.Required.Add(name);
            }
        }
    }

    private static bool IsResponseType(Type type) =>
        (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(PagedResult<>))
        || (type.Namespace?.StartsWith("OrderManagement.Api.Contracts", StringComparison.Ordinal) == true
            && (type.Name.EndsWith("Response", StringComparison.Ordinal) || type.Name == "RunningTotalPoint"));
}
