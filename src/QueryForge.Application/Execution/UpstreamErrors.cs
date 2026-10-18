namespace QueryForge.Application.Execution;

/// <summary>Error codes for upstream execution failures, returned to callers in ProblemDetails <c>code</c>.</summary>
public static class UpstreamErrors
{
    public const string DestinationBlocked = "upstream_destination_blocked";
    public const string Unreachable = "upstream_unreachable";
    public const string Timeout = "upstream_timeout";
    public const string ResponseTooLarge = "upstream_response_too_large";
    public const string UnsupportedContentType = "upstream_unsupported_content_type";
    public const string InvalidResponse = "upstream_invalid_response";
    public const string CapacityExceeded = "execution_capacity_exceeded";
}
