using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Asp.Versioning;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Rewrite;
using OrderManagement.Api.Auth;
using OrderManagement.Api.Common;
using OrderManagement.Api.GraphQL;
using OrderManagement.Api.Common.Errors;
using OrderManagement.Api.Common.Http;
using OrderManagement.Api.Common.Validation;
using OrderManagement.Api.OpenApi;
using OrderManagement.Api.Services;
using OrderManagement.Api.Services.Interfaces;
using OrderManagement.Infrastructure.Messaging;
using Scalar.AspNetCore;
using Serilog;
using Serilog.Events;

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
        services.AddScoped<IOrderEventPublisher, OutboxOrderEventPublisher>();
        services.AddScoped<IReportService, ReportService>();
        services.AddHttpContextAccessor();

        // Publisher only: events go through the transactional outbox. The API hosts no consumers.
        services.AddMessaging(config, publishThroughOutbox: true);

        services.AddApiAuth(config, builder.Environment);
        services.AddApiCors(config);
        services.AddApiRateLimiting(config);
        services.AddOrdersGraphQL(builder.Environment);

        return builder;
    }

    public static WebApplication UseApi(this WebApplication app)
    {
        // Order matters: correlation first so every later component (including error handling) can use it.
        app.UseMiddleware<CorrelationIdMiddleware>();

        // One structured line per request: method, path, status, duration, correlation ID and caller. Health probes are
        // logged at Verbose (i.e. not at all by default) so they don't drown real traffic.
        app.UseSerilogRequestLogging(logging =>
        {
            logging.GetLevel = (http, _, exception) =>
                exception is not null || http.Response.StatusCode >= 500 ? LogEventLevel.Error
                : IsHealthProbe(http.Request.Path) ? LogEventLevel.Verbose
                : LogEventLevel.Information;
            logging.EnrichDiagnosticContext = (diagnostics, http) =>
            {
                if (CorrelationIdMiddleware.Get(http) is { } correlationId)
                {
                    diagnostics.Set("CorrelationId", correlationId);
                }

                if (http.User.FindFirst("oid")?.Value is { } userId)
                {
                    diagnostics.Set("UserId", userId); // Entra object ID: a pseudonymous identifier, not a name or email
                }
            };
        });
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
        app.MapOrdersGraphQL();

        if (documentationEnabled)
        {
            app.MapScalarReference();
        }

        return app;
    }

    private static bool IsHealthProbe(PathString path) =>
        path.StartsWithSegments(Extensions.LivenessPath) || path.StartsWithSegments(Extensions.ReadinessPath);

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
        var requestsPerMinute = config.GetValue("RateLimiting:RequestsPerMinute", 600);
        var writesPerMinute = config.GetValue("RateLimiting:WritesPerMinute", 120);

        services.AddRateLimiter(limiter =>
        {
            limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            // Two layers, both per user (or per IP when anonymous), so one noisy client can't starve everyone else:
            // - every request (REST, GraphQL, reports, the dev token endpoint) counts against a generous global limit;
            // - writes also count against a stricter one, because each write is a transaction and a broker message.
            // Health probes opt out (DisableRateLimiting), so an orchestrator never sees a 429.
            limiter.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(http =>
                PerMinute(PartitionKey(http), requestsPerMinute));

            limiter.AddPolicy(WritesRateLimit, http => PerMinute(PartitionKey(http), writesPerMinute));

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

    // Runs after authentication, so a signed-in caller is keyed on the token's oid and keeps their own budget even when
    // many users share one IP (an office, or a proxy). Behind a load balancer the IP is the proxy's unless forwarded
    // headers are configured; production would also limit at the gateway (APIM / Front Door).
    private static string PartitionKey(HttpContext http) =>
        http.User.FindFirst("oid")?.Value is { } userId
            ? $"user:{userId}"
            : $"ip:{http.Connection.RemoteIpAddress?.ToString() ?? "unknown"}";

    private static RateLimitPartition<string> PerMinute(string key, int permits) =>
        RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = permits,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
        });

    // Caching, deliberately not enabled (see README "Known limitations"). The data that would benefit is reference data
    // that rarely changes, and it is already cached in the browser (ReferenceDataController) and by TanStack Query.
    // Orders are not cached server-side: the worker changes their status, so a cache would need invalidation, and
    // GET /orders/{id} already answers 304 from its ETag. To add a shared cache across API instances:
    //
    //   1. Directory.Packages.props:  <PackageVersion Include="Microsoft.Extensions.Caching.Hybrid" Version="9.x" />
    //                                 <PackageVersion Include="Microsoft.Extensions.Caching.StackExchangeRedis" Version="8.x" />
    //      AppHost:                   var redis = builder.AddRedis("cache"); api.WithReference(redis);
    //
    //   2. In AddApi:
    //      services.AddStackExchangeRedisCache(redis =>
    //      {
    //          redis.Configuration = config.GetConnectionString("cache");
    //          redis.InstanceName = "orders:";
    //      });
    //      services.AddHybridCache(cache => cache.DefaultEntryOptions = new HybridCacheEntryOptions
    //      {
    //          Expiration = TimeSpan.FromMinutes(10),         // Redis (shared by every instance)
    //          LocalCacheExpiration = TimeSpan.FromMinutes(1), // in-process copy on each instance
    //      });
    //
    //   3. Where the data is read, e.g. a reference-data or product lookup service:
    //      public Task<IReadOnlyList<CountryResponse>> GetCountriesAsync(CancellationToken ct) =>
    //          cache.GetOrCreateAsync("reference:countries", async token => await LoadCountriesAsync(token),
    //              tags: ["reference"], cancellationToken: ct).AsTask();
    //
    //      and on change: await cache.RemoveByTagAsync("reference", ct);
    //
    //   Never cache per-user or per-role responses under a shared key: include the caller in the key, or don't cache.
}
