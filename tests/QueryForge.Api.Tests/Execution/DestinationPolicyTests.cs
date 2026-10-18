using System.Net;
using QueryForge.Infrastructure.Execution;

namespace QueryForge.Api.Tests.Execution;

public sealed class DestinationPolicyTests
{
    [Theory]
    [InlineData("0.0.0.0")]
    [InlineData("0.255.255.255")]
    [InlineData("10.0.0.1")]
    [InlineData("10.255.255.255")]
    [InlineData("100.64.0.0")]
    [InlineData("100.127.255.255")]
    [InlineData("127.0.0.1")]
    [InlineData("127.255.255.254")]
    [InlineData("169.254.169.254")] // cloud metadata
    [InlineData("169.254.0.1")]
    [InlineData("172.16.0.0")]
    [InlineData("172.31.255.255")]
    [InlineData("172.30.89.10")] // Compose network
    [InlineData("192.0.0.1")]
    [InlineData("192.0.2.1")]
    [InlineData("192.88.99.1")]
    [InlineData("192.168.0.1")]
    [InlineData("192.168.255.255")]
    [InlineData("198.18.0.0")]
    [InlineData("198.19.255.255")]
    [InlineData("198.51.100.1")]
    [InlineData("203.0.113.1")]
    [InlineData("224.0.0.1")]
    [InlineData("239.255.255.255")]
    [InlineData("240.0.0.1")]
    [InlineData("255.255.255.255")]
    public void IPv4_NonPublicAddresses_AreBlocked(string address)
    {
        Assert.False(DestinationPolicy.IsAddressAllowed(IPAddress.Parse(address)));
    }

    [Theory]
    [InlineData("1.1.1.1")]
    [InlineData("8.8.8.8")]
    [InlineData("9.255.255.255")]   // just below 10/8
    [InlineData("11.0.0.0")]        // just above 10/8
    [InlineData("100.63.255.255")]  // just below 100.64/10
    [InlineData("100.128.0.0")]     // just above 100.64/10
    [InlineData("126.255.255.255")] // just below 127/8
    [InlineData("128.0.0.0")]       // just above 127/8
    [InlineData("169.253.255.255")] // just below 169.254/16
    [InlineData("169.255.0.0")]     // just above 169.254/16
    [InlineData("172.15.255.255")]  // just below 172.16/12
    [InlineData("172.32.0.0")]      // just above 172.16/12
    [InlineData("192.0.1.1")]       // between 192.0.0/24 and 192.0.2/24
    [InlineData("192.167.255.255")] // just below 192.168/16
    [InlineData("192.169.0.0")]     // just above 192.168/16
    [InlineData("198.17.255.255")]  // just below 198.18/15
    [InlineData("198.20.0.0")]      // just above 198.18/15
    [InlineData("223.255.255.255")] // just below 224/4
    public void IPv4_PublicAddresses_AreAllowed(string address)
    {
        Assert.True(DestinationPolicy.IsAddressAllowed(IPAddress.Parse(address)));
    }

    [Theory]
    [InlineData("::")]
    [InlineData("::1")]
    [InlineData("::127.0.0.1")]                 // deprecated IPv4-compatible form
    [InlineData("::808:808")]                   // IPv4-compatible form of a public address
    [InlineData("64:ff9b::7f00:1")]             // NAT64 of 127.0.0.1
    [InlineData("64:ff9b::808:808")]            // NAT64 is blocked even for public IPv4
    [InlineData("64:ff9b:1::1")]                // local-use NAT64
    [InlineData("100::1")]                      // discard-only
    [InlineData("fc00::1")]                     // unique local
    [InlineData("fd00:ec2::254")]               // AWS instance metadata (IPv6)
    [InlineData("fe80::1")]                     // link-local
    [InlineData("fe80::1%2")]                   // link-local with a zone
    [InlineData("fec0::1")]                     // deprecated site-local
    [InlineData("ff02::1")]                     // multicast
    [InlineData("ff05::1:3")]
    [InlineData("1fff:ffff:ffff:ffff:ffff:ffff:ffff:ffff")] // just below 2000::/3
    [InlineData("4000::1")]                     // just above 2000::/3
    [InlineData("2001::1")]                     // Teredo
    [InlineData("2001:0:4136:e378:8000:63bf:3fff:fdd2")] // Teredo
    [InlineData("2001:2::1")]                   // benchmarking
    [InlineData("2001:10::1")]                  // ORCHID
    [InlineData("2001:1ff:ffff::1")]            // top of 2001::/23
    [InlineData("2001:db8::1")]                 // documentation
    [InlineData("2001:db8:ffff::1")]
    [InlineData("2002:7f00:1::1")]              // 6to4 of 127.0.0.1
    [InlineData("2002:808:808::1")]             // 6to4 is blocked even for public IPv4
    [InlineData("3fff::1")]                     // documentation
    [InlineData("3fff:fff:ffff::1")]            // top of 3fff::/20
    public void IPv6_NonGlobalAndSpecialPurposeAddresses_AreBlocked(string address)
    {
        Assert.False(DestinationPolicy.IsAddressAllowed(IPAddress.Parse(address)));
    }

    [Theory]
    [InlineData("2606:4700:4700::1111")]
    [InlineData("2001:4860:4860::8888")]
    [InlineData("2a00:1450:4001::1")]
    [InlineData("2000::1")]          // bottom of 2000::/3
    [InlineData("2001:200::1")]      // just above 2001::/23
    [InlineData("2001:db9::1")]      // just above 2001:db8::/32
    [InlineData("2003::1")]          // just above 2002::/16
    [InlineData("3fff:1000::1")]     // just above 3fff::/20
    public void IPv6_GlobalUnicastAddresses_AreAllowed(string address)
    {
        Assert.True(DestinationPolicy.IsAddressAllowed(IPAddress.Parse(address)));
    }

    [Theory]
    [InlineData("::ffff:127.0.0.1")]
    [InlineData("::ffff:10.0.0.1")]
    [InlineData("::ffff:169.254.169.254")]
    [InlineData("::ffff:0.0.0.0")]
    [InlineData("::ffff:172.30.89.10")]
    public void IPv4MappedIPv6_IsCheckedAsIPv4_AndBlockedForNonPublic(string address)
    {
        var parsed = IPAddress.Parse(address);

        Assert.True(parsed.IsIPv4MappedToIPv6);
        Assert.False(DestinationPolicy.IsAddressAllowed(parsed));
    }

    [Theory]
    [InlineData("::ffff:8.8.8.8")]
    [InlineData("::ffff:1.1.1.1")]
    public void IPv4MappedIPv6_IsCheckedAsIPv4_AndAllowedForPublic(string address)
    {
        Assert.True(DestinationPolicy.IsAddressAllowed(IPAddress.Parse(address)));
    }

    [Theory]
    [InlineData(80)]
    [InlineData(443)]
    public void DefaultPolicy_AllowsHttpAndHttpsPorts(int port)
    {
        Assert.Equal(DestinationDecision.Allowed, new DestinationPolicy().Evaluate(IPAddress.Parse("8.8.8.8"), port));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(22)]
    [InlineData(25)]
    [InlineData(1433)]
    [InlineData(6379)]
    [InlineData(8080)]
    [InlineData(8443)]
    [InlineData(65535)]
    public void DefaultPolicy_BlocksOtherPorts(int port)
    {
        Assert.Equal(DestinationDecision.BlockedPort, new DestinationPolicy().Evaluate(IPAddress.Parse("8.8.8.8"), port));
    }

    [Fact]
    public void Evaluate_ReportsBlockedAddressBeforePort()
    {
        // A blocked address is reported as such even on a disallowed port, so logs show the more important reason.
        Assert.Equal(DestinationDecision.BlockedAddress, new DestinationPolicy().Evaluate(IPAddress.Loopback, 22));
        Assert.Equal(DestinationDecision.BlockedAddress, new DestinationPolicy().Evaluate(IPAddress.Loopback, 443));
    }

    [Fact]
    public void ConfiguredPorts_ReplaceTheDefaults()
    {
        var policy = new DestinationPolicy([8443]);

        Assert.Equal(DestinationDecision.Allowed, policy.Evaluate(IPAddress.Parse("8.8.8.8"), 8443));
        Assert.Equal(DestinationDecision.BlockedPort, policy.Evaluate(IPAddress.Parse("8.8.8.8"), 443));
    }

    [Fact]
    public void ConfiguredPorts_DoNotRelaxAddressRules()
    {
        var policy = new DestinationPolicy([8443]);

        Assert.Equal(DestinationDecision.BlockedAddress, policy.Evaluate(IPAddress.Loopback, 8443));
        Assert.Equal(DestinationDecision.BlockedAddress, policy.Evaluate(IPAddress.Parse("169.254.169.254"), 8443));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(65536)]
    public void Constructor_RejectsOutOfRangePorts(int port)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new DestinationPolicy([port]));
    }

    [Fact]
    public void Constructor_RejectsEmptyPortList()
    {
        Assert.Throws<ArgumentException>(() => new DestinationPolicy([]));
    }

    [Fact]
    public void IsAddressAllowed_RejectsNull()
    {
        Assert.Throws<ArgumentNullException>(() => DestinationPolicy.IsAddressAllowed(null!));
    }
}
