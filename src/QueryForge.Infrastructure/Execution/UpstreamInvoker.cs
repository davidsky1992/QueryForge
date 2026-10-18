using System.Diagnostics;
using System.Net.Http;
using QueryForge.Application.Common;
using QueryForge.Application.Execution;

namespace QueryForge.Infrastructure.Execution;

/// <summary>
/// Sends one upstream request under a per-call timeout and turns every outcome into either a fully read
/// <see cref="UpstreamResponse"/> or an upstream error code. Cancellation by the caller (client disconnect) is not an
/// upstream error: it propagates as <see cref="OperationCanceledException"/> and is reported as 499 by the API.
/// </summary>
public static class UpstreamInvoker
{
    public static async Task<Result<UpstreamResponse>> SendAsync(
        HttpClient client,
        HttpRequestMessage request,
        TimeSpan timeout,
        long maxResponseBytes,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(timeout, TimeSpan.Zero);

        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);
        var stopwatch = Stopwatch.StartNew();

        try
        {
            // Headers only; the body is read by the bounded reader, under the same timeout.
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeoutSource.Token);
            var result = await UpstreamResponseReader.ReadAsync(response, maxResponseBytes, timeoutSource.Token);

            return result.IsSuccess
                ? Result<UpstreamResponse>.Success(result.Value! with { Duration = stopwatch.Elapsed })
                : result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            // The per-call timeout (or HttpClient.Timeout) fired while the caller was still waiting.
            return Failure(UpstreamErrors.Timeout, "The upstream did not respond within the configured time.");
        }
        catch (Exception exception) when (FindRejection(exception) is { } rejection)
        {
            return rejection.Reason == UpstreamConnectionRejection.ResolutionFailed
                ? Failure(UpstreamErrors.Unreachable, "The upstream could not be reached.")
                : Failure(UpstreamErrors.DestinationBlocked, "The upstream destination is not allowed.");
        }
        catch (HttpRequestException exception) when (IsProtocolError(exception.HttpRequestError))
        {
            return Failure(UpstreamErrors.InvalidResponse, "The upstream returned a malformed response.");
        }
        catch (HttpRequestException)
        {
            return Failure(UpstreamErrors.Unreachable, "The upstream could not be reached.");
        }
        catch (IOException)
        {
            // The connection broke or the body ended early while it was being read.
            return Failure(UpstreamErrors.InvalidResponse, "The upstream returned a malformed response.");
        }
    }

    private static bool IsProtocolError(HttpRequestError error) => error is
        HttpRequestError.InvalidResponse or
        HttpRequestError.ResponseEnded or
        HttpRequestError.HttpProtocolError or
        HttpRequestError.ConfigurationLimitExceeded;

    private static UpstreamConnectionRejectedException? FindRejection(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is UpstreamConnectionRejectedException rejection)
            {
                return rejection;
            }
        }

        return null;
    }

    private static Result<UpstreamResponse> Failure(string code, string message) =>
        Result<UpstreamResponse>.Failure(code, message);
}
