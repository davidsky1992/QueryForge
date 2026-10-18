using System.Net;

namespace QueryForge.Infrastructure.Execution;

public sealed class UpstreamHandlerOptions
{
    public TimeSpan ConnectTimeout { get; init; } = TimeSpan.FromSeconds(5);

    public int MaxConnectionsPerServer { get; init; } = 10;

    // Pooled connections only ever point at addresses checked when they were opened; the lifetime bounds how long
    // a connection outlives the DNS answer it was opened from.
    public TimeSpan PooledConnectionLifetime { get; init; } = TimeSpan.FromMinutes(2);

    public int MaxResponseHeadersLengthKilobytes { get; init; } = 32;
}

/// <summary>
/// Builds the primary handler for upstream calls. Every connection goes through <see cref="GuardedConnector"/>, and
/// every handler feature that could reach a different destination or attach ambient credentials is switched off.
/// </summary>
public static class UpstreamHandlerFactory
{
    public static SocketsHttpHandler CreatePrimaryHandler(GuardedConnector connector, UpstreamHandlerOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(connector);
        options ??= new UpstreamHandlerOptions();

        return new SocketsHttpHandler
        {
            // With a proxy, ConnectCallback would only see the proxy's address, never the real destination.
            UseProxy = false,
            Proxy = null,

            // Redirects are returned to the caller, never followed to an unchecked location.
            AllowAutoRedirect = false,

            // No ambient state may leak between callers or to upstreams.
            UseCookies = false,
            Credentials = null,
            DefaultProxyCredentials = null,
            PreAuthenticate = false,

            // Bodies are returned as sent; decompression would let a small response expand without bound.
            AutomaticDecompression = DecompressionMethods.None,

            ConnectTimeout = options.ConnectTimeout,
            MaxConnectionsPerServer = options.MaxConnectionsPerServer,
            PooledConnectionLifetime = options.PooledConnectionLifetime,
            MaxResponseHeadersLength = options.MaxResponseHeadersLengthKilobytes,

            ConnectCallback = async (context, cancellationToken) =>
                await connector.ConnectAsync(context.DnsEndPoint.Host, context.DnsEndPoint.Port, cancellationToken)
        };
    }
}

/// <summary>
/// Pins upstream requests to HTTP/1.1. HTTP/3 runs over QUIC and does not use <c>ConnectCallback</c>, so it would
/// bypass the destination checks; pinning here means no request can opt into it, whatever version it asks for.
/// </summary>
public sealed class UpstreamHttpVersionHandler : DelegatingHandler
{
    protected override HttpResponseMessage Send(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Pin(request);
        return base.Send(request, cancellationToken);
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Pin(request);
        return base.SendAsync(request, cancellationToken);
    }

    private static void Pin(HttpRequestMessage request)
    {
        request.Version = HttpVersion.Version11;
        request.VersionPolicy = HttpVersionPolicy.RequestVersionOrLower;
    }
}
