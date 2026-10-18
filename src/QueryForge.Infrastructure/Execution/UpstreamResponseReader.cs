using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using QueryForge.Application.Common;
using QueryForge.Application.Execution;

namespace QueryForge.Infrastructure.Execution;

/// <summary>
/// Reads an upstream response body without ever buffering more than the configured limit, and accepts only JSON
/// (UTF-8) and UTF-8 text. Everything else is reported as an error code; no upstream header except the media type
/// is kept.
/// </summary>
public static class UpstreamResponseReader
{
    private const int ChunkSize = 16 * 1024;
    private const int MaxJsonDepth = 64;
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    public static async Task<Result<UpstreamResponse>> ReadAsync(HttpResponseMessage response, long maxBytes, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(response);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxBytes, 1);

        var headers = response.Content.Headers;
        var statusCode = (int)response.StatusCode;

        if (headers.ContentEncoding.Any(encoding => !encoding.Equals("identity", StringComparison.OrdinalIgnoreCase)))
        {
            return Failure(UpstreamErrors.InvalidResponse, "The upstream response used an unsupported content encoding.");
        }

        // A declared length over the limit is rejected before reading anything.
        if (headers.ContentLength > maxBytes)
        {
            return TooLarge();
        }

        var body = await ReadBoundedAsync(response.Content, maxBytes, cancellationToken);
        if (body is null)
        {
            return TooLarge();
        }

        var mediaType = headers.ContentType?.MediaType?.ToLowerInvariant();
        if (body.Length == 0)
        {
            return Result<UpstreamResponse>.Success(new UpstreamResponse(statusCode, mediaType, null, null, TimeSpan.Zero));
        }

        if (mediaType is null || !IsUtf8(headers.ContentType!))
        {
            return Unsupported();
        }

        if (IsJson(mediaType))
        {
            try
            {
                using var document = JsonDocument.Parse(body, new JsonDocumentOptions { MaxDepth = MaxJsonDepth });
                return Result<UpstreamResponse>.Success(
                    new UpstreamResponse(statusCode, mediaType, document.RootElement.Clone(), null, TimeSpan.Zero));
            }
            catch (JsonException)
            {
                return Failure(UpstreamErrors.InvalidResponse, "The upstream response is not valid JSON.");
            }
        }

        if (mediaType.StartsWith("text/", StringComparison.Ordinal))
        {
            try
            {
                return Result<UpstreamResponse>.Success(
                    new UpstreamResponse(statusCode, mediaType, null, StrictUtf8.GetString(body), TimeSpan.Zero));
            }
            catch (DecoderFallbackException)
            {
                return Failure(UpstreamErrors.InvalidResponse, "The upstream response is not valid UTF-8 text.");
            }
        }

        return Unsupported();
    }

    // Returns null as soon as the body exceeds maxBytes, so at most maxBytes plus one chunk is ever held.
    private static async Task<byte[]?> ReadBoundedAsync(HttpContent content, long maxBytes, CancellationToken cancellationToken)
    {
        await using var stream = await content.ReadAsStreamAsync(cancellationToken);
        using var buffer = new MemoryStream();
        var chunk = new byte[ChunkSize];
        while (true)
        {
            var read = await stream.ReadAsync(chunk, cancellationToken);
            if (read == 0)
            {
                return buffer.ToArray();
            }

            if (buffer.Length + read > maxBytes)
            {
                return null;
            }

            buffer.Write(chunk, 0, read);
        }
    }

    private static bool IsJson(string mediaType) =>
        mediaType == "application/json" ||
        (mediaType.StartsWith("application/", StringComparison.Ordinal) && mediaType.EndsWith("+json", StringComparison.Ordinal));

    private static bool IsUtf8(MediaTypeHeaderValue contentType)
    {
        var charset = contentType.CharSet?.Trim().Trim('"');
        return string.IsNullOrEmpty(charset) ||
            charset.Equals("utf-8", StringComparison.OrdinalIgnoreCase) ||
            charset.Equals("utf8", StringComparison.OrdinalIgnoreCase) ||
            charset.Equals("us-ascii", StringComparison.OrdinalIgnoreCase);
    }

    private static Result<UpstreamResponse> TooLarge() =>
        Failure(UpstreamErrors.ResponseTooLarge, "The upstream response exceeded the configured size limit.");

    private static Result<UpstreamResponse> Unsupported() =>
        Failure(UpstreamErrors.UnsupportedContentType, "The upstream response has an unsupported content type.");

    private static Result<UpstreamResponse> Failure(string code, string message) =>
        Result<UpstreamResponse>.Failure(code, message);
}
