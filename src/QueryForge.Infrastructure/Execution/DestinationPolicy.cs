using System.Net;
using System.Net.Sockets;

namespace QueryForge.Infrastructure.Execution;

public enum DestinationDecision
{
    Allowed,
    BlockedAddress,
    BlockedPort
}

/// <summary>
/// Decides whether an upstream connection to a resolved IP address and port is permitted. Pure and I/O-free: it is
/// applied to the address actually being connected to (see the guarded connector), never to a host name string.
/// </summary>
/// <remarks>
/// IPv4 uses a deny-list of non-public ranges. IPv6 is allowed only inside global unicast (2000::/3), minus
/// special-purpose, documentation and IPv4-translation prefixes; everything outside 2000::/3 (loopback, unique local,
/// link-local, multicast, NAT64, IPv4-compatible) is therefore blocked by default. IPv4-mapped IPv6 addresses are
/// checked as IPv4. The lists are static: new special-purpose ranges require a code change.
/// </remarks>
public sealed class DestinationPolicy
{
    public static readonly IReadOnlyList<int> DefaultAllowedPorts = [80, 443];

    private static readonly IPNetwork[] BlockedIPv4 =
    [
        IPNetwork.Parse("0.0.0.0/8"),        // "this" network
        IPNetwork.Parse("10.0.0.0/8"),       // private
        IPNetwork.Parse("100.64.0.0/10"),    // carrier-grade NAT
        IPNetwork.Parse("127.0.0.0/8"),      // loopback
        IPNetwork.Parse("169.254.0.0/16"),   // link-local, including cloud metadata 169.254.169.254
        IPNetwork.Parse("172.16.0.0/12"),    // private (includes the Compose network)
        IPNetwork.Parse("192.0.0.0/24"),     // IETF protocol assignments
        IPNetwork.Parse("192.0.2.0/24"),     // documentation (TEST-NET-1)
        IPNetwork.Parse("192.88.99.0/24"),   // deprecated 6to4 relay anycast
        IPNetwork.Parse("192.168.0.0/16"),   // private
        IPNetwork.Parse("198.18.0.0/15"),    // benchmarking
        IPNetwork.Parse("198.51.100.0/24"),  // documentation (TEST-NET-2)
        IPNetwork.Parse("203.0.113.0/24"),   // documentation (TEST-NET-3)
        IPNetwork.Parse("224.0.0.0/4"),      // multicast
        IPNetwork.Parse("240.0.0.0/4")       // reserved, including broadcast 255.255.255.255
    ];

    private static readonly IPNetwork GlobalUnicastIPv6 = IPNetwork.Parse("2000::/3");

    private static readonly IPNetwork[] BlockedIPv6 =
    [
        IPNetwork.Parse("2001::/23"),        // IETF protocol assignments, including Teredo 2001::/32
        IPNetwork.Parse("2001:db8::/32"),    // documentation
        IPNetwork.Parse("2002::/16"),        // 6to4 (embeds an IPv4 address)
        IPNetwork.Parse("3fff::/20")         // documentation
    ];

    private readonly HashSet<int> _allowedPorts;

    public DestinationPolicy()
        : this(DefaultAllowedPorts)
    {
    }

    public DestinationPolicy(IEnumerable<int> allowedPorts)
    {
        ArgumentNullException.ThrowIfNull(allowedPorts);

        _allowedPorts = [];
        foreach (var port in allowedPorts)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(port, IPEndPoint.MinPort + 1, nameof(allowedPorts));
            ArgumentOutOfRangeException.ThrowIfGreaterThan(port, IPEndPoint.MaxPort, nameof(allowedPorts));
            _allowedPorts.Add(port);
        }

        if (_allowedPorts.Count == 0)
        {
            throw new ArgumentException("At least one port must be allowed.", nameof(allowedPorts));
        }
    }

    public DestinationDecision Evaluate(IPAddress address, int port)
    {
        if (!IsAddressAllowed(address))
        {
            return DestinationDecision.BlockedAddress;
        }

        return IsPortAllowed(port) ? DestinationDecision.Allowed : DestinationDecision.BlockedPort;
    }

    public bool IsPortAllowed(int port) => _allowedPorts.Contains(port);

    public static bool IsAddressAllowed(IPAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);

        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        return address.AddressFamily switch
        {
            AddressFamily.InterNetwork => !BlockedIPv4.Any(network => network.Contains(address)),
            AddressFamily.InterNetworkV6 => GlobalUnicastIPv6.Contains(address) && !BlockedIPv6.Any(network => network.Contains(address)),
            _ => false
        };
    }
}
