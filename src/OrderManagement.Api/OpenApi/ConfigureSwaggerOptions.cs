using Asp.Versioning.ApiExplorer;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace OrderManagement.Api.OpenApi;

/// <summary>One OpenAPI document per API version, each with bearer auth so "Authorize" works in Swagger UI.</summary>
internal sealed class ConfigureSwaggerOptions(IApiVersionDescriptionProvider versions) : IConfigureOptions<SwaggerGenOptions>
{
    public const string BearerScheme = "Bearer";

    public void Configure(SwaggerGenOptions options)
    {
        foreach (var version in versions.ApiVersionDescriptions)
        {
            options.SwaggerDoc(version.GroupName, new OpenApiInfo
            {
                Title = "Amrod Order Management API",
                Version = version.ApiVersion.ToString(),
                Description =
                    "Customers and orders for SADC markets. Unversioned paths (/api/orders) are aliases of the " +
                    "current version. Errors use RFC 7807 ProblemDetails with a stable `code`, a friendly " +
                    "`title`/`detail`, and a `correlationId` to quote to support." +
                    (version.IsDeprecated ? " **This version is deprecated.**" : string.Empty),
            });
        }

        options.AddSecurityDefinition(BearerScheme, new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
            Description = "Microsoft Entra access token. In Development, get one from POST /api/v1/dev/token.",
        });
        options.AddSecurityRequirement(new OpenApiSecurityRequirement
        {
            [new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = BearerScheme },
            }] = [],
        });

        options.SupportNonNullableReferenceTypes();
        options.SchemaFilter<RequiredResponsePropertiesSchemaFilter>();
        options.OperationFilter<CamelCaseQueryParametersOperationFilter>();
        options.IncludeXmlComments(Path.Combine(AppContext.BaseDirectory, "OrderManagement.Api.xml"), includeControllerXmlComments: true);
    }
}
