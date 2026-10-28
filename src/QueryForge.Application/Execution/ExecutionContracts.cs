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

/// <summary>
/// A fully built upstream request. The URL has already passed the scheme, port and path checks. The credential, if
/// any, is sent only with this request, which goes only to the definition's base URL host.
/// </summary>
public sealed record UpstreamRequest(Uri Url, TimeSpan Timeout, long MaxResponseBytes, UpstreamCredential? Credential = null);

/// <summary>
/// An upstream API key and the header it is sent in. Deliberately not a record: a generated ToString would print the
/// value wherever the request is logged or shown in a test failure, so ToString redacts it.
/// </summary>
public sealed class UpstreamCredential
{
    public UpstreamCredential(string headerName, string value)
    {
        HeaderName = headerName;
        Value = value;
    }

    public string HeaderName { get; }

    public string Value { get; }

    public override string ToString() => $"UpstreamCredential {{ HeaderName = {HeaderName}, Value = *** }}";
}

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
    public const string UpstreamAuthNotConfigured = "upstream_auth_not_configured";
    public const string UpstreamSecretUnavailable = "upstream_secret_unavailable";
    public const string RequestTooLarge = "request_too_large";
}
