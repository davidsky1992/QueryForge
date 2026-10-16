using QueryForge.Application.Common;

namespace QueryForge.Application.ApiDefinitions;

public interface IApiDefinitionService
{
    Task<Result<ApiDefinitionDto>> CreateAsync(CreateApiDefinitionRequest request, CancellationToken cancellationToken);

    Task<Result<ApiDefinitionDto>> UpdateAsync(Guid id, UpdateApiDefinitionRequest request, CancellationToken cancellationToken);

    Task<Result<ApiDefinitionDto>> DeleteAsync(Guid id, string? rowVersion, CancellationToken cancellationToken);

    Task<Result<ApiDefinitionDto>> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    Task<PagedResult<ApiDefinitionDto>> ListAsync(ApiDefinitionQuery query, CancellationToken cancellationToken);
}
