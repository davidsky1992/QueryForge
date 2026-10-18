using System.Text.Json;

namespace QueryForge.Application.Execution;

/// <summary>
/// An upstream response that was read completely within the limits. Upstream headers are deliberately not kept; only
/// the media type is reported. At most one of <see cref="Json"/> and <see cref="Text"/> is set, and neither is set
/// for an empty body.
/// </summary>
public sealed record UpstreamResponse(
    int StatusCode,
    string? ContentType,
    JsonElement? Json,
    string? Text,
    TimeSpan Duration);
