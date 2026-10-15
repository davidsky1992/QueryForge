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

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
