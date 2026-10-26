using System.Text.Json;
using QueryForge.Application.ApiOperations;
using QueryForge.Application.Auditing;
using QueryForge.Application.Common;
using QueryForge.Domain.ApiDefinitions;
using QueryForge.Domain.Auditing;

namespace QueryForge.Application.Execution;

public interface IExecutionService
{
    Task<Result<ExecutionResultDto>> ExecuteAsync(string definitionSlug, string operationKey, ExecuteOperationRequest request, CancellationToken cancellationToken);
}

/// <summary>
/// Executes a registered operation: checks it may run, binds only declared parameters, builds the URL under the
/// definition's BaseUrl, calls the upstream through <see cref="IUpstreamClient"/> and returns a fixed envelope.
/// Every attempt on an existing operation is audited with outcome and timing, never with parameter values or bodies.
/// </summary>
public sealed class ExecutionService : IExecutionService
{
    private readonly IApiOperationRepository _repository;
    private readonly IUpstreamClient _upstreamClient;
    private readonly IAuditLog _auditLog;
    private readonly IRequestContext _requestContext;
    private readonly ExecutionOptions _options;
    private readonly TimeProvider _timeProvider;

    public ExecutionService(
        IApiOperationRepository repository,
        IUpstreamClient upstreamClient,
        IAuditLog auditLog,
        IRequestContext requestContext,
        ExecutionOptions options,
        TimeProvider timeProvider)
    {
        _repository = repository;
        _upstreamClient = upstreamClient;
        _auditLog = auditLog;
        _requestContext = requestContext;
        _options = options;
        _timeProvider = timeProvider;
    }

    public async Task<Result<ExecutionResultDto>> ExecuteAsync(
        string definitionSlug,
        string operationKey,
        ExecuteOperationRequest request,
        CancellationToken cancellationToken)
    {
        var target = await _repository.FindForExecutionAsync(
            (definitionSlug ?? string.Empty).Trim().ToLowerInvariant(),
            (operationKey ?? string.Empty).Trim().ToLowerInvariant(),
            cancellationToken);

        if (target is null)
        {
            return Result<ExecutionResultDto>.Failure(Errors.NotFound, "The operation was not found.");
        }

        var (definition, operation) = target;

        if (definition.Status != ApiDefinitionStatus.Active || !operation.IsEnabled)
        {
            return await RejectAsync(operation.Id, ExecutionErrors.OperationDisabled,
                "The operation is disabled or its API definition is not active.");
        }

        if (definition.AuthenticationType != ApiAuthenticationType.None)
        {
            return await RejectAsync(operation.Id, ExecutionErrors.UpstreamAuthNotSupported,
                "Upstream authentication for this API definition is not supported yet.");
        }

        var issues = ParameterBinder.Bind(operation, request?.Parameters, out var values);
        if (issues.Count > 0)
        {
            await AuditAsync(operation.Id, AuditOutcome.Rejected, Errors.Validation);
            return Result<ExecutionResultDto>.Invalid(issues);
        }

        var url = UpstreamUrlBuilder.TryBuild(definition.BaseUrl, operation, values, _options);
        if (url is null)
        {
            return await RejectAsync(operation.Id, UpstreamErrors.DestinationBlocked,
                "The API definition's base URL is not allowed for execution.");
        }

        Result<UpstreamResponse> outcome;
        try
        {
            outcome = await _upstreamClient.SendAsync(
                new UpstreamRequest(url, TimeSpan.FromSeconds(operation.TimeoutSeconds), operation.MaxResponseBytes),
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The client went away; record that the call was abandoned, then let the API report 499.
            await AuditAsync(operation.Id, AuditOutcome.Failed, "client_cancelled");
            throw;
        }

        if (!outcome.IsSuccess)
        {
            await AuditAsync(operation.Id, AuditOutcome.Failed, outcome.ErrorCode);
            return Result<ExecutionResultDto>.Failure(outcome.ErrorCode!, outcome.ErrorMessage!);
        }

        var response = outcome.Value!;
        var durationMs = (long)response.Duration.TotalMilliseconds;
        await AuditAsync(operation.Id, AuditOutcome.Succeeded, errorCode: null, response.StatusCode, durationMs);

        JsonElement? body = response.Json ?? (response.Text is null ? null : JsonSerializer.SerializeToElement(response.Text));
        return Result<ExecutionResultDto>.Success(new ExecutionResultDto(response.StatusCode, response.ContentType, body, durationMs));
    }

    private async Task<Result<ExecutionResultDto>> RejectAsync(Guid operationId, string errorCode, string message)
    {
        await AuditAsync(operationId, AuditOutcome.Rejected, errorCode);
        return Result<ExecutionResultDto>.Failure(errorCode, message);
    }

    // Not cancellable: the attempt has happened and must be recorded even if the client disconnects.
    private Task AuditAsync(Guid operationId, AuditOutcome outcome, string? errorCode, int? upstreamStatus = null, long? durationMs = null)
    {
        return _auditLog.WriteAsync(
            AuditEvent.Record(
                _timeProvider.GetUtcNow(),
                _requestContext.Actor,
                AuditActions.OperationExecuted,
                AuditTargets.ApiOperation,
                operationId.ToString(),
                outcome,
                upstreamStatus,
                durationMs,
                errorCode,
                _requestContext.TraceId),
            CancellationToken.None);
    }
}
