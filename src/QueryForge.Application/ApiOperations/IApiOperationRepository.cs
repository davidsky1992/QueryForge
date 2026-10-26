using QueryForge.Domain.ApiDefinitions;
using QueryForge.Domain.ApiOperations;

namespace QueryForge.Application.ApiOperations;

public sealed record ExecutionTarget(ApiDefinition Definition, ApiOperation Operation);

public interface IApiOperationRepository
{
    /// <summary>Read-only lookup of an operation (with its parameters) and its definition by slug and key.</summary>
    Task<ExecutionTarget?> FindForExecutionAsync(string definitionSlug, string operationKey, CancellationToken cancellationToken);

    Task<bool> DefinitionExistsAsync(Guid apiDefinitionId, CancellationToken cancellationToken);

    Task<ApiOperation?> GetByIdAsync(Guid apiDefinitionId, Guid operationId, CancellationToken cancellationToken);

    Task<IReadOnlyList<ApiOperation>> ListByDefinitionAsync(Guid apiDefinitionId, CancellationToken cancellationToken);

    Task<bool> KeyExistsAsync(Guid apiDefinitionId, string key, Guid? excludedId, CancellationToken cancellationToken);

    Task AddAsync(ApiOperation operation, CancellationToken cancellationToken);

    void Remove(ApiOperation operation);

    void SetOriginalRowVersion(ApiOperation operation, byte[] rowVersion);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
