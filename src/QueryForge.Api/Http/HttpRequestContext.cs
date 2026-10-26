using System.Diagnostics;
using QueryForge.Application.Auditing;

namespace QueryForge.Api.Http;

/// <summary>Actor and trace id of the current HTTP request, for audit events.</summary>
internal sealed class HttpRequestContext : IRequestContext
{
    private const string UnknownActor = "unknown";

    private readonly IHttpContextAccessor _httpContextAccessor;

    public HttpRequestContext(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    // The authentication handler sets the name claim to the configured API key name, never the key itself.
    public string Actor => _httpContextAccessor.HttpContext?.User.Identity?.Name is { Length: > 0 } name ? name : UnknownActor;

    public string? TraceId => Activity.Current?.TraceId.ToString() ?? _httpContextAccessor.HttpContext?.TraceIdentifier;
}
