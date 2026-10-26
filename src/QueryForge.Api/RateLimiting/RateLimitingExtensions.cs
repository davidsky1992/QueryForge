using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using QueryForge.Api.Http;

namespace QueryForge.Api.RateLimiting;

public static class RateLimitPolicies
{
    public const string Reads = "api-reads";
    public const string Writes = "api-writes";
    public const string Health = "health";
    public const string Execute = "api-execute";
}

public static class RateLimitingExtensions
{
    public static IServiceCollection AddApiRateLimiting(this IServiceCollection services, IConfiguration configuration)
    {
        var options = configuration.GetSection(RateLimitingOptions.SectionName).Get<RateLimitingOptions>() ?? new RateLimitingOptions();
        foreach (var (name, window) in options.All())
        {
            if (window.PermitLimit < 1 || window.WindowSeconds < 1)
            {
                throw new InvalidOperationException(
                    $"{RateLimitingOptions.SectionName}:{name} requires PermitLimit and WindowSeconds of at least 1.");
            }
        }

        services.AddRateLimiter(limiter =>
        {
            limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            limiter.OnRejected = async (context, cancellationToken) =>
            {
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                {
                    context.HttpContext.Response.Headers.RetryAfter =
                        Math.Ceiling(retryAfter.TotalSeconds).ToString(CultureInfo.InvariantCulture);
                }

                await ProblemResponses.WriteAsync(
                    context.HttpContext,
                    StatusCodes.Status429TooManyRequests,
                    "Too many requests",
                    "The request rate limit was exceeded. Retry after the time given in the Retry-After header.");
            };

            limiter.AddPolicy(RateLimitPolicies.Reads, context => PerClient(context, RateLimitPolicies.Reads, options.Reads, options.Anonymous));
            limiter.AddPolicy(RateLimitPolicies.Writes, context => PerClient(context, RateLimitPolicies.Writes, options.Writes, options.Anonymous));
            limiter.AddPolicy(RateLimitPolicies.Execute, context => PerClient(context, RateLimitPolicies.Execute, options.Execute, options.Anonymous));
            limiter.AddPolicy(RateLimitPolicies.Health,context => FixedWindow($"{RateLimitPolicies.Health}:ip:{ClientIp(context)}", options.Health));
        });

        return services;
    }

    // Requires UseAuthentication before UseRateLimiter so the API key identity is available.
    private static RateLimitPartition<string> PerClient(HttpContext context, string policy, RateLimitWindow authenticated, RateLimitWindow anonymous)
    {
        var keyName = context.User.Identity?.IsAuthenticated == true ? context.User.Identity.Name : null;

        return keyName is not null
            ? FixedWindow($"{policy}:key:{keyName}", authenticated)
            : FixedWindow($"{policy}:anonymous:{ClientIp(context)}", anonymous);
    }

    private static RateLimitPartition<string> FixedWindow(string partitionKey, RateLimitWindow window)
    {
        return RateLimitPartition.GetFixedWindowLimiter(partitionKey, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = window.PermitLimit,
            Window = TimeSpan.FromSeconds(window.WindowSeconds),
            QueueLimit = 0,
            AutoReplenishment = true
        });
    }

    // The remote address is the real client only when forwarded headers from a trusted proxy have been applied.
    private static string ClientIp(HttpContext context)
    {
        return context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    }
}
