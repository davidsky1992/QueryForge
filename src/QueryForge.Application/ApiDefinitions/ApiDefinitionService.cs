using QueryForge.Application.Common;
using QueryForge.Domain.ApiDefinitions;

namespace QueryForge.Application.ApiDefinitions;

public sealed class ApiDefinitionService : IApiDefinitionService
{
    private readonly IApiDefinitionRepository _repository;
    private readonly TimeProvider _timeProvider;

    public ApiDefinitionService(IApiDefinitionRepository repository, TimeProvider timeProvider)
    {
        _repository = repository;
        _timeProvider = timeProvider;
    }

    public async Task<Result<ApiDefinitionDto>> CreateAsync(CreateApiDefinitionRequest request, CancellationToken cancellationToken)
    {
        var validationIssues = ApiDefinitionValidator.Validate(request);
        if (validationIssues.Count > 0)
        {
            return Result<ApiDefinitionDto>.Invalid(validationIssues);
        }

        if (await _repository.SlugExistsAsync(request.Slug.Trim().ToLowerInvariant(), excludedId: null, cancellationToken))
        {
            return Result<ApiDefinitionDto>.Failure(Errors.Conflict, "An API definition with this slug already exists.");
        }

        var apiDefinition = ApiDefinition.Create(
            request.Name,
            request.Slug,
            request.Description,
            request.BaseUrl,
            request.Status,
            request.AuthenticationType,
            request.DocumentationUrl,
            request.Tags,
            _timeProvider.GetUtcNow());

        await _repository.AddAsync(apiDefinition, cancellationToken);
        await _repository.SaveChangesAsync(cancellationToken);

        return Result<ApiDefinitionDto>.Success(apiDefinition.ToDto());
    }

    public async Task<Result<ApiDefinitionDto>> UpdateAsync(Guid id, UpdateApiDefinitionRequest request, CancellationToken cancellationToken)
    {
        var validationIssues = ApiDefinitionValidator.Validate(request, out var originalRowVersion);
        if (validationIssues.Count > 0)
        {
            return Result<ApiDefinitionDto>.Invalid(validationIssues);
        }

        var apiDefinition = await _repository.GetByIdAsync(id, cancellationToken);
        if (apiDefinition is null)
        {
            return Result<ApiDefinitionDto>.Failure(Errors.NotFound, "API definition was not found.");
        }

        if (await _repository.SlugExistsAsync(request.Slug.Trim().ToLowerInvariant(), id, cancellationToken))
        {
            return Result<ApiDefinitionDto>.Failure(Errors.Conflict, "An API definition with this slug already exists.");
        }

        _repository.SetOriginalRowVersion(apiDefinition, originalRowVersion);
        apiDefinition.Update(
            request.Name,
            request.Slug,
            request.Description,
            request.BaseUrl,
            request.Status,
            request.AuthenticationType,
            request.DocumentationUrl,
            request.Tags,
            _timeProvider.GetUtcNow());

        if (!await TrySaveChangesAsync(cancellationToken))
        {
            return ConcurrencyConflict();
        }

        return Result<ApiDefinitionDto>.Success(apiDefinition.ToDto());
    }

    public async Task<Result<ApiDefinitionDto>> DeleteAsync(Guid id, string? rowVersion, CancellationToken cancellationToken)
    {
        var validationIssues = ApiDefinitionValidator.ValidateRowVersion(rowVersion, out var originalRowVersion);
        if (validationIssues.Count > 0)
        {
            return Result<ApiDefinitionDto>.Invalid(validationIssues);
        }

        var apiDefinition = await _repository.GetByIdAsync(id, cancellationToken);
        if (apiDefinition is null)
        {
            return Result<ApiDefinitionDto>.Failure(Errors.NotFound, "API definition was not found.");
        }

        _repository.SetOriginalRowVersion(apiDefinition, originalRowVersion);
        _repository.Remove(apiDefinition);

        if (!await TrySaveChangesAsync(cancellationToken))
        {
            return ConcurrencyConflict();
        }

        return Result<ApiDefinitionDto>.Success(apiDefinition.ToDto());
    }

    public async Task<Result<ApiDefinitionDto>> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        var apiDefinition = await _repository.GetByIdAsync(id, cancellationToken);

        return apiDefinition is null
            ? Result<ApiDefinitionDto>.Failure(Errors.NotFound, "API definition was not found.")
            : Result<ApiDefinitionDto>.Success(apiDefinition.ToDto());
    }

    public async Task<PagedResult<ApiDefinitionDto>> ListAsync(ApiDefinitionQuery query, CancellationToken cancellationToken)
    {
        var page = await _repository.ListAsync(query, cancellationToken);
        var items = page.Items.Select(item => item.ToDto()).ToArray();

        return new PagedResult<ApiDefinitionDto>(items, page.PageNumber, page.PageSize, page.TotalCount);
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

    private static Result<ApiDefinitionDto> ConcurrencyConflict()
    {
        return Result<ApiDefinitionDto>.Failure(
            Errors.ConcurrencyConflict,
            "The API definition was modified or deleted by someone else. Reload it and try again.");
    }
}
