using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Asp.Versioning;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Rewrite;
using OrderManagement.Api.Auth;
using OrderManagement.Api.Common;
using OrderManagement.Api.Common.Errors;
using OrderManagement.Api.Common.Http;
using OrderManagement.Api.Common.Validation;
using OrderManagement.Api.OpenApi;
using OrderManagement.Api.Services;
using OrderManagement.Api.Services.Interfaces;
using Scalar.AspNetCore;

namespace OrderManagement.Api;

/// <summary>Composition of the API's cross-cutting concerns, kept out of Program.cs.</summary>
internal static class ApiSetup
{
    public const string WebCorsPolicy = "Web";
    public const string WritesRateLimit = "writes";

    private const long MaxRequestBodyBytes = 1024 * 1024; // 1 MB: an order with 100 lines is ~10 KB

    public static WebApplicationBuilder AddApi(this WebApplicationBuilder builder)
    {
        var services = builder.Services;
        var config = builder.Configuration;

        builder.WebHost.ConfigureKestrel(kestrel =>
        {
            kestrel.AddServerHeader = false;
            kestrel.Limits.MaxRequestBodySize = MaxRequestBodyBytes;
        });

        services
            .AddControllers(mvc =>
            {
                mvc.Filters.Add<FluentValidationFilter>();
                // Validation is owned by FluentValidation (with friendly messages), not by nullability annotations.
                mvc.SuppressImplicitRequiredAttributeForNonNullableReferenceTypes = true;
            })
            .ConfigureApiBehaviorOptions(api => api.InvalidModelStateResponseFactory = ModelBindingProblems.Create)
            .AddJsonOptions(json => json.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
        services.ConfigureHttpJsonOptions(json => json.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

        services.AddValidatorsFromAssemblyContaining<Program>(ServiceLifetime.Singleton, includeInternalTypes: true);

        services.AddProblemDetails(problem =>
            problem.CustomizeProblemDetails = ctx => ProblemDetailsEnricher.Enrich(ctx.HttpContext, ctx.ProblemDetails));
        services.AddExceptionHandler<GlobalExceptionHandler>();

        services
            .AddApiVersioning(versioning =>
            {
                versioning.DefaultApiVersion = ApiVersions.Default;
                versioning.AssumeDefaultVersionWhenUnspecified = true;
                versioning.ReportApiVersions = true; // api-supported-versions response header
                versioning.ApiVersionReader = new UrlSegmentApiVersionReader();
            })
            .AddMvc()
            .AddApiExplorer(explorer =>
            {
                explorer.GroupNameFormat = "'v'VVV";
                explorer.SubstituteApiVersionInUrl = true;
            });

        services.AddSwaggerGen();
        services.ConfigureOptions<ConfigureSwaggerOptions>();

        // Application services: explicit registrations, one line per service, no assembly scanning magic.
        services.AddScoped<ICustomerService, CustomerService>();
        services.AddScoped<IOrderService, OrderService>();

        services.AddApiAuth(config, builder.Environment);
        services.AddApiCors(config);
        services.AddApiRateLimiting(config);

        return builder;
    }

    public static WebApplication UseApi(this WebApplication app)
    {
        // Order matters: correlation first so every later component (including error handling) can use it.
        app.UseMiddleware<CorrelationIdMiddleware>();
        app.UseExceptionHandler();
        app.UseStatusCodePages(); // empty 401/403/404/405 responses become ProblemDetails
        app.UseMiddleware<SecurityHeadersMiddleware>();

        if (!app.Environment.IsDevelopment() && !app.Environment.IsEnvironment("Testing"))
        {
            app.UseHsts();
        }

        app.UseHttpsRedirection();

        var documentationEnabled = app.Environment.IsDevelopment() || app.Environment.IsEnvironment("Testing");
        if (documentationEnabled)
        {
            // Before authorization: the secure-by-default fallback policy would otherwise also cover the docs.
            app.UseSwaggerDocuments();
        }

        // The brief's unversioned routes (/api/orders) are aliases of the current version (ApiVersions.Current).
        app.UseRewriter(new RewriteOptions().AddRewrite(@"^api/(?!v\d+(?:\.\d+)?/)(.*)$", $"api/{ApiVersions.CurrentUrlSegment}/$1", skipRemainingRules: true));
        app.UseRouting(); // explicit, so routing sees the rewritten path

        app.UseCors(WebCorsPolicy);
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseRateLimiter();

        app.MapControllers();

        if (documentationEnabled)
        {
            app.MapScalarReference();
        }

        return app;
    }

    private static void UseSwaggerDocuments(this WebApplication app)
    {
        app.UseSwagger();
        app.UseSwaggerUI(ui =>
        {
            foreach (var version in app.DescribeApiVersions())
            {
                ui.SwaggerEndpoint($"/swagger/{version.GroupName}/swagger.json", version.GroupName.ToUpperInvariant());
            }
        });
    }

    private static void MapScalarReference(this WebApplication app)
    {
        app.MapScalarApiReference(scalar =>
        {
            scalar.WithTitle("Amrod Order Management API");
            scalar.WithOpenApiRoutePattern("/swagger/{documentName}/swagger.json");
            foreach (var version in app.DescribeApiVersions())
            {
                scalar.AddDocument(version.GroupName);
            }
        }).AllowAnonymous();
    }

    private static void AddApiCors(this IServiceCollection services, IConfiguration config)
    {
        // Only origins listed in configuration may call the API from a browser. An empty list allows none.
        var origins = config.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];

        services.AddCors(cors => cors.AddPolicy(WebCorsPolicy, policy => policy
            .WithOrigins(origins)
            .WithMethods(HttpMethods.Get, HttpMethods.Post, HttpMethods.Put)
            .WithHeaders("Authorization", "Content-Type", "Accept", "If-None-Match", "If-Match",
                ApiHeaders.IdempotencyKey, ApiHeaders.CorrelationId)
            .WithExposedHeaders("ETag", "Location", "Idempotent-Replayed", ApiHeaders.CorrelationId, ApiHeaders.ApiSupportedVersions)
            .SetPreflightMaxAge(TimeSpan.FromMinutes(10))));
    }

    private static void AddApiRateLimiting(this IServiceCollection services, IConfiguration config)
    {
        var permitsPerMinute = config.GetValue("RateLimiting:WritesPerMinute", 120);

        services.AddRateLimiter(limiter =>
        {
            limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            // Per user (or per IP when anonymous): one noisy client can't starve everyone else's writes.
            limiter.AddPolicy(WritesRateLimit, http => RateLimitPartition.GetFixedWindowLimiter(
                http.User.FindFirst("oid")?.Value ?? http.Connection.RemoteIpAddress?.ToString() ?? "anonymous",
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = permitsPerMinute,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0,
                }));

            limiter.OnRejected = async (ctx, ct) =>
            {
                if (ctx.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                {
                    ctx.HttpContext.Response.Headers.RetryAfter = ((int)retryAfter.TotalSeconds).ToString(System.Globalization.CultureInfo.InvariantCulture);
                }

                var problem = new ProblemDetails { Status = StatusCodes.Status429TooManyRequests };
                await ctx.HttpContext.RequestServices.GetRequiredService<IProblemDetailsService>()
                    .WriteAsync(new ProblemDetailsContext { HttpContext = ctx.HttpContext, ProblemDetails = problem });
            };
        });
    }
}
