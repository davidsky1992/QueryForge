using QueryForge.Application.Execution;

namespace QueryForge.Api.Http;

/// <summary>
/// HTTP status for each upstream error code: 502 when the upstream (or the route to it) failed or answered with
/// something unusable, 504 when it did not answer in time, 503 when QueryForge itself has no capacity.
/// </summary>
public static class UpstreamErrorStatusCodes
{
    public static int For(string errorCode) => errorCode switch
    {
        UpstreamErrors.DestinationBlocked or
        UpstreamErrors.Unreachable or
        UpstreamErrors.ResponseTooLarge or
        UpstreamErrors.UnsupportedContentType or
        UpstreamErrors.InvalidResponse => StatusCodes.Status502BadGateway,
        UpstreamErrors.Timeout => StatusCodes.Status504GatewayTimeout,
        UpstreamErrors.CapacityExceeded => StatusCodes.Status503ServiceUnavailable,
        _ => throw new ArgumentOutOfRangeException(nameof(errorCode), errorCode, "Not an upstream error code.")
    };
}
