using Microsoft.Net.Http.Headers;

namespace QueryForge.Api.Security;

/// <summary>
/// Adds security headers suited to a JSON API. Swagger UI in Development is exempt from the restrictive
/// Content-Security-Policy because it serves HTML, scripts, and styles.
/// </summary>
public sealed class SecurityHeadersMiddleware
{
    private const string ApiContentSecurityPolicy = "default-src 'none'; frame-ancestors 'none'";

    private readonly RequestDelegate _next;
    private readonly IWebHostEnvironment _environment;

    public SecurityHeadersMiddleware(RequestDelegate next, IWebHostEnvironment environment)
    {
        _next = next;
        _environment = environment;
    }

    public Task InvokeAsync(HttpContext context)
    {
        context.Response.OnStarting(() =>
        {
            var headers = context.Response.Headers;
            headers.XContentTypeOptions = "nosniff";
            headers.XFrameOptions = "DENY";
            headers["Referrer-Policy"] = "no-referrer";

            var isSwagger = _environment.IsDevelopment() && context.Request.Path.StartsWithSegments("/swagger");
            if (!isSwagger)
            {
                headers.ContentSecurityPolicy = ApiContentSecurityPolicy;

                // Responses may contain management data; keep them out of shared and browser caches.
                if (!headers.ContainsKey(HeaderNames.CacheControl))
                {
                    headers.CacheControl = "no-store";
                }
            }

            return Task.CompletedTask;
        });

        return _next(context);
    }
}
