using QueryForge.Domain.ApiOperations;

namespace QueryForge.Application.ApiOperations;

public interface IApiOperationRepository
{
    Task<bool> DefinitionExistsAsync(Guid apiDefinitionId, CancellationToken cancellationToken);

    Task<ApiOperation?> GetByIdAsync(Guid apiDefinitionId, Guid operationId, CancellationToken cancellationToken);

    Task<IReadOnlyList<ApiOperation>> ListByDefinitionAsync(Guid apiDefinitionId, CancellationToken cancellationToken);

    Task<bool> KeyExistsAsync(Guid apiDefinitionId, string key, Guid? excludedId, CancellationToken cancellationToken);

    Task AddAsync(ApiOperation operation, CancellationToken cancellationToken);

    void Remove(ApiOperation operation);

    void SetOriginalRowVersion(ApiOperation operation, byte[] rowVersion);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
