using System.Text.Json;
using QueryForge.Application.Common;

namespace QueryForge.Application.Execution;

/// <summary>Caller input: values for declared parameters only. Nothing else about the upstream call can be set.</summary>
public sealed record ExecuteOperationRequest(IReadOnlyDictionary<string, JsonElement>? Parameters);

/// <summary>
/// Fixed response envelope. The upstream status is reported as data; upstream headers are never passed through.
/// <see cref="Body"/> is the parsed JSON, a JSON string for text responses, or null for an empty body.
/// </summary>
public sealed record ExecutionResultDto(int UpstreamStatus, string? ContentType, JsonElement? Body, long DurationMs);

/// <summary>A fully built upstream request. The URL has already passed the scheme, port and path checks.</summary>
public sealed record UpstreamRequest(Uri Url, TimeSpan Timeout, long MaxResponseBytes);

/// <summary>Sends upstream requests through the hardened HTTP pipeline (destination checks at connection time).</summary>
public interface IUpstreamClient
{
    Task<Result<UpstreamResponse>> SendAsync(UpstreamRequest request, CancellationToken cancellationToken);
}

/// <summary>Execution settings that apply to every operation.</summary>
public sealed class ExecutionOptions
{
    public const string SectionName = "Execution";

    /// <summary>Exposes the execution endpoint. Off until the security review approves enabling it.</summary>
    public bool Enabled { get; set; }

    /// <summary>Allows plain-HTTP upstreams. Intended for local development only.</summary>
    public bool AllowHttp { get; set; }

    /// <summary>Upstream ports that may be contacted.</summary>
    public List<int> AllowedPorts { get; set; } = [80, 443];

    /// <summary>Upstream calls in flight at once, per API instance. Further requests get 503 rather than queueing.</summary>
    public int MaxConcurrentExecutions { get; set; } = 20;

    /// <summary>Largest accepted execution request body. A constant so the server limit and the reader agree.</summary>
    public const int MaxRequestBodyBytes = 16 * 1024;
}

public static class ExecutionErrors
{
    public const string OperationDisabled = "operation_disabled";
    public const string UpstreamAuthNotSupported = "upstream_auth_not_supported";
    public const string RequestTooLarge = "request_too_large";
}
