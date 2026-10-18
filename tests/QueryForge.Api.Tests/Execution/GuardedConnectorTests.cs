using System.Net;
using System.Net.Sockets;
using QueryForge.Infrastructure.Execution;

namespace QueryForge.Api.Tests.Execution;

public sealed class GuardedConnectorTests
{
    private static readonly IPAddress Public1 = IPAddress.Parse("8.8.8.8");
    private static readonly IPAddress Public2 = IPAddress.Parse("1.1.1.1");

    [Fact]
    public async Task PublicHost_ConnectsToTheResolvedAddressAndPort()
    {
        var resolver = new FakeDnsResolver(_ => [Public1]);
        var connector = new FakeSocketConnector();

        var stream = await Create(resolver, connector).ConnectAsync("api.example.test", 443, CancellationToken.None);

        Assert.Same(connector.LastStream, stream);
        Assert.Equal([new IPEndPoint(Public1, 443)], connector.Attempts);
        Assert.Equal(["api.example.test"], resolver.Queries);
    }

    [Fact]
    public async Task DnsRebinding_ConnectsOnlyToTheAddressThatWasChecked()
    {
        // First answer is public, every later answer is loopback: a classic rebinding attack.
        var resolver = new FakeDnsResolver(call => call == 1 ? [Public1] : [IPAddress.Loopback]);
        var connector = new FakeSocketConnector();
        var guarded = Create(resolver, connector);

        await guarded.ConnectAsync("rebind.example.test", 443, CancellationToken.None);

        // Resolved once; the socket went to the checked public address, never to a second lookup's result.
        Assert.Single(resolver.Queries);
        Assert.Equal([new IPEndPoint(Public1, 443)], connector.Attempts);

        // The next connection resolves again and is checked again, so the loopback answer is rejected.
        var rejected = await Assert.ThrowsAsync<UpstreamConnectionRejectedException>(
            () => guarded.ConnectAsync("rebind.example.test", 443, CancellationToken.None));
        Assert.Equal(UpstreamConnectionRejection.BlockedAddress, rejected.Reason);
        Assert.Equal(2, resolver.Queries.Count);
        Assert.Single(connector.Attempts);
    }

    [Theory]
    [InlineData("8.8.8.8", "127.0.0.1")]
    [InlineData("127.0.0.1", "8.8.8.8")]
    [InlineData("2606:4700:4700::1111", "::1")]
    [InlineData("8.8.8.8", "169.254.169.254")]
    [InlineData("1.1.1.1", "::ffff:10.0.0.1")]
    public async Task MixedAnswer_WithAnyBlockedAddress_IsRejectedWithoutConnecting(string first, string second)
    {
        var connector = new FakeSocketConnector();
        var guarded = Create(new FakeDnsResolver(_ => [IPAddress.Parse(first), IPAddress.Parse(second)]), connector);

        var rejected = await Assert.ThrowsAsync<UpstreamConnectionRejectedException>(
            () => guarded.ConnectAsync("mixed.example.test", 443, CancellationToken.None));

        Assert.Equal(UpstreamConnectionRejection.BlockedAddress, rejected.Reason);
        Assert.Empty(connector.Attempts);
    }

    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("10.0.0.1")]
    [InlineData("172.30.89.10")]
    [InlineData("169.254.169.254")]
    [InlineData("::1")]
    [InlineData("::ffff:127.0.0.1")]
    [InlineData("fd00:ec2::254")]
    [InlineData("fe80::1")]
    public async Task HostResolvingToBlockedAddress_IsRejectedWithoutConnecting(string address)
    {
        var connector = new FakeSocketConnector();
        var guarded = Create(new FakeDnsResolver(_ => [IPAddress.Parse(address)]), connector);

        var rejected = await Assert.ThrowsAsync<UpstreamConnectionRejectedException>(
            () => guarded.ConnectAsync("internal.example.test", 443, CancellationToken.None));

        Assert.Equal(UpstreamConnectionRejection.BlockedAddress, rejected.Reason);
        Assert.Empty(connector.Attempts);
    }

    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("127.1")]
    [InlineData("2130706433")]
    [InlineData("0x7f000001")]
    [InlineData("::1")]
    [InlineData("[::1]")]
    [InlineData("::ffff:127.0.0.1")]
    [InlineData("[::ffff:127.0.0.1]")]
    [InlineData("169.254.169.254")]
    [InlineData("fe80::1%2")]
    public async Task BlockedIpLiteral_IsRejectedWithoutDnsOrConnecting(string host)
    {
        var resolver = new FakeDnsResolver(_ => throw new InvalidOperationException("IP literals must not be resolved."));
        var connector = new FakeSocketConnector();

        var rejected = await Assert.ThrowsAsync<UpstreamConnectionRejectedException>(
            () => Create(resolver, connector).ConnectAsync(host, 443, CancellationToken.None));

        Assert.Equal(UpstreamConnectionRejection.BlockedAddress, rejected.Reason);
        Assert.Empty(resolver.Queries);
        Assert.Empty(connector.Attempts);
    }

    [Theory]
    [InlineData("8.8.8.8", "8.8.8.8")]
    [InlineData("[2606:4700:4700::1111]", "2606:4700:4700::1111")]
    public async Task PublicIpLiteral_ConnectsWithoutDns(string host, string expected)
    {
        var resolver = new FakeDnsResolver(_ => throw new InvalidOperationException("IP literals must not be resolved."));
        var connector = new FakeSocketConnector();

        await Create(resolver, connector).ConnectAsync(host, 443, CancellationToken.None);

        Assert.Empty(resolver.Queries);
        Assert.Equal([new IPEndPoint(IPAddress.Parse(expected), 443)], connector.Attempts);
    }

    [Theory]
    [InlineData(22)]
    [InlineData(6379)]
    [InlineData(8080)]
    public async Task BlockedPort_IsRejectedBeforeAnyDnsLookup(int port)
    {
        var resolver = new FakeDnsResolver(_ => [Public1]);
        var connector = new FakeSocketConnector();

        var rejected = await Assert.ThrowsAsync<UpstreamConnectionRejectedException>(
            () => Create(resolver, connector).ConnectAsync("api.example.test", port, CancellationToken.None));

        Assert.Equal(UpstreamConnectionRejection.BlockedPort, rejected.Reason);
        Assert.Empty(resolver.Queries);
        Assert.Empty(connector.Attempts);
    }

    [Fact]
    public async Task ConfiguredPort_IsAllowedButAddressRulesStillApply()
    {
        var connector = new FakeSocketConnector();
        var policy = new DestinationPolicy([8443]);

        await new GuardedConnector(policy, new FakeDnsResolver(_ => [Public1]), connector)
            .ConnectAsync("api.example.test", 8443, CancellationToken.None);
        var rejected = await Assert.ThrowsAsync<UpstreamConnectionRejectedException>(
            () => new GuardedConnector(policy, new FakeDnsResolver(_ => [IPAddress.Loopback]), connector)
                .ConnectAsync("internal.example.test", 8443, CancellationToken.None));

        Assert.Equal(UpstreamConnectionRejection.BlockedAddress, rejected.Reason);
        Assert.Equal([new IPEndPoint(Public1, 8443)], connector.Attempts);
    }

    [Fact]
    public async Task EmptyDnsAnswer_IsRejectedAsResolutionFailure()
    {
        var connector = new FakeSocketConnector();

        var rejected = await Assert.ThrowsAsync<UpstreamConnectionRejectedException>(
            () => Create(new FakeDnsResolver(_ => []), connector).ConnectAsync("empty.example.test", 443, CancellationToken.None));

        Assert.Equal(UpstreamConnectionRejection.ResolutionFailed, rejected.Reason);
        Assert.Empty(connector.Attempts);
    }

    [Fact]
    public async Task DnsError_IsRejectedAsResolutionFailure_KeepingTheCause()
    {
        var dnsError = new SocketException((int)SocketError.HostNotFound);
        var connector = new FakeSocketConnector();

        var rejected = await Assert.ThrowsAsync<UpstreamConnectionRejectedException>(
            () => Create(new FakeDnsResolver(_ => throw dnsError), connector).ConnectAsync("missing.example.test", 443, CancellationToken.None));

        Assert.Equal(UpstreamConnectionRejection.ResolutionFailed, rejected.Reason);
        Assert.Same(dnsError, rejected.InnerException);
        Assert.Empty(connector.Attempts);
    }

    [Fact]
    public async Task RejectionMessages_DoNotRevealHostOrAddresses()
    {
        var connector = new FakeSocketConnector();

        var rejected = await Assert.ThrowsAsync<UpstreamConnectionRejectedException>(
            () => Create(new FakeDnsResolver(_ => [IPAddress.Parse("10.1.2.3")]), connector)
                .ConnectAsync("secret-internal.example.test", 443, CancellationToken.None));

        Assert.DoesNotContain("secret-internal", rejected.Message);
        Assert.DoesNotContain("10.1.2.3", rejected.Message);
    }

    [Fact]
    public async Task ConnectFailure_FallsBackToTheNextCheckedAddressWithoutResolvingAgain()
    {
        var resolver = new FakeDnsResolver(_ => [Public1, Public2]);
        var connector = new FakeSocketConnector(failFor: [Public1]);

        await Create(resolver, connector).ConnectAsync("api.example.test", 443, CancellationToken.None);

        Assert.Single(resolver.Queries);
        Assert.Equal([new IPEndPoint(Public1, 443), new IPEndPoint(Public2, 443)], connector.Attempts);
    }

    [Fact]
    public async Task ConnectFailure_OnEveryAddress_SurfacesTheSocketError()
    {
        var connector = new FakeSocketConnector(failFor: [Public1, Public2]);

        await Assert.ThrowsAsync<SocketException>(
            () => Create(new FakeDnsResolver(_ => [Public1, Public2]), connector).ConnectAsync("down.example.test", 443, CancellationToken.None));

        Assert.Equal(2, connector.Attempts.Count);
    }

    [Fact]
    public async Task UnexpectedConnectorError_IsNotRetriedOnOtherAddresses()
    {
        // Only socket-level connect failures fall back to the next address; anything else surfaces immediately.
        var connector = new FakeSocketConnector(onConnect: () => throw new InvalidOperationException("unexpected"));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => Create(new FakeDnsResolver(_ => [Public1, Public2]), connector).ConnectAsync("api.example.test", 443, CancellationToken.None));

        Assert.Single(connector.Attempts);
    }

    [Fact]
    public async Task DuplicateAnswers_AreTriedOnce()
    {
        var connector = new FakeSocketConnector(failFor: [Public1]);

        await Assert.ThrowsAsync<SocketException>(
            () => Create(new FakeDnsResolver(_ => [Public1, Public1]), connector).ConnectAsync("api.example.test", 443, CancellationToken.None));

        Assert.Single(connector.Attempts);
    }

    [Fact]
    public async Task AlreadyCancelled_DoesNothing()
    {
        var resolver = new FakeDnsResolver(_ => [Public1]);
        var connector = new FakeSocketConnector();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => Create(resolver, connector).ConnectAsync("api.example.test", 443, new CancellationToken(canceled: true)));

        Assert.Empty(resolver.Queries);
        Assert.Empty(connector.Attempts);
    }

    [Fact]
    public async Task CancellationDuringDnsLookup_StopsBeforeConnecting()
    {
        using var cancellation = new CancellationTokenSource();
        var resolver = new FakeDnsResolver(_ =>
        {
            cancellation.Cancel();
            cancellation.Token.ThrowIfCancellationRequested();
            return [Public1];
        });
        var connector = new FakeSocketConnector();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => Create(resolver, connector).ConnectAsync("slow.example.test", 443, cancellation.Token));

        Assert.Empty(connector.Attempts);
    }

    [Fact]
    public async Task CancellationDuringConnect_IsNotRetriedOnOtherAddresses()
    {
        using var cancellation = new CancellationTokenSource();
        var connector = new FakeSocketConnector(onConnect: () =>
        {
            cancellation.Cancel();
            cancellation.Token.ThrowIfCancellationRequested();
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => Create(new FakeDnsResolver(_ => [Public1, Public2]), connector).ConnectAsync("api.example.test", 443, cancellation.Token));

        Assert.Single(connector.Attempts);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public async Task EmptyHost_IsRejected(string host)
    {
        await Assert.ThrowsAsync<ArgumentException>(
            () => Create(new FakeDnsResolver(_ => [Public1]), new FakeSocketConnector()).ConnectAsync(host, 443, CancellationToken.None));
    }

    private static GuardedConnector Create(IDnsResolver resolver, ISocketConnector connector) =>
        new(new DestinationPolicy(), resolver, connector);

    private sealed class FakeDnsResolver(Func<int, IPAddress[]> answer) : IDnsResolver
    {
        public List<string> Queries { get; } = [];

        public Task<IPAddress[]> ResolveAsync(string host, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Queries.Add(host);
            return Task.FromResult(answer(Queries.Count));
        }
    }

    private sealed class FakeSocketConnector(IPAddress[]? failFor = null, Action? onConnect = null) : ISocketConnector
    {
        public List<IPEndPoint> Attempts { get; } = [];

        public Stream? LastStream { get; private set; }

        public Task<Stream> ConnectAsync(IPEndPoint endPoint, CancellationToken cancellationToken)
        {
            Attempts.Add(endPoint);
            onConnect?.Invoke();
            if (failFor?.Contains(endPoint.Address) == true)
            {
                throw new SocketException((int)SocketError.ConnectionRefused);
            }

            LastStream = new MemoryStream();
            return Task.FromResult(LastStream);
        }
    }
}
