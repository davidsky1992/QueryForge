using System.Net;
using System.Net.Sockets;

namespace QueryForge.Infrastructure.Execution;

public enum UpstreamConnectionRejection
{
    BlockedAddress,
    BlockedPort,
    ResolutionFailed
}

/// <summary>
/// Thrown when an upstream connection is refused before any socket is opened. The message is fixed per reason and
/// never contains the host name or resolved addresses, so it is safe to log.
/// </summary>
public sealed class UpstreamConnectionRejectedException : Exception
{
    public UpstreamConnectionRejectedException(UpstreamConnectionRejection reason, Exception? innerException = null)
        : base(MessageFor(reason), innerException)
    {
        Reason = reason;
    }

    public UpstreamConnectionRejection Reason { get; }

    private static string MessageFor(UpstreamConnectionRejection reason) => reason switch
    {
        UpstreamConnectionRejection.BlockedAddress => "The upstream host resolves to an address that is not allowed.",
        UpstreamConnectionRejection.BlockedPort => "The upstream port is not allowed.",
        _ => "The upstream host could not be resolved."
    };
}

public interface IDnsResolver
{
    Task<IPAddress[]> ResolveAsync(string host, CancellationToken cancellationToken);
}

public interface ISocketConnector
{
    Task<Stream> ConnectAsync(IPEndPoint endPoint, CancellationToken cancellationToken);
}

public sealed class SystemDnsResolver : IDnsResolver
{
    public Task<IPAddress[]> ResolveAsync(string host, CancellationToken cancellationToken) =>
        Dns.GetHostAddressesAsync(host, cancellationToken);
}

public sealed class SystemSocketConnector : ISocketConnector
{
    public async Task<Stream> ConnectAsync(IPEndPoint endPoint, CancellationToken cancellationToken)
    {
        var socket = new Socket(endPoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        try
        {
            await socket.ConnectAsync(endPoint, cancellationToken);
            return new NetworkStream(socket, ownsSocket: true);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }
}

/// <summary>
/// Opens upstream TCP connections only to addresses permitted by <see cref="DestinationPolicy"/>. The host is resolved
/// exactly once per connection, every resolved address is checked, and the socket connects to one of those checked
/// addresses — never to a name — so DNS rebinding between the check and the connect is not possible.
/// Intended for <c>SocketsHttpHandler.ConnectCallback</c>, with proxies disabled so the host is the real destination.
/// </summary>
public sealed class GuardedConnector
{
    private readonly DestinationPolicy _policy;
    private readonly IDnsResolver _resolver;
    private readonly ISocketConnector _connector;

    public GuardedConnector(DestinationPolicy policy, IDnsResolver resolver, ISocketConnector connector)
    {
        _policy = policy ?? throw new ArgumentNullException(nameof(policy));
        _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
        _connector = connector ?? throw new ArgumentNullException(nameof(connector));
    }

    public async Task<Stream> ConnectAsync(string host, int port, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(host);
        cancellationToken.ThrowIfCancellationRequested();

        // Checked before resolving, so a disallowed port never causes a DNS lookup.
        if (!_policy.IsPortAllowed(port))
        {
            throw new UpstreamConnectionRejectedException(UpstreamConnectionRejection.BlockedPort);
        }

        var addresses = await ResolveAsync(host, cancellationToken);

        // Any blocked address rejects the whole connection: a mixed answer is treated as hostile rather than
        // filtered, because the public entries may only be there to pass a check.
        if (addresses.Any(address => !DestinationPolicy.IsAddressAllowed(address)))
        {
            throw new UpstreamConnectionRejectedException(UpstreamConnectionRejection.BlockedAddress);
        }

        Exception? lastError = null;
        foreach (var address in addresses)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                return await _connector.ConnectAsync(new IPEndPoint(address, port), cancellationToken);
            }
            catch (SocketException exception)
            {
                lastError = exception;
            }
        }

        throw lastError!;
    }

    private async Task<IPAddress[]> ResolveAsync(string host, CancellationToken cancellationToken)
    {
        // Uri.IdnHost already strips IPv6 brackets; strip them here too so the connector does not depend on that.
        var literal = host.Length > 2 && host[0] == '[' && host[^1] == ']' ? host[1..^1] : host;
        if (IPAddress.TryParse(literal, out var parsed))
        {
            return [parsed];
        }

        IPAddress[] addresses;
        try
        {
            addresses = await _resolver.ResolveAsync(host, cancellationToken);
        }
        catch (SocketException exception)
        {
            throw new UpstreamConnectionRejectedException(UpstreamConnectionRejection.ResolutionFailed, exception);
        }

        var distinct = addresses.Distinct().ToArray();
        if (distinct.Length == 0)
        {
            throw new UpstreamConnectionRejectedException(UpstreamConnectionRejection.ResolutionFailed);
        }

        return distinct;
    }
}
