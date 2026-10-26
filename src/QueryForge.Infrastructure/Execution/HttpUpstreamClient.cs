using System.Net.Http.Headers;
using QueryForge.Application.Common;
using QueryForge.Application.Execution;

namespace QueryForge.Infrastructure.Execution;

/// <summary>
/// Sends upstream requests through the hardened handler chain. The request carries only headers QueryForge sets
/// itself: nothing from the incoming request (API key, cookies, forwarding headers) is ever copied to it.
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

        return await UpstreamInvoker.SendAsync(_httpClient, message, request.Timeout, request.MaxResponseBytes, cancellationToken);
    }
}
