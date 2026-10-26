using QueryForge.Application.Auditing;
using QueryForge.Application.Common;
using QueryForge.Domain.ApiOperations;
using QueryForge.Domain.Auditing;

namespace QueryForge.Application.ApiOperations;

public interface IApiOperationService
{
    Task<Result<IReadOnlyList<ApiOperationDto>>> ListAsync(Guid apiDefinitionId, CancellationToken cancellationToken);

    Task<Result<ApiOperationDto>> GetByIdAsync(Guid apiDefinitionId, Guid operationId, CancellationToken cancellationToken);

    Task<Result<ApiOperationDto>> CreateAsync(Guid apiDefinitionId, CreateApiOperationRequest request, CancellationToken cancellationToken);

    Task<Result<ApiOperationDto>> UpdateAsync(Guid apiDefinitionId, Guid operationId, UpdateApiOperationRequest request, CancellationToken cancellationToken);

    Task<Result<ApiOperationDto>> DeleteAsync(Guid apiDefinitionId, Guid operationId, string? rowVersion, CancellationToken cancellationToken);
}

/// <summary>
/// Manages operations of an API definition. Successful changes and concurrency rejections are audited with the
/// acting key name and the operation id only — never the request content.
/// </summary>
public sealed class ApiOperationService : IApiOperationService
{
    private readonly IApiOperationRepository _repository;
    private readonly IAuditLog _auditLog;
    private readonly IRequestContext _requestContext;
    private readonly TimeProvider _timeProvider;

    public ApiOperationService(IApiOperationRepository repository, IAuditLog auditLog, IRequestContext requestContext, TimeProvider timeProvider)
    {
        _repository = repository;
        _auditLog = auditLog;
        _requestContext = requestContext;
        _timeProvider = timeProvider;
    }

    public async Task<Result<IReadOnlyList<ApiOperationDto>>> ListAsync(Guid apiDefinitionId, CancellationToken cancellationToken)
    {
        if (!await _repository.DefinitionExistsAsync(apiDefinitionId, cancellationToken))
        {
            return Result<IReadOnlyList<ApiOperationDto>>.Failure(Errors.NotFound, DefinitionNotFound);
        }

        var operations = await _repository.ListByDefinitionAsync(apiDefinitionId, cancellationToken);
        return Result<IReadOnlyList<ApiOperationDto>>.Success(operations.Select(operation => operation.ToDto()).ToArray());
    }

    public async Task<Result<ApiOperationDto>> GetByIdAsync(Guid apiDefinitionId, Guid operationId, CancellationToken cancellationToken)
    {
        var operation = await _repository.GetByIdAsync(apiDefinitionId, operationId, cancellationToken);

        return operation is null
            ? Result<ApiOperationDto>.Failure(Errors.NotFound, OperationNotFound)
            : Result<ApiOperationDto>.Success(operation.ToDto());
    }

    public async Task<Result<ApiOperationDto>> CreateAsync(Guid apiDefinitionId, CreateApiOperationRequest request, CancellationToken cancellationToken)
    {
        var validationIssues = ApiOperationValidator.Validate(request);
        if (validationIssues.Count > 0)
        {
            return Result<ApiOperationDto>.Invalid(validationIssues);
        }

        if (!await _repository.DefinitionExistsAsync(apiDefinitionId, cancellationToken))
        {
            return Result<ApiOperationDto>.Failure(Errors.NotFound, DefinitionNotFound);
        }

        var key = request.Key.Trim().ToLowerInvariant();
        if (await _repository.KeyExistsAsync(apiDefinitionId, key, excludedId: null, cancellationToken))
        {
            return Result<ApiOperationDto>.Failure(Errors.Conflict, DuplicateKey);
        }

        var operation = ApiOperation.Create(
            apiDefinitionId,
            request.Key,
            request.Method,
            request.PathTemplate,
            request.TimeoutSeconds,
            request.MaxResponseBytes,
            request.Enabled,
            (request.Parameters ?? []).Select(parameter => parameter.ToDomain()),
            _timeProvider.GetUtcNow());

        await _repository.AddAsync(operation, cancellationToken);
        await _repository.SaveChangesAsync(cancellationToken);
        await AuditAsync(AuditActions.OperationCreated, operation.Id, AuditOutcome.Succeeded, errorCode: null);

        return Result<ApiOperationDto>.Success(operation.ToDto());
    }

    public async Task<Result<ApiOperationDto>> UpdateAsync(Guid apiDefinitionId, Guid operationId, UpdateApiOperationRequest request, CancellationToken cancellationToken)
    {
        var validationIssues = ApiOperationValidator.Validate(request, out var originalRowVersion);
        if (validationIssues.Count > 0)
        {
            return Result<ApiOperationDto>.Invalid(validationIssues);
        }

        var operation = await _repository.GetByIdAsync(apiDefinitionId, operationId, cancellationToken);
        if (operation is null)
        {
            return Result<ApiOperationDto>.Failure(Errors.NotFound, OperationNotFound);
        }

        var key = request.Key.Trim().ToLowerInvariant();
        if (await _repository.KeyExistsAsync(apiDefinitionId, key, operationId, cancellationToken))
        {
            return Result<ApiOperationDto>.Failure(Errors.Conflict, DuplicateKey);
        }

        _repository.SetOriginalRowVersion(operation, originalRowVersion);
        operation.Update(
            request.Key,
            request.Method,
            request.PathTemplate,
            request.TimeoutSeconds,
            request.MaxResponseBytes,
            request.Enabled,
            (request.Parameters ?? []).Select(parameter => parameter.ToDomain()),
            _timeProvider.GetUtcNow());

        if (!await TrySaveChangesAsync(cancellationToken))
        {
            await AuditAsync(AuditActions.OperationUpdated, operationId, AuditOutcome.Rejected, Errors.ConcurrencyConflict);
            return ConcurrencyConflict();
        }

        await AuditAsync(AuditActions.OperationUpdated, operationId, AuditOutcome.Succeeded, errorCode: null);
        return Result<ApiOperationDto>.Success(operation.ToDto());
    }

    public async Task<Result<ApiOperationDto>> DeleteAsync(Guid apiDefinitionId, Guid operationId, string? rowVersion, CancellationToken cancellationToken)
    {
        var validationIssues = ApiOperationValidator.ValidateRowVersion(rowVersion, out var originalRowVersion);
        if (validationIssues.Count > 0)
        {
            return Result<ApiOperationDto>.Invalid(validationIssues);
        }

        var operation = await _repository.GetByIdAsync(apiDefinitionId, operationId, cancellationToken);
        if (operation is null)
        {
            return Result<ApiOperationDto>.Failure(Errors.NotFound, OperationNotFound);
        }

        _repository.SetOriginalRowVersion(operation, originalRowVersion);
        _repository.Remove(operation);

        if (!await TrySaveChangesAsync(cancellationToken))
        {
            await AuditAsync(AuditActions.OperationDeleted, operationId, AuditOutcome.Rejected, Errors.ConcurrencyConflict);
            return ConcurrencyConflict();
        }

        await AuditAsync(AuditActions.OperationDeleted, operationId, AuditOutcome.Succeeded, errorCode: null);
        return Result<ApiOperationDto>.Success(operation.ToDto());
    }

    // The change (or its rejection) has already happened, so a client disconnect must not cancel its record.
    private Task AuditAsync(string action, Guid operationId, AuditOutcome outcome, string? errorCode)
    {
        return _auditLog.WriteAsync(
            AuditEvent.Record(
                _timeProvider.GetUtcNow(),
                _requestContext.Actor,
                action,
                AuditTargets.ApiOperation,
                operationId.ToString(),
                outcome,
                errorCode: errorCode,
                traceId: _requestContext.TraceId),
            CancellationToken.None);
    }

    private async Task<bool> TrySaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _repository.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (ConcurrencyConflictException)
        {
            return false;
        }
    }

    private static Result<ApiOperationDto> ConcurrencyConflict()
    {
        return Result<ApiOperationDto>.Failure(
            Errors.ConcurrencyConflict,
            "The operation was modified or deleted by someone else. Reload it and try again.");
    }

    private const string DefinitionNotFound = "API definition was not found.";
    private const string OperationNotFound = "API operation was not found.";
    private const string DuplicateKey = "An operation with this key already exists for this API definition.";
}
