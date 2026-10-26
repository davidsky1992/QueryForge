namespace QueryForge.Api.RateLimiting;

/// <summary>
/// Fixed-window limits bound from the "RateLimiting" configuration section. Counters are kept in memory per API
/// instance; they are not shared between instances.
/// </summary>
public sealed class RateLimitingOptions
{
    public const string SectionName = "RateLimiting";

    /// <summary>Authenticated GET requests, per API key.</summary>
    public RateLimitWindow Reads { get; set; } = new() { PermitLimit = 120 };

    /// <summary>Authenticated POST/PUT/DELETE requests, per API key.</summary>
    public RateLimitWindow Writes { get; set; } = new() { PermitLimit = 30 };

    /// <summary>Unauthenticated requests to protected endpoints, per client IP (slows down key guessing).</summary>
    public RateLimitWindow Anonymous { get; set; } = new() { PermitLimit = 30 };

    /// <summary>Readiness and detailed health requests, per client IP.</summary>
    public RateLimitWindow Health { get; set; } = new() { PermitLimit = 60 };

    /// <summary>Operation executions, per API key. Each one makes an outbound call, so it is kept stricter.</summary>
    public RateLimitWindow Execute { get; set; } = new() { PermitLimit = 60 };

    public IEnumerable<(string Name, RateLimitWindow Window)> All()
    {
        yield return (nameof(Reads), Reads);
        yield return (nameof(Writes), Writes);
        yield return (nameof(Anonymous), Anonymous);
        yield return (nameof(Health), Health);
        yield return (nameof(Execute), Execute);
    }
}

public sealed class RateLimitWindow
{
    public int PermitLimit { get; set; }
    public int WindowSeconds { get; set; } = 60;
}
