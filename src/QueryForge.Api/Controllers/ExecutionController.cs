using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Net.Http.Headers;
using QueryForge.Api.Execution;
using QueryForge.Api.Http;
using QueryForge.Api.RateLimiting;
using QueryForge.Api.Security;
using QueryForge.Application.Execution;

namespace QueryForge.Api.Controllers;

/// <summary>
/// Executes a registered operation. Only exists when <c>Execution:Enabled</c> is true. Callers supply parameter
/// values only; the upstream URL, method, headers and authentication come from the registration.
/// </summary>
[ApiController]
[Route("api/execute")]
[Authorize(Policy = AuthorizationPolicies.ExecuteOperations)]
[EnableRateLimiting(RateLimitPolicies.Execute)]
[ProducesResponseType(StatusCodes.Status401Unauthorized)]
[ProducesResponseType(StatusCodes.Status403Forbidden)]
[ProducesResponseType(StatusCodes.Status429TooManyRequests)]
public sealed class ExecutionController : ControllerBase
{
    private static readonly JsonSerializerOptions RequestJsonOptions = new(JsonSerializerDefaults.Web) { MaxDepth = 8 };

    private readonly IExecutionService _executionService;
    private readonly ExecutionConcurrencyLimiter _concurrencyLimiter;

    public ExecutionController(IExecutionService executionService, ExecutionConcurrencyLimiter concurrencyLimiter)
    {
        _executionService = executionService;
        _concurrencyLimiter = concurrencyLimiter;
    }

    [HttpPost("{definitionSlug:maxlength(180)}/{operationKey:maxlength(64)}")]
    [RequestSizeLimit(ExecutionOptions.MaxRequestBodyBytes)]
    [ProducesResponseType(typeof(ExecutionResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status413PayloadTooLarge)]
    [ProducesResponseType(StatusCodes.Status415UnsupportedMediaType)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    [ProducesResponseType(StatusCodes.Status504GatewayTimeout)]
    public async Task<IActionResult> Execute(string definitionSlug, string operationKey, CancellationToken cancellationToken)
    {
        var body = await BoundedBodyReader.ReadAsync(Request.Body, Request.ContentLength, ExecutionOptions.MaxRequestBodyBytes, cancellationToken);
        if (body is null)
        {
            return Problem(StatusCodes.Status413PayloadTooLarge, "Request too large",
                $"The request body must be {ExecutionOptions.MaxRequestBodyBytes} bytes or smaller.", ExecutionErrors.RequestTooLarge);
        }

        ExecuteOperationRequest request;
        if (body.Length == 0)
        {
            request = new ExecuteOperationRequest(null);
        }
        else
        {
            if (!MediaTypeHeaderValue.TryParse(Request.ContentType, out var contentType) ||
                !contentType.MediaType.Equals("application/json", StringComparison.OrdinalIgnoreCase))
            {
                return Problem(StatusCodes.Status415UnsupportedMediaType, "Unsupported media type", "The request body must be application/json.", null);
            }

            try
            {
                request = JsonSerializer.Deserialize<ExecuteOperationRequest>(body, RequestJsonOptions) ?? new ExecuteOperationRequest(null);
            }
            catch (JsonException)
            {
                // The parser message can quote the input, so a fixed message is returned instead.
                return Problem(StatusCodes.Status400BadRequest, "Malformed request", "The request body is not valid JSON of the form {\"parameters\":{...}}.", null);
            }
        }

        if (!_concurrencyLimiter.TryAcquire())
        {
            Response.Headers.RetryAfter = "1";
            return this.ToFailureResult(UpstreamErrors.CapacityExceeded, "Too many executions are in progress. Retry shortly.", []);
        }

        try
        {
            var result = await _executionService.ExecuteAsync(definitionSlug, operationKey, request, cancellationToken);
            return result.IsSuccess ? Ok(result.Value) : this.ToFailureResult(result);
        }
        finally
        {
            _concurrencyLimiter.Release();
        }
    }

    private ObjectResult Problem(int status, string title, string detail, string? code)
    {
        var problem = new ProblemDetails { Status = status, Title = title, Detail = detail };
        if (code is not null)
        {
            problem.Extensions["code"] = code;
        }

        return new ObjectResult(problem) { StatusCode = status, ContentTypes = { "application/problem+json" } };
    }
}
