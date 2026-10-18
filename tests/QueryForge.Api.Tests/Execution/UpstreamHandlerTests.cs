using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Text;
using QueryForge.Infrastructure.Execution;

namespace QueryForge.Api.Tests.Execution;

/// <summary>
/// Real-socket tests of the upstream handler against loopback servers. No test reaches the internet: blocked
/// destinations must never produce a TCP connection, and "public" upstreams are simulated by a connector that opens
/// the socket for an already-checked public endpoint to a loopback server instead.
/// </summary>
public sealed class UpstreamHandlerTests
{
    [Fact]
    public void PrimaryHandler_DisablesEverythingThatCouldBypassChecksOrLeakState()
    {
        using var handler = UpstreamHandlerFactory.CreatePrimaryHandler(DefaultConnector());

        Assert.False(handler.UseProxy);
        Assert.Null(handler.Proxy);
        Assert.False(handler.AllowAutoRedirect);
        Assert.False(handler.UseCookies);
        Assert.Null(handler.Credentials);
        Assert.Null(handler.DefaultProxyCredentials);
        Assert.False(handler.PreAuthenticate);
        Assert.Equal(DecompressionMethods.None, handler.AutomaticDecompression);
        Assert.NotNull(handler.ConnectCallback);
        Assert.Null(handler.SslOptions.RemoteCertificateValidationCallback); // default certificate validation
    }

    [Fact]
    public void PrimaryHandler_AppliesLimits()
    {
        using var defaults = UpstreamHandlerFactory.CreatePrimaryHandler(DefaultConnector());
        using var custom = UpstreamHandlerFactory.CreatePrimaryHandler(DefaultConnector(), new UpstreamHandlerOptions
        {
            ConnectTimeout = TimeSpan.FromSeconds(2),
            MaxConnectionsPerServer = 3,
            PooledConnectionLifetime = TimeSpan.FromSeconds(30),
            MaxResponseHeadersLengthKilobytes = 8
        });

        Assert.Equal(TimeSpan.FromSeconds(5), defaults.ConnectTimeout);
        Assert.Equal(10, defaults.MaxConnectionsPerServer);
        Assert.Equal(TimeSpan.FromMinutes(2), defaults.PooledConnectionLifetime);
        Assert.Equal(32, defaults.MaxResponseHeadersLength);
        Assert.Equal(TimeSpan.FromSeconds(2), custom.ConnectTimeout);
        Assert.Equal(3, custom.MaxConnectionsPerServer);
        Assert.Equal(TimeSpan.FromSeconds(30), custom.PooledConnectionLifetime);
        Assert.Equal(8, custom.MaxResponseHeadersLength);
    }

    [Fact]
    public async Task PositiveControl_RealSocketConnector_ReachesTheLoopbackServer()
    {
        // Proves the server counts connections, so the zero counts in the blocking tests mean "never connected".
        await using var server = LoopbackServer.StartIPv4();

        await using (await new SystemSocketConnector().ConnectAsync(server.EndPoint, CancellationToken.None))
        {
            await WaitForAsync(() => server.AcceptedConnections == 1);
        }

        Assert.Equal(1, server.AcceptedConnections);
    }

    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("127.1")]
    [InlineData("2130706433")]
    [InlineData("0x7f.0.0.1")]
    [InlineData("0x7f000001")]
    [InlineData("[::ffff:127.0.0.1]")]
    [InlineData("localhost")]
    public async Task LoopbackIPv4Destinations_AreBlockedBeforeAnyConnection(string host)
    {
        await using var server = LoopbackServer.StartIPv4();
        // The server's port is allowed, so only the address rules can stop the request.
        using var client = CreateClient(RealConnector(server.Port));

        var exception = await Assert.ThrowsAsync<HttpRequestException>(() => client.GetAsync($"http://{host}:{server.Port}/"));

        Assert.Equal(UpstreamConnectionRejection.BlockedAddress, FindRejection(exception).Reason);
        Assert.Equal(0, server.AcceptedConnections);
    }

    [Theory]
    [InlineData("[::1]")]
    [InlineData("[0:0:0:0:0:0:0:1]")]
    [InlineData("localhost")]
    public async Task LoopbackIPv6Destinations_AreBlockedBeforeAnyConnection(string host)
    {
        await using var server = LoopbackServer.StartIPv6();
        using var client = CreateClient(RealConnector(server.Port));

        var exception = await Assert.ThrowsAsync<HttpRequestException>(() => client.GetAsync($"http://{host}:{server.Port}/"));

        Assert.Equal(UpstreamConnectionRejection.BlockedAddress, FindRejection(exception).Reason);
        Assert.Equal(0, server.AcceptedConnections);
    }

    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("::ffff:127.0.0.1")]
    public async Task HostNameResolvingToLoopback_IsBlockedBeforeAnyConnection(string resolvedAddress)
    {
        await using var server = LoopbackServer.StartIPv4();
        var resolver = new StaticResolver(IPAddress.Parse(resolvedAddress));
        using var client = CreateClient(RealConnector(server.Port, resolver));

        var exception = await Assert.ThrowsAsync<HttpRequestException>(() => client.GetAsync($"http://internal.example.test:{server.Port}/"));

        Assert.Equal(UpstreamConnectionRejection.BlockedAddress, FindRejection(exception).Reason);
        Assert.Equal(0, server.AcceptedConnections);
    }

    [Fact]
    public async Task DisallowedPort_IsBlockedBeforeAnyConnection()
    {
        await using var server = LoopbackServer.StartIPv4();
        var simulator = new PublicUpstreamSimulator(server);
        using var client = CreateClient(new GuardedConnector(new DestinationPolicy(), simulator, simulator));

        var exception = await Assert.ThrowsAsync<HttpRequestException>(() => client.GetAsync("http://public.example.test:6379/"));

        Assert.Equal(UpstreamConnectionRejection.BlockedPort, FindRejection(exception).Reason);
        Assert.Empty(simulator.Attempts);
        Assert.Equal(0, server.AcceptedConnections);
    }

    [Fact]
    public async Task PublicUpstream_IsReachedThroughTheCheckedEndpoint()
    {
        await using var server = LoopbackServer.StartIPv4();
        server.EnqueueResponse("HTTP/1.1 200 OK\r\nContent-Type: text/plain", "ok"u8.ToArray());
        var simulator = new PublicUpstreamSimulator(server);
        using var client = CreateClient(new GuardedConnector(new DestinationPolicy(), simulator, simulator));

        using var response = await client.GetAsync("http://public.example.test/data");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("ok", await response.Content.ReadAsStringAsync());
        Assert.Equal([new IPEndPoint(PublicUpstreamSimulator.PublicAddress, 80)], simulator.Attempts);
        Assert.Equal(1, server.AcceptedConnections);
        var head = Assert.Single(server.Requests);
        Assert.StartsWith("GET /data HTTP/1.1\r\n", head);
        Assert.Contains("\r\nHost: public.example.test", head);
    }

    [Theory]
    [InlineData("http://169.254.169.254/latest/meta-data/")]
    [InlineData("http://127.0.0.1/")]
    [InlineData("/elsewhere")]
    public async Task Redirects_AreReturnedNotFollowed(string location)
    {
        await using var server = LoopbackServer.StartIPv4();
        server.EnqueueResponse($"HTTP/1.1 302 Found\r\nLocation: {location}");
        server.EnqueueResponse("HTTP/1.1 200 OK", "followed"u8.ToArray());
        var simulator = new PublicUpstreamSimulator(server);
        using var client = CreateClient(new GuardedConnector(new DestinationPolicy(), simulator, simulator));

        using var response = await client.GetAsync("http://public.example.test/");

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.Single(server.Requests);
        Assert.Single(simulator.Attempts);
    }

    [Fact]
    public async Task Cookies_AreNeitherStoredNorSent()
    {
        await using var server = LoopbackServer.StartIPv4();
        server.EnqueueResponse("HTTP/1.1 200 OK\r\nSet-Cookie: session=abc123; Path=/");
        server.EnqueueResponse("HTTP/1.1 200 OK");
        var simulator = new PublicUpstreamSimulator(server);
        using var client = CreateClient(new GuardedConnector(new DestinationPolicy(), simulator, simulator));

        (await client.GetAsync("http://public.example.test/login")).Dispose();
        (await client.GetAsync("http://public.example.test/next")).Dispose();

        Assert.Equal(2, server.Requests.Count);
        Assert.All(server.Requests, head => Assert.DoesNotContain("abc123", head));
        Assert.All(server.Requests, head => AssertNoHeader(head, "Cookie"));
    }

    [Fact]
    public async Task Handler_AddsNoAmbientCredentialsOrEncodingHeaders()
    {
        await using var server = LoopbackServer.StartIPv4();
        server.EnqueueResponse("HTTP/1.1 200 OK");
        var simulator = new PublicUpstreamSimulator(server);
        using var client = CreateClient(new GuardedConnector(new DestinationPolicy(), simulator, simulator));

        (await client.GetAsync("http://public.example.test/")).Dispose();

        var head = Assert.Single(server.Requests);
        AssertNoHeader(head, "Authorization");
        AssertNoHeader(head, "Proxy-Authorization");
        AssertNoHeader(head, "Cookie");
        AssertNoHeader(head, "Accept-Encoding");
    }

    [Fact]
    public async Task CompressedResponses_AreNotDecompressed()
    {
        var compressed = Gzip("hello world");
        await using var server = LoopbackServer.StartIPv4();
        server.EnqueueResponse("HTTP/1.1 200 OK\r\nContent-Type: text/plain\r\nContent-Encoding: gzip", compressed);
        var simulator = new PublicUpstreamSimulator(server);
        using var client = CreateClient(new GuardedConnector(new DestinationPolicy(), simulator, simulator));

        using var response = await client.GetAsync("http://public.example.test/");

        Assert.Contains("gzip", response.Content.Headers.ContentEncoding);
        Assert.Equal(compressed, await response.Content.ReadAsByteArrayAsync());
    }

    [Theory]
    [InlineData(3, HttpVersionPolicy.RequestVersionExact)]
    [InlineData(3, HttpVersionPolicy.RequestVersionOrHigher)]
    [InlineData(2, HttpVersionPolicy.RequestVersionExact)]
    public async Task RequestedHttpVersion_IsPinnedToHttp11(int major, HttpVersionPolicy policy)
    {
        await using var server = LoopbackServer.StartIPv4();
        server.EnqueueResponse("HTTP/1.1 200 OK");
        var simulator = new PublicUpstreamSimulator(server);
        using var client = CreateClient(new GuardedConnector(new DestinationPolicy(), simulator, simulator));
        using var request = new HttpRequestMessage(HttpMethod.Get, "http://public.example.test/")
        {
            Version = new Version(major, 0),
            VersionPolicy = policy
        };

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.StartsWith("GET / HTTP/1.1\r\n", Assert.Single(server.Requests));
        Assert.Single(simulator.Attempts); // went through ConnectCallback, not QUIC
    }

    [Fact]
    public async Task ConnectTimeout_CoversTheGuardedConnectIncludingDns()
    {
        var resolver = new HangingResolver();
        var connector = new GuardedConnector(new DestinationPolicy(), resolver, new SystemSocketConnector());
        using var client = CreateClient(connector, new UpstreamHandlerOptions { ConnectTimeout = TimeSpan.FromMilliseconds(200) });
        var stopwatch = Stopwatch.StartNew();

        await Assert.ThrowsAnyAsync<Exception>(() => client.GetAsync("http://slow-dns.example.test/"));

        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(5), $"Took {stopwatch.Elapsed}.");
        Assert.True(resolver.WasCancelled);
    }

    private static HttpClient CreateClient(GuardedConnector connector, UpstreamHandlerOptions? options = null) =>
        new(new UpstreamHttpVersionHandler { InnerHandler = UpstreamHandlerFactory.CreatePrimaryHandler(connector, options) })
        {
            Timeout = TimeSpan.FromSeconds(10)
        };

    private static GuardedConnector DefaultConnector() =>
        new(new DestinationPolicy(), new SystemDnsResolver(), new SystemSocketConnector());

    // Real DNS and sockets; the loopback server's port is allowed so only the address rules can block.
    private static GuardedConnector RealConnector(int port, IDnsResolver? resolver = null) =>
        new(new DestinationPolicy([port]), resolver ?? new SystemDnsResolver(), new SystemSocketConnector());

    private static UpstreamConnectionRejectedException FindRejection(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is UpstreamConnectionRejectedException rejection)
            {
                return rejection;
            }
        }

        throw new Xunit.Sdk.XunitException($"Expected an {nameof(UpstreamConnectionRejectedException)} in the chain, got: {exception}");
    }

    private static void AssertNoHeader(string head, string name)
    {
        var lines = head.Split("\r\n").Skip(1);
        Assert.DoesNotContain(lines, line => line.StartsWith(name + ":", StringComparison.OrdinalIgnoreCase));
    }

    private static byte[] Gzip(string text)
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.Optimal))
        {
            gzip.Write(Encoding.UTF8.GetBytes(text));
        }

        return output.ToArray();
    }

    private static async Task WaitForAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition() && DateTime.UtcNow < deadline)
        {
            await Task.Delay(10);
        }
    }

    private sealed class StaticResolver(params IPAddress[] addresses) : IDnsResolver
    {
        public Task<IPAddress[]> ResolveAsync(string host, CancellationToken cancellationToken) => Task.FromResult(addresses);
    }

    private sealed class HangingResolver : IDnsResolver
    {
        public bool WasCancelled { get; private set; }

        public async Task<IPAddress[]> ResolveAsync(string host, CancellationToken cancellationToken)
        {
            try
            {
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                WasCancelled = true;
                throw;
            }

            return [];
        }
    }

    /// <summary>
    /// Resolves every name to a public address (which passes the policy) and opens the socket for that checked
    /// endpoint to the loopback server, recording the endpoint the guarded connector asked for.
    /// </summary>
    private sealed class PublicUpstreamSimulator(LoopbackServer server) : IDnsResolver, ISocketConnector
    {
        public static readonly IPAddress PublicAddress = IPAddress.Parse("93.184.215.14");

        public List<IPEndPoint> Attempts { get; } = [];

        public Task<IPAddress[]> ResolveAsync(string host, CancellationToken cancellationToken) =>
            Task.FromResult(new[] { PublicAddress });

        public Task<Stream> ConnectAsync(IPEndPoint endPoint, CancellationToken cancellationToken)
        {
            lock (Attempts)
            {
                Attempts.Add(endPoint);
            }

            return new SystemSocketConnector().ConnectAsync(server.EndPoint, cancellationToken);
        }
    }
}
