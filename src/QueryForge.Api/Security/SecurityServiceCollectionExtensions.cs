using System.Net;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Options;

namespace QueryForge.Api.Security;

public static class SecurityServiceCollectionExtensions
{
    public const string CorsOriginsSection = "Cors:AllowedOrigins";
    public const string KnownProxiesSection = "ForwardedHeaders:KnownProxies";

    /// <summary>
    /// API key authentication with a fail-closed fallback policy: every endpoint requires an authenticated key
    /// unless it explicitly allows anonymous access.
    /// </summary>
    public static IServiceCollection AddApiKeySecurity(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<SecurityOptions>()
            .Bind(configuration.GetSection(SecurityOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<SecurityOptions>, SecurityOptionsValidator>();
        services.AddSingleton<ApiKeyStore>();

        services.AddAuthentication(ApiKeyAuthenticationDefaults.Scheme)
            .AddScheme<AuthenticationSchemeOptions, ApiKeyAuthenticationHandler>(ApiKeyAuthenticationDefaults.Scheme, null);

        services.AddAuthorizationBuilder()
            .SetFallbackPolicy(new AuthorizationPolicyBuilder(ApiKeyAuthenticationDefaults.Scheme)
                .RequireAuthenticatedUser()
                .Build())
            .AddPolicy(AuthorizationPolicies.ReadApiDefinitions, policy => policy
                .AddAuthenticationSchemes(ApiKeyAuthenticationDefaults.Scheme)
                .RequireRole(ApiKeyRoles.Reader, ApiKeyRoles.Admin))
            .AddPolicy(AuthorizationPolicies.WriteApiDefinitions, policy => policy
                .AddAuthenticationSchemes(ApiKeyAuthenticationDefaults.Scheme)
                .RequireRole(ApiKeyRoles.Admin))
            .AddPolicy(AuthorizationPolicies.ViewHealthDetails, policy => policy
                .AddAuthenticationSchemes(ApiKeyAuthenticationDefaults.Scheme)
                .RequireRole(ApiKeyRoles.Admin));

        return services;
    }

    /// <summary>
    /// Registers a CORS policy only when origins are configured. Production is expected to be same-origin
    /// (frontend proxy), so no origins are configured by default.
    /// </summary>
    public static IServiceCollection AddConfiguredCors(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        var origins = GetCorsOrigins(configuration);
        foreach (var origin in origins)
        {
            if (origin.Contains('*'))
            {
                throw new InvalidOperationException($"{CorsOriginsSection} must not contain wildcards.");
            }

            if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri) ||
                (uri.Scheme != Uri.UriSchemeHttps && !(environment.IsDevelopment() && uri.Scheme == Uri.UriSchemeHttp)))
            {
                throw new InvalidOperationException(
                    $"{CorsOriginsSection} contains an invalid origin '{origin}'. Use absolute https origins (http is allowed only in Development).");
            }
        }

        if (origins.Length > 0)
        {
            services.AddCors(options => options.AddDefaultPolicy(policy => policy
                .WithOrigins(origins)
                .WithMethods("GET", "POST", "PUT", "DELETE")
                .WithHeaders("Content-Type", ApiKeyAuthenticationDefaults.HeaderName)
                .WithExposedHeaders("Location", "Retry-After")));
        }

        return services;
    }

    public static WebApplication UseConfiguredCors(this WebApplication app)
    {
        if (GetCorsOrigins(app.Configuration).Length > 0)
        {
            app.UseCors();
        }

        return app;
    }

    /// <summary>
    /// Trusts X-Forwarded-For/Proto only from explicitly configured proxy addresses. With no proxies configured
    /// the middleware is not added at all, because ForwardedHeadersMiddleware trusts every sender when its
    /// known-proxy and known-network lists are both empty.
    /// </summary>
    public static IServiceCollection AddTrustedForwardedHeaders(this IServiceCollection services, IConfiguration configuration)
    {
        var proxies = GetKnownProxies(configuration);

        services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            options.ForwardLimit = 1;
            options.KnownNetworks.Clear();
            options.KnownProxies.Clear();
            foreach (var proxy in proxies)
            {
                options.KnownProxies.Add(proxy);
            }
        });

        return services;
    }

    public static WebApplication UseTrustedForwardedHeaders(this WebApplication app)
    {
        if (GetKnownProxies(app.Configuration).Length > 0)
        {
            app.UseForwardedHeaders();
        }

        return app;
    }

    private static string[] GetCorsOrigins(IConfiguration configuration)
    {
        return (configuration.GetSection(CorsOriginsSection).Get<string[]>() ?? [])
            .Where(origin => !string.IsNullOrWhiteSpace(origin))
            .Select(origin => origin.Trim().TrimEnd('/'))
            .ToArray();
    }

    private static IPAddress[] GetKnownProxies(IConfiguration configuration)
    {
        return (configuration.GetSection(KnownProxiesSection).Get<string[]>() ?? [])
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => IPAddress.TryParse(value.Trim(), out var address)
                ? address
                : throw new InvalidOperationException($"{KnownProxiesSection} contains an invalid IP address '{value}'."))
            .ToArray();
    }
}
