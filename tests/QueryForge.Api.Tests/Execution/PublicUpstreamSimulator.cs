using System.Net;
using QueryForge.Infrastructure.Execution;

namespace QueryForge.Api.Tests.Execution;

/// <summary>
/// Resolves every name to a public address (which passes the policy) and opens the socket for that checked endpoint
/// to a loopback endpoint instead, recording the endpoint the guarded connector asked for. Keeps tests off the internet.
/// </summary>
internal sealed class PublicUpstreamSimulator(IPEndPoint target) : IDnsResolver, ISocketConnector
{
    public static readonly IPAddress PublicAddress = IPAddress.Parse("93.184.215.14");

    public PublicUpstreamSimulator(LoopbackServer server)
        : this(server.EndPoint)
    {
    }

    public List<IPEndPoint> Attempts { get; } = [];

    public Task<IPAddress[]> ResolveAsync(string host, CancellationToken cancellationToken) =>
        Task.FromResult(new[] { PublicAddress });

    public Task<Stream> ConnectAsync(IPEndPoint endPoint, CancellationToken cancellationToken)
    {
        lock (Attempts)
        {
            Attempts.Add(endPoint);
        }

        return new SystemSocketConnector().ConnectAsync(target, cancellationToken);
    }
}
