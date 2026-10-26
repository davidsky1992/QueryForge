namespace QueryForge.Api.Http;

/// <summary>Reads a request body without ever holding more than the limit plus one chunk.</summary>
public static class BoundedBodyReader
{
    private const int ChunkSize = 4096;

    /// <summary>Returns the body, or null as soon as it is (or is declared to be) larger than <paramref name="maxBytes"/>.</summary>
    public static async Task<byte[]?> ReadAsync(Stream body, long? declaredLength, int maxBytes, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);

        if (declaredLength > maxBytes)
        {
            return null;
        }

        // The declared length is not trusted: chunked bodies have none, and the count below is what is enforced.
        using var buffer = new MemoryStream();
        var chunk = new byte[ChunkSize];
        int read;
        while ((read = await body.ReadAsync(chunk, cancellationToken)) > 0)
        {
            if (buffer.Length + read > maxBytes)
            {
                return null;
            }

            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }
}
