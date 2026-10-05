namespace OrderManagement.Api.Common.Http;

/// <summary>
/// Secure-by-default response headers. The API only ever returns JSON, so the content security policy denies
/// everything. The interactive docs (Swagger UI, Scalar) need scripts and styles, so they are exempt.
/// </summary>
internal sealed class SecurityHeadersMiddleware(RequestDelegate next)
{
    public Task InvokeAsync(HttpContext context)
    {
        var headers = context.Response.Headers;
        headers.XContentTypeOptions = "nosniff";
        headers.XFrameOptions = "DENY";
        headers["Referrer-Policy"] = "no-referrer";
        headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
        headers["Cross-Origin-Opener-Policy"] = "same-origin";

        if (!IsDocumentation(context.Request.Path))
        {
            headers.ContentSecurityPolicy = "default-src 'none'; frame-ancestors 'none'";
        }

        return next(context);
    }

    private static bool IsDocumentation(PathString path) =>
        path.StartsWithSegments("/swagger") || path.StartsWithSegments("/scalar");
}
