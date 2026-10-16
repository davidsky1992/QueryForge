using QueryForge.Application.Common;
using QueryForge.Domain.ApiDefinitions;

namespace QueryForge.Application.ApiDefinitions;

public interface IApiDefinitionRepository
{
    Task<ApiDefinition?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    Task<bool> SlugExistsAsync(string slug, Guid? excludedId, CancellationToken cancellationToken);

    Task<PagedResult<ApiDefinition>> ListAsync(ApiDefinitionQuery query, CancellationToken cancellationToken);

    Task AddAsync(ApiDefinition apiDefinition, CancellationToken cancellationToken);

    void Remove(ApiDefinition apiDefinition);

    /// <summary>
    /// Sets the row version the client last read so the next save only succeeds if the stored version still matches.
    /// </summary>
    void SetOriginalRowVersion(ApiDefinition apiDefinition, byte[] rowVersion);

    /// <exception cref="ConcurrencyConflictException">The record changed since the original row version was read.</exception>
    Task SaveChangesAsync(CancellationToken cancellationToken);
}
