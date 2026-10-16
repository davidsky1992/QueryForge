using Microsoft.Extensions.Diagnostics.HealthChecks;
using StackExchange.Redis;

namespace QueryForge.Api.Health;

public sealed class RedisHealthCheck : IHealthCheck
{
    private readonly IConnectionMultiplexer? _multiplexer;

    // The multiplexer is only registered when a Redis connection string is configured.
    public RedisHealthCheck(IConnectionMultiplexer? multiplexer = null)
    {
        _multiplexer = multiplexer;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        if (_multiplexer is null)
        {
            return HealthCheckResult.Healthy("Redis is not configured; the in-memory cache is in use.");
        }

        try
        {
            await _multiplexer.GetDatabase().PingAsync().WaitAsync(cancellationToken);
            return HealthCheckResult.Healthy("Redis is reachable.");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return new HealthCheckResult(context.Registration.FailureStatus, "Redis is unreachable.", exception);
        }
    }
}
