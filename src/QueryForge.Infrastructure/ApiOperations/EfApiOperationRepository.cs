using Microsoft.EntityFrameworkCore;
using QueryForge.Application.ApiOperations;
using QueryForge.Application.Common;
using QueryForge.Domain.ApiOperations;
using QueryForge.Infrastructure.Persistence;

namespace QueryForge.Infrastructure.ApiOperations;

internal sealed class EfApiOperationRepository : IApiOperationRepository
{
    private readonly QueryForgeDbContext _dbContext;

    public EfApiOperationRepository(QueryForgeDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<ExecutionTarget?> FindForExecutionAsync(string definitionSlug, string operationKey, CancellationToken cancellationToken)
    {
        var match = await (
                from operation in _dbContext.ApiOperations.AsNoTracking()
                join definition in _dbContext.ApiDefinitions.AsNoTracking() on operation.ApiDefinitionId equals definition.Id
                where definition.Slug == definitionSlug && operation.Key == operationKey
                select new { definition, operation })
            .FirstOrDefaultAsync(cancellationToken);

        return match is null ? null : new ExecutionTarget(match.definition, match.operation);
    }

    public Task<bool> DefinitionExistsAsync(Guid apiDefinitionId, CancellationToken cancellationToken)
    {
        return _dbContext.ApiDefinitions.AnyAsync(apiDefinition => apiDefinition.Id == apiDefinitionId, cancellationToken);
    }

    public Task<ApiOperation?> GetByIdAsync(Guid apiDefinitionId, Guid operationId, CancellationToken cancellationToken)
    {
        return _dbContext.ApiOperations.FirstOrDefaultAsync(
            operation => operation.Id == operationId && operation.ApiDefinitionId == apiDefinitionId,
            cancellationToken);
    }

    public async Task<IReadOnlyList<ApiOperation>> ListByDefinitionAsync(Guid apiDefinitionId, CancellationToken cancellationToken)
    {
        return await _dbContext.ApiOperations
            .AsNoTracking()
            .Where(operation => operation.ApiDefinitionId == apiDefinitionId)
            .OrderBy(operation => operation.Key)
            .ToListAsync(cancellationToken);
    }

    public Task<bool> KeyExistsAsync(Guid apiDefinitionId, string key, Guid? excludedId, CancellationToken cancellationToken)
    {
        return _dbContext.ApiOperations.AnyAsync(
            operation => operation.ApiDefinitionId == apiDefinitionId &&
                operation.Key == key &&
                (!excludedId.HasValue || operation.Id != excludedId.Value),
            cancellationToken);
    }

    public async Task AddAsync(ApiOperation operation, CancellationToken cancellationToken)
    {
        await _dbContext.ApiOperations.AddAsync(operation, cancellationToken);
    }

    public void Remove(ApiOperation operation)
    {
        _dbContext.ApiOperations.Remove(operation);
    }

    public void SetOriginalRowVersion(ApiOperation operation, byte[] rowVersion)
    {
        // Same approach as API definitions: the check and the write happen atomically in the UPDATE/DELETE.
        _dbContext.Entry(operation).Property(entity => entity.RowVersion).OriginalValue = rowVersion;
    }

    public async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            throw new ConcurrencyConflictException("The record was modified or deleted after it was read.", exception);
        }
    }
}
