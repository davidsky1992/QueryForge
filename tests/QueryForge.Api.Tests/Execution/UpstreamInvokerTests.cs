using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using QueryForge.Api.Http;
using QueryForge.Application.Execution;
using QueryForge.Infrastructure.Execution;

namespace QueryForge.Api.Tests.Execution;

public sealed class UpstreamInvokerTests
{
    private const long MaxBytes = 1024;
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    // ---- Successful reads ----

    [Fact]
    public async Task Json_IsParsedAndUpstreamStatusKept()
    {
        var result = await SendAsync(Respond(HttpStatusCode.OK, """{"city":"Hanoi","temp":31}""", "application/json"));

        Assert.True(result.IsSuccess);
        var response = result.Value!;
        Assert.Equal(200, response.StatusCode);
        Assert.Equal("application/json", response.ContentType);
        Assert.Equal("Hanoi", response.Json!.Value.GetProperty("city").GetString());
        Assert.Null(response.Text);
        Assert.True(response.Duration > TimeSpan.Zero);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.Found)]
    public async Task UpstreamErrorStatuses_AreReportedNotTreatedAsFailures(HttpStatusCode status)
    {
        var result = await SendAsync(Respond(status, """{"title":"x"}""", "application/problem+json"));

        Assert.True(result.IsSuccess);
        Assert.Equal((int)status, result.Value!.StatusCode);
        Assert.Equal("application/problem+json", result.Value.ContentType);
        Assert.NotNull(result.Value.Json);
    }

    [Theory]
    [InlineData("text/plain; charset=utf-8")]
    [InlineData("text/plain")]
    [InlineData("text/csv; charset=\"UTF-8\"")]
    public async Task Utf8Text_IsReturnedAsText(string contentType)
    {
        var result = await SendAsync(Respond(HttpStatusCode.OK, "xin chào", contentType));

        Assert.True(result.IsSuccess);
        Assert.Equal("xin chào", result.Value!.Text);
        Assert.Null(result.Value.Json);
    }

    [Fact]
    public async Task EmptyBody_IsSuccessWithoutBody()
    {
        var result = await SendAsync(_ => new HttpResponseMessage(HttpStatusCode.NoContent));

        Assert.True(result.IsSuccess);
        Assert.Equal(204, result.Value!.StatusCode);
        Assert.Null(result.Value.Json);
        Assert.Null(result.Value.Text);
    }

    [Fact]
    public async Task BodyOfExactlyTheLimit_IsAccepted()
    {
        var text = new string('a', (int)MaxBytes);

        var result = await SendAsync(Respond(HttpStatusCode.OK, text, "text/plain"));

        Assert.True(result.IsSuccess);
        Assert.Equal(text, result.Value!.Text);
    }

    [Fact]
    public async Task IdentityContentEncoding_IsAccepted()
    {
        var result = await SendAsync(_ =>
        {
            var response = Respond(HttpStatusCode.OK, "ok", "text/plain")(null!);
            response.Content.Headers.ContentEncoding.Add("identity");
            return response;
        });

        Assert.True(result.IsSuccess);
    }

    // ---- Content type and encoding ----

    [Theory]
    [InlineData("application/octet-stream")]
    [InlineData("image/png")]
    [InlineData("application/xml")]
    [InlineData("application/json; charset=iso-8859-1")]
    [InlineData("text/plain; charset=windows-1252")]
    [InlineData("multipart/form-data; boundary=x")]
    public async Task UnsupportedContentTypes_AreRejected(string contentType)
    {
        var result = await SendAsync(Respond(HttpStatusCode.OK, "data", contentType));

        AssertError(result, UpstreamErrors.UnsupportedContentType);
    }

    [Fact]
    public async Task MissingContentType_WithBody_IsRejected()
    {
        var result = await SendAsync(_ =>
        {
            var content = new ByteArrayContent("data"u8.ToArray());
            content.Headers.ContentType = null;
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
        });

        AssertError(result, UpstreamErrors.UnsupportedContentType);
    }

    [Theory]
    [InlineData("gzip")]
    [InlineData("br")]
    [InlineData("deflate")]
    public async Task CompressedBodies_AreRejected(string encoding)
    {
        var result = await SendAsync(_ =>
        {
            // Valid JSON, so only the encoding can cause the rejection.
            var response = Respond(HttpStatusCode.OK, """{"ok":true}""", "application/json")(null!);
            response.Content.Headers.ContentEncoding.Add(encoding);
            return response;
        });

        AssertError(result, UpstreamErrors.InvalidResponse);
    }

    // ---- Malformed bodies ----

    [Theory]
    [InlineData("{")]
    [InlineData("{\"a\":}")]
    [InlineData("not json")]
    public async Task MalformedJson_IsInvalidResponse(string body)
    {
        var result = await SendAsync(Respond(HttpStatusCode.OK, body, "application/json"));

        AssertError(result, UpstreamErrors.InvalidResponse);
    }

    [Fact]
    public async Task DeeplyNestedJson_IsInvalidResponse()
    {
        var body = new string('[', 65) + new string(']', 65);

        var result = await SendAsync(Respond(HttpStatusCode.OK, body, "application/json"));

        AssertError(result, UpstreamErrors.InvalidResponse);
    }

    [Fact]
    public async Task InvalidUtf8Text_IsInvalidResponse()
    {
        var result = await SendAsync(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = WithType(new ByteArrayContent([0x48, 0xC3, 0x28, 0x49]), "text/plain")
        });

        AssertError(result, UpstreamErrors.InvalidResponse);
    }

    // ---- Size limits ----

    [Fact]
    public async Task DeclaredLengthOverLimit_IsRejectedWithoutReadingTheBody()
    {
        var body = new CountingStream(new ThrowingStream());
        var result = await SendAsync(_ =>
        {
            var content = WithType(new StreamContent(body), "application/json");
            content.Headers.ContentLength = MaxBytes + 1;
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
        });

        AssertError(result, UpstreamErrors.ResponseTooLarge);
        Assert.Equal(0, body.BytesRead);
    }

    [Fact]
    public async Task UndeclaredLengthOverLimit_StopsReadingSoonAfterTheLimit()
    {
        // 10 MB on offer without a Content-Length; the reader must give up after the limit plus one chunk.
        var body = new CountingStream(new EndlessStream(10 * 1024 * 1024));
        var result = await SendAsync(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = WithType(new StreamContent(body), "text/plain")
        });

        AssertError(result, UpstreamErrors.ResponseTooLarge);
        Assert.InRange(body.BytesRead, MaxBytes + 1, MaxBytes + 16 * 1024);
    }

    // ---- Timeouts and cancellation ----

    [Fact]
    public async Task NoResponseWithinTimeout_IsTimeout()
    {
        var stopwatch = Stopwatch.StartNew();

        // WaitAsync makes a regression fail fast instead of hanging the test run.
        var result = await SendAsync(async (_, token) =>
        {
            await Task.Delay(System.Threading.Timeout.Infinite, token);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }, timeout: TimeSpan.FromMilliseconds(200)).WaitAsync(TimeSpan.FromSeconds(10));

        AssertError(result, UpstreamErrors.Timeout);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(4), $"Took {stopwatch.Elapsed}.");
    }

    [Fact]
    public async Task SlowDripBody_IsTimeout()
    {
        // Headers arrive at once; the body trickles one byte per 100 ms and would take minutes to finish.
        var result = await SendAsync(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = WithType(new StreamContent(new SlowStream(TimeSpan.FromMilliseconds(100))), "text/plain")
        }, timeout: TimeSpan.FromMilliseconds(300)).WaitAsync(TimeSpan.FromSeconds(10));

        AssertError(result, UpstreamErrors.Timeout);
    }

    [Fact]
    public async Task ClientCancellationBeforeResponse_PropagatesInsteadOfBecomingAnUpstreamError()
    {
        using var client = new CancellationTokenSource();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => SendAsync(async (_, token) =>
        {
            client.Cancel();
            await Task.Delay(System.Threading.Timeout.Infinite, token);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }, cancellationToken: client.Token));
    }

    [Fact]
    public async Task ClientCancellationWhileReadingBody_Propagates()
    {
        using var client = new CancellationTokenSource();
        var body = new SlowStream(TimeSpan.FromMilliseconds(50), onRead: client.Cancel);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => SendAsync(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = WithType(new StreamContent(body), "text/plain")
        }, cancellationToken: client.Token));
    }

    // ---- Connection failures ----

    [Theory]
    [InlineData(UpstreamConnectionRejection.BlockedAddress, UpstreamErrors.DestinationBlocked)]
    [InlineData(UpstreamConnectionRejection.BlockedPort, UpstreamErrors.DestinationBlocked)]
    [InlineData(UpstreamConnectionRejection.ResolutionFailed, UpstreamErrors.Unreachable)]
    public async Task ConnectionRejections_MapToTheirCodes(UpstreamConnectionRejection reason, string expected)
    {
        var result = await SendAsync(_ => throw new HttpRequestException(
            HttpRequestError.ConnectionError, "wrapped", new UpstreamConnectionRejectedException(reason)));

        AssertError(result, expected);
    }

    [Theory]
    [InlineData(HttpRequestError.ConnectionError, UpstreamErrors.Unreachable)]
    [InlineData(HttpRequestError.NameResolutionError, UpstreamErrors.Unreachable)]
    [InlineData(HttpRequestError.SecureConnectionError, UpstreamErrors.Unreachable)]
    [InlineData(HttpRequestError.Unknown, UpstreamErrors.Unreachable)]
    [InlineData(HttpRequestError.InvalidResponse, UpstreamErrors.InvalidResponse)]
    [InlineData(HttpRequestError.ResponseEnded, UpstreamErrors.InvalidResponse)]
    [InlineData(HttpRequestError.HttpProtocolError, UpstreamErrors.InvalidResponse)]
    [InlineData(HttpRequestError.ConfigurationLimitExceeded, UpstreamErrors.InvalidResponse)]
    public async Task HttpRequestErrors_MapToTheirCodes(HttpRequestError error, string expected)
    {
        var result = await SendAsync(_ => throw new HttpRequestException(error, "failure"));

        AssertError(result, expected);
    }

    [Fact]
    public async Task ErrorMessages_DoNotRevealUpstreamDetails()
    {
        var result = await SendAsync(_ => throw new HttpRequestException(
            HttpRequestError.ConnectionError, "No connection to secret-internal.example.test (10.1.2.3)"));

        Assert.DoesNotContain("secret-internal", result.ErrorMessage!);
        Assert.DoesNotContain("10.1.2.3", result.ErrorMessage!);
    }

    // ---- Real sockets through the hardened handler ----

    [Fact]
    public async Task RealSocket_MalformedStatusLine_IsInvalidResponse()
    {
        await using var server = LoopbackServer.StartIPv4();
        server.EnqueueRaw("this is not http\r\n\r\n"u8.ToArray());

        var result = await SendThroughHandlerAsync(new PublicUpstreamSimulator(server));

        AssertError(result, UpstreamErrors.InvalidResponse);
    }

    [Fact]
    public async Task RealSocket_BodyShorterThanDeclared_IsInvalidResponse()
    {
        await using var server = LoopbackServer.StartIPv4();
        server.EnqueueRaw("HTTP/1.1 200 OK\r\nContent-Type: text/plain\r\nContent-Length: 100\r\nConnection: close\r\n\r\nonly a few bytes"u8.ToArray());

        var result = await SendThroughHandlerAsync(new PublicUpstreamSimulator(server));

        AssertError(result, UpstreamErrors.InvalidResponse);
    }

    [Fact]
    public async Task RealSocket_OversizedHeaders_AreInvalidResponse()
    {
        await using var server = LoopbackServer.StartIPv4();
        server.EnqueueResponse($"HTTP/1.1 200 OK\r\nContent-Type: text/plain\r\nX-Padding: {new string('a', 40 * 1024)}", "ok"u8.ToArray());

        var result = await SendThroughHandlerAsync(new PublicUpstreamSimulator(server));

        AssertError(result, UpstreamErrors.InvalidResponse);
    }

    [Fact]
    public async Task RealSocket_ConnectionRefused_IsUnreachable()
    {
        // Reserve a loopback port, then close it so nothing is listening there.
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var closedEndPoint = (IPEndPoint)listener.LocalEndpoint;
        listener.Stop();

        var result = await SendThroughHandlerAsync(new PublicUpstreamSimulator(closedEndPoint));

        AssertError(result, UpstreamErrors.Unreachable);
    }

    [Fact]
    public async Task RealSocket_BlockedDestination_IsDestinationBlocked()
    {
        await using var server = LoopbackServer.StartIPv4();
        var connector = new GuardedConnector(new DestinationPolicy([server.Port]), new SystemDnsResolver(), new SystemSocketConnector());
        using var client = CreateHardenedClient(connector);
        using var request = new HttpRequestMessage(HttpMethod.Get, $"http://127.0.0.1:{server.Port}/");

        var result = await UpstreamInvoker.SendAsync(client, request, Timeout, MaxBytes, CancellationToken.None);

        AssertError(result, UpstreamErrors.DestinationBlocked);
        Assert.Equal(0, server.AcceptedConnections);
    }

    // ---- Arguments and status mapping ----

    [Fact]
    public async Task InvalidLimits_AreRejected()
    {
        using var client = new HttpClient(new StubHandler((_, _) => Task.FromResult(new HttpResponseMessage())));
        using var request = new HttpRequestMessage(HttpMethod.Get, "http://public.example.test/");

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => UpstreamInvoker.SendAsync(client, request, TimeSpan.Zero, MaxBytes, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => UpstreamInvoker.SendAsync(client, request, Timeout, 0, CancellationToken.None));
    }

    [Theory]
    [InlineData(UpstreamErrors.DestinationBlocked, 502)]
    [InlineData(UpstreamErrors.Unreachable, 502)]
    [InlineData(UpstreamErrors.ResponseTooLarge, 502)]
    [InlineData(UpstreamErrors.UnsupportedContentType, 502)]
    [InlineData(UpstreamErrors.InvalidResponse, 502)]
    [InlineData(UpstreamErrors.Timeout, 504)]
    [InlineData(UpstreamErrors.CapacityExceeded, 503)]
    public void ErrorCodes_MapToGatewayStatuses(string code, int status)
    {
        Assert.Equal(status, UpstreamErrorStatusCodes.For(code));
    }

    [Fact]
    public void UnknownErrorCode_IsNotSilentlyMapped()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => UpstreamErrorStatusCodes.For("not_found"));
    }

    // ---- Helpers ----

    private static Task<QueryForge.Application.Common.Result<UpstreamResponse>> SendAsync(
        Func<HttpRequestMessage, HttpResponseMessage> respond,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default) =>
        SendAsync((request, _) => Task.FromResult(respond(request)), timeout, cancellationToken);

    private static async Task<QueryForge.Application.Common.Result<UpstreamResponse>> SendAsync(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        using var client = new HttpClient(new StubHandler(respond)) { Timeout = System.Threading.Timeout.InfiniteTimeSpan };
        using var request = new HttpRequestMessage(HttpMethod.Get, "http://public.example.test/");
        return await UpstreamInvoker.SendAsync(client, request, timeout ?? Timeout, MaxBytes, cancellationToken);
    }

    private static async Task<QueryForge.Application.Common.Result<UpstreamResponse>> SendThroughHandlerAsync(PublicUpstreamSimulator simulator)
    {
        using var client = CreateHardenedClient(new GuardedConnector(new DestinationPolicy(), simulator, simulator));
        using var request = new HttpRequestMessage(HttpMethod.Get, "http://public.example.test/");
        return await UpstreamInvoker.SendAsync(client, request, Timeout, MaxBytes, CancellationToken.None);
    }

    private static HttpClient CreateHardenedClient(GuardedConnector connector) =>
        new(new UpstreamHttpVersionHandler { InnerHandler = UpstreamHandlerFactory.CreatePrimaryHandler(connector) })
        {
            Timeout = System.Threading.Timeout.InfiniteTimeSpan
        };

    private static Func<HttpRequestMessage, HttpResponseMessage> Respond(HttpStatusCode status, string body, string contentType) =>
        _ => new HttpResponseMessage(status) { Content = WithType(new ByteArrayContent(Encoding.UTF8.GetBytes(body)), contentType) };

    private static HttpContent WithType(HttpContent content, string contentType)
    {
        content.Headers.TryAddWithoutValidation("Content-Type", contentType);
        return content;
    }

    private static void AssertError(QueryForge.Application.Common.Result<UpstreamResponse> result, string expectedCode)
    {
        Assert.False(result.IsSuccess, $"Expected {expectedCode} but the call succeeded.");
        Assert.Equal(expectedCode, result.ErrorCode);
        Assert.False(string.IsNullOrWhiteSpace(result.ErrorMessage));
    }

    private sealed class StubHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            respond(request, cancellationToken);
    }

    private sealed class CountingStream(Stream inner) : Stream
    {
        public long BytesRead { get; private set; }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => Count(inner.Read(buffer, offset, count));
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            Count(await inner.ReadAsync(buffer, cancellationToken));
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        private int Count(int read)
        {
            BytesRead += read;
            return read;
        }
    }

    private sealed class EndlessStream(long length) : Stream
    {
        private long _remaining = length;
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count)
        {
            var read = (int)Math.Min(count, _remaining);
            buffer.AsSpan(offset, read).Fill((byte)'a');
            _remaining -= read;
            return read;
        }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private sealed class ThrowingStream : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new InvalidOperationException("The body must not be read.");
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private sealed class SlowStream(TimeSpan delayPerByte, Action? onRead = null) : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            onRead?.Invoke();
            await Task.Delay(delayPerByte, cancellationToken);
            buffer.Span[0] = (byte)'a';
            return 1;
        }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
