using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.ServiceDiscovery;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using OrderManagement.Contracts;
using Serilog;

namespace Microsoft.Extensions.Hosting;

// Adds common Aspire services: service discovery, resilience, health checks, and OpenTelemetry.
// This project should be referenced by each service project in your solution.
// To learn more about using this project, see https://aka.ms/aspire/service-defaults
public static class Extensions
{
    /// <summary>Liveness: is the process up and responsive? No dependencies, so an outage elsewhere never restarts it.</summary>
    public const string LivenessPath = "/healthz";

    /// <summary>Readiness: can it serve traffic right now? Checks the database and the message broker.</summary>
    public const string ReadinessPath = "/readiness";


    public static TBuilder AddServiceDefaults<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        builder.AddStructuredLogging();
        builder.ConfigureOpenTelemetry();

        builder.AddDefaultHealthChecks();

        builder.Services.AddServiceDiscovery();

        builder.Services.ConfigureHttpClientDefaults(http =>
        {
            // Turn on resilience by default
            http.AddStandardResilienceHandler();

            // Turn on service discovery by default
            http.AddServiceDiscovery();
        });

        // Uncomment the following to restrict the allowed schemes for service discovery.
        // builder.Services.Configure<ServiceDiscoveryOptions>(options =>
        // {
        //     options.AllowedSchemes = ["https"];
        // });

        return builder;
    }

    private const string DevelopmentConsoleTemplate =
        "[{Timestamp:HH:mm:ss} {Level:u3}] {SourceContext}{NewLine}      {Message:lj} {Properties:j}{NewLine}{Exception}";

    /// <summary>
    /// Serilog renders the console: readable lines in Development, one compact JSON object per line everywhere else (ready
    /// for any log shipper). Levels and any extra sinks come from the <c>Serilog</c> configuration section. Every event is
    /// also forwarded to the OpenTelemetry provider, so the Aspire dashboard and any OTLP backend keep receiving logs.
    /// <c>FromLogContext</c> turns logger scopes (correlation ID, message ID) into properties on every line.
    /// Don't also configure a Console sink under <c>Serilog:WriteTo</c>: it would duplicate every line. Use config for
    /// additional sinks (Seq, files, …) only.
    /// </summary>
    public static TBuilder AddStructuredLogging<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        builder.Logging.ClearProviders(); // no duplicate console output; OpenTelemetry's provider is added back below

        var development = builder.Environment.IsDevelopment();
        var testing = builder.Environment.IsEnvironment("Testing");

        builder.Services.AddSerilog(
            (services, logger) =>
            {
                logger
                    .ReadFrom.Configuration(builder.Configuration)
                    .ReadFrom.Services(services)
                    .Enrich.FromLogContext()
                    .Enrich.WithProperty("Application", builder.Environment.ApplicationName);

                if (development)
                {
                    logger.WriteTo.Console(outputTemplate: DevelopmentConsoleTemplate, formatProvider: System.Globalization.CultureInfo.InvariantCulture);
                }
                else if (!testing) // keep test output readable
                {
                    logger.WriteTo.Console(new Serilog.Formatting.Compact.RenderedCompactJsonFormatter());
                }
            },
            writeToProviders: true);

        return builder;
    }

    public static TBuilder ConfigureOpenTelemetry<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        builder.Logging.AddOpenTelemetry(logging =>
        {
            logging.IncludeFormattedMessage = true;
            logging.IncludeScopes = true;
        });

        builder.Services.AddOpenTelemetry()
            .WithMetrics(metrics =>
            {
                metrics.AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddRuntimeInstrumentation()
                    .AddMeter("MassTransit") // consume/publish counts and durations
                    .AddMeter("OrderManagement.*"); // business metrics (orders created, allocated, fulfilled)
            })
            .WithTracing(tracing =>
            {
                tracing.AddSource(builder.Environment.ApplicationName)
                    .AddSource("MassTransit") // one trace across API → outbox → RabbitMQ → worker
                    .AddAspNetCoreInstrumentation(tracing =>
                        // Exclude health check requests from tracing
                        tracing.Filter = context =>
                            !context.Request.Path.StartsWithSegments(LivenessPath)
                            && !context.Request.Path.StartsWithSegments(ReadinessPath)
                    )
                    // Uncomment the following line to enable gRPC instrumentation (requires the OpenTelemetry.Instrumentation.GrpcNetClient package)
                    //.AddGrpcClientInstrumentation()
                    .AddHttpClientInstrumentation();
            });

        builder.AddOpenTelemetryExporters();

        return builder;
    }

    private static TBuilder AddOpenTelemetryExporters<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        var useOtlpExporter = !string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]);

        if (useOtlpExporter)
        {
            builder.Services.AddOpenTelemetry().UseOtlpExporter();
        }

        // Uncomment the following lines to enable the Azure Monitor exporter (requires the Azure.Monitor.OpenTelemetry.AspNetCore package)
        //if (!string.IsNullOrEmpty(builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"]))
        //{
        //    builder.Services.AddOpenTelemetry()
        //       .UseAzureMonitor();
        //}

        return builder;
    }

    public static TBuilder AddDefaultHealthChecks<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        builder.Services.AddHealthChecks()
            // Add a default liveness check to ensure app is responsive
            .AddCheck("self", () => HealthCheckResult.Healthy(), [HealthTags.Live]);

        return builder;
    }

    /// <summary>
    /// Maps <c>/healthz</c> and <c>/readiness</c> in every environment, because orchestrators probe production too.
    /// Exception details never appear in the response; they stay in the logs.
    /// </summary>
    public static WebApplication MapDefaultEndpoints(this WebApplication app)
    {
        // Public probes answer with the overall status only. The per-check breakdown (dependency names and timings) is
        // reconnaissance material, so it is shown in Development and Testing, and to anyone reading the logs.
        var detailed = app.Environment.IsDevelopment() || app.Environment.IsEnvironment("Testing");
        var writer = detailed ? (Func<HttpContext, HealthReport, Task>)WriteDetailedHealthResponse : WriteStatusOnlyResponse;

        // AllowAnonymous: probes (Aspire, Docker, Kubernetes) never carry a token, and the API's
        // secure-by-default fallback policy would otherwise answer 401.
        app.MapHealthChecks(LivenessPath, new HealthCheckOptions
        {
            Predicate = r => r.Tags.Contains(HealthTags.Live),
            ResponseWriter = writer,
        }).AllowAnonymous().DisableRateLimiting();

        app.MapHealthChecks(ReadinessPath, new HealthCheckOptions
        {
            Predicate = r => r.Tags.Contains(HealthTags.Ready),
            ResponseWriter = writer,
        }).AllowAnonymous().DisableRateLimiting();

        return app;
    }

    private static Task WriteStatusOnlyResponse(HttpContext context, HealthReport report)
    {
        context.Response.ContentType = "application/json; charset=utf-8";
        context.Response.Headers.CacheControl = "no-store";
        return JsonSerializer.SerializeAsync(context.Response.Body, new { status = report.Status.ToString() },
            cancellationToken: context.RequestAborted);
    }

    private static Task WriteDetailedHealthResponse(HttpContext context, HealthReport report)
    {
        context.Response.ContentType = "application/json; charset=utf-8";
        context.Response.Headers.CacheControl = "no-store";

        return JsonSerializer.SerializeAsync(context.Response.Body, new
        {
            status = report.Status.ToString(),
            durationMs = Math.Round(report.TotalDuration.TotalMilliseconds, 1),
            checks = report.Entries.Select(e => new
            {
                name = e.Key,
                status = e.Value.Status.ToString(),
                durationMs = Math.Round(e.Value.Duration.TotalMilliseconds, 1),
            }),
        }, cancellationToken: context.RequestAborted);
    }
}
