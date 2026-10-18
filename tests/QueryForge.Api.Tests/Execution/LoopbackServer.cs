using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace QueryForge.Api.Tests.Execution;

/// <summary>
/// Minimal TCP server on a loopback address for real-socket tests. Counts accepted connections and, per connection,
/// reads one HTTP request head, records it, writes the next scripted response and closes.
/// </summary>
internal sealed class LoopbackServer : IAsyncDisposable
{
    private readonly TcpListener _listener;
    private readonly CancellationTokenSource _stop = new();
    private readonly ConcurrentQueue<byte[]> _responses = new();
    private readonly Task _acceptLoop;
    private int _acceptedConnections;

    private LoopbackServer(IPAddress address)
    {
        _listener = new TcpListener(address, 0);
        _listener.Start();
        _acceptLoop = AcceptLoopAsync();
    }

    public static LoopbackServer StartIPv4() => new(IPAddress.Loopback);

    public static LoopbackServer StartIPv6() => new(IPAddress.IPv6Loopback);

    public IPEndPoint EndPoint => (IPEndPoint)_listener.LocalEndpoint;

    public int Port => EndPoint.Port;

    public int AcceptedConnections => Volatile.Read(ref _acceptedConnections);

    public ConcurrentQueue<string> Requests { get; } = new();

    public void EnqueueResponse(string head, byte[]? body = null)
    {
        body ??= [];
        var bytes = Encoding.ASCII.GetBytes(head.TrimEnd('\r', '\n') + $"\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n");
        _responses.Enqueue([.. bytes, .. body]);
    }

    private async Task AcceptLoopAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await _listener.AcceptTcpClientAsync(_stop.Token);
            }
            catch (Exception) when (_stop.IsCancellationRequested)
            {
                return;
            }

            Interlocked.Increment(ref _acceptedConnections);
            _ = ServeAsync(client);
        }
    }

    private async Task ServeAsync(TcpClient client)
    {
        using (client)
        {
            try
            {
                var stream = client.GetStream();
                var head = await ReadHeadAsync(stream, _stop.Token);
                Requests.Enqueue(head);
                if (_responses.TryDequeue(out var response))
                {
                    await stream.WriteAsync(response, _stop.Token);
                }
            }
            catch (Exception) when (_stop.IsCancellationRequested)
            {
            }
            catch (IOException)
            {
            }
        }
    }

    private static async Task<string> ReadHeadAsync(Stream stream, CancellationToken cancellationToken)
    {
        var buffer = new List<byte>();
        var chunk = new byte[1024];
        while (buffer.Count < 64 * 1024)
        {
            var read = await stream.ReadAsync(chunk, cancellationToken);
            if (read == 0)
            {
                break;
            }

            buffer.AddRange(chunk.AsSpan(0, read).ToArray());
            var text = Encoding.ASCII.GetString(buffer.ToArray());
            var end = text.IndexOf("\r\n\r\n", StringComparison.Ordinal);
            if (end >= 0)
            {
                return text[..end];
            }
        }

        return Encoding.ASCII.GetString(buffer.ToArray());
    }

    public async ValueTask DisposeAsync()
    {
        _stop.Cancel();
        _listener.Stop();
        try
        {
            await _acceptLoop;
        }
        catch (Exception)
        {
        }

        _stop.Dispose();
    }
}
