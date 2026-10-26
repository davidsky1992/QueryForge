using QueryForge.Api.Http;

namespace QueryForge.Api.Tests.Execution;

public sealed class BoundedBodyReaderTests
{
    private const int Limit = 16 * 1024;

    [Theory]
    [InlineData(0)]
    [InlineData(10)]
    [InlineData(Limit)]
    public async Task BodiesUpToTheLimit_AreReturned(int size)
    {
        var body = await BoundedBodyReader.ReadAsync(new UnknownLengthStream(size), declaredLength: null, Limit, CancellationToken.None);

        Assert.Equal(size, body!.Length);
    }

    [Fact]
    public async Task UndeclaredBodyOverTheLimit_StopsSoonAfterTheLimit()
    {
        // 10 MB offered without a declared length (as with a chunked request).
        var stream = new UnknownLengthStream(10 * 1024 * 1024);

        var body = await BoundedBodyReader.ReadAsync(stream, declaredLength: null, Limit, CancellationToken.None);

        Assert.Null(body);
        Assert.InRange(stream.BytesRead, Limit + 1, Limit + 4096);
    }

    [Fact]
    public async Task DeclaredLengthOverTheLimit_IsRejectedWithoutReading()
    {
        var stream = new UnknownLengthStream(Limit + 1);

        var body = await BoundedBodyReader.ReadAsync(stream, declaredLength: Limit + 1, Limit, CancellationToken.None);

        Assert.Null(body);
        Assert.Equal(0, stream.BytesRead);
    }

    [Fact]
    public async Task UnderstatedDeclaredLength_IsNotTrusted()
    {
        var stream = new UnknownLengthStream(Limit * 2);

        Assert.Null(await BoundedBodyReader.ReadAsync(stream, declaredLength: 10, Limit, CancellationToken.None));
    }

    private sealed class UnknownLengthStream(long size) : Stream
    {
        private long _remaining = size;
        public long BytesRead { get; private set; }
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
            BytesRead += read;
            return read;
        }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
