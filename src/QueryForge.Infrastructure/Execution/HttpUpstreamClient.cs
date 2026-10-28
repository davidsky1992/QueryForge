using System.Net.Http.Headers;
using QueryForge.Application.Common;
using QueryForge.Application.Execution;

namespace QueryForge.Infrastructure.Execution;

/// <summary>
/// Sends upstream requests through the hardened handler chain. The request carries only headers QueryForge sets
/// itself, plus the definition's API key header when configured: nothing from the incoming request (API key,
/// cookies, forwarding headers) is ever copied to it.
/// </summary>
public sealed class HttpUpstreamClient : IUpstreamClient
{
    public const string UserAgent = "QueryForge/1.0";

    private readonly HttpClient _httpClient;

    public HttpUpstreamClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<Result<UpstreamResponse>> SendAsync(UpstreamRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        using var message = new HttpRequestMessage(HttpMethod.Get, request.Url);
        message.Headers.UserAgent.ParseAdd(UserAgent);
        message.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        message.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/*", 0.5));

        // The header name was validated at registration and the value at startup (no control characters); the
        // value is added as-is so keys in Authorization or vendor formats are not reinterpreted.
        if (request.Credential is { } credential)
        {
            message.Headers.TryAddWithoutValidation(credential.HeaderName, credential.Value);
        }

        return await UpstreamInvoker.SendAsync(_httpClient, message, request.Timeout, request.MaxResponseBytes, cancellationToken);
    }
}
