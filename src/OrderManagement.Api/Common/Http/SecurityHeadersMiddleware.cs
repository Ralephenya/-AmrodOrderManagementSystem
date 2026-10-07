using OrderManagement.Api.GraphQL;

namespace OrderManagement.Api.Common.Http;

/// <summary>
/// Secure-by-default response headers. The API only ever returns JSON, so the content security policy denies
/// everything. The interactive tools need scripts and styles, so they are exempt: Swagger UI, Scalar, and the Nitro
/// GraphQL IDE, which is served by GET /graphql only where developer features are on (Development and Testing).
/// Everywhere else, and for every GraphQL POST, the strict policy applies.
/// </summary>
internal sealed class SecurityHeadersMiddleware(RequestDelegate next, IHostEnvironment environment)
{
    private readonly bool _graphQLToolEnabled = GraphQLSetup.DeveloperFeaturesEnabled(environment);

    public Task InvokeAsync(HttpContext context)
    {
        var headers = context.Response.Headers;
        headers.XContentTypeOptions = "nosniff";
        headers.XFrameOptions = "DENY";
        headers["Referrer-Policy"] = "no-referrer";
        headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
        headers["Cross-Origin-Opener-Policy"] = "same-origin";

        if (!IsInteractiveTool(context.Request, _graphQLToolEnabled))
        {
            headers.ContentSecurityPolicy = "default-src 'none'; frame-ancestors 'none'";
        }

        return next(context);
    }

    private static bool IsInteractiveTool(HttpRequest request, bool graphQLToolEnabled) =>
        request.Path.StartsWithSegments("/swagger")
        || request.Path.StartsWithSegments("/scalar")
        || (graphQLToolEnabled && HttpMethods.IsGet(request.Method) && request.Path.StartsWithSegments(GraphQLSetup.Path));
}
