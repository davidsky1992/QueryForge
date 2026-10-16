using Microsoft.EntityFrameworkCore;
using QueryForge.Application.ApiDefinitions;
using QueryForge.Application.Common;
using QueryForge.Domain.ApiDefinitions;
using QueryForge.Infrastructure.Persistence;

namespace QueryForge.Infrastructure.ApiDefinitions;

internal sealed class EfApiDefinitionRepository : IApiDefinitionRepository
{
    private readonly QueryForgeDbContext _dbContext;

    public EfApiDefinitionRepository(QueryForgeDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<ApiDefinition?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        return _dbContext.ApiDefinitions.FirstOrDefaultAsync(apiDefinition => apiDefinition.Id == id, cancellationToken);
    }

    public Task<bool> SlugExistsAsync(string slug, Guid? excludedId, CancellationToken cancellationToken)
    {
        return _dbContext.ApiDefinitions.AnyAsync(
            apiDefinition => apiDefinition.Slug == slug && (!excludedId.HasValue || apiDefinition.Id != excludedId.Value),
            cancellationToken);
    }

    public async Task<PagedResult<ApiDefinition>> ListAsync(ApiDefinitionQuery query, CancellationToken cancellationToken)
    {
        var pageNumber = query.NormalizedPageNumber;
        var pageSize = query.NormalizedPageSize;

        IQueryable<ApiDefinition> apiDefinitions = _dbContext.ApiDefinitions.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var searchPattern = $"%{query.Search.Trim()}%";
            apiDefinitions = apiDefinitions.Where(apiDefinition =>
                EF.Functions.Like(apiDefinition.Name, searchPattern) ||
                EF.Functions.Like(apiDefinition.Slug, searchPattern) ||
                (apiDefinition.Tags != null && EF.Functions.Like(apiDefinition.Tags, searchPattern)));
        }

        if (query.Status.HasValue)
        {
            apiDefinitions = apiDefinitions.Where(apiDefinition => apiDefinition.Status == query.Status.Value);
        }

        var totalCount = await apiDefinitions.CountAsync(cancellationToken);
        var items = await apiDefinitions
            .OrderByDescending(apiDefinition => apiDefinition.CreatedAtUtc)
            .ThenBy(apiDefinition => apiDefinition.Name)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return new PagedResult<ApiDefinition>(items, pageNumber, pageSize, totalCount);
    }

    public async Task AddAsync(ApiDefinition apiDefinition, CancellationToken cancellationToken)
    {
        await _dbContext.ApiDefinitions.AddAsync(apiDefinition, cancellationToken);
    }

    public void Remove(ApiDefinition apiDefinition)
    {
        _dbContext.ApiDefinitions.Remove(apiDefinition);
    }

    public void SetOriginalRowVersion(ApiDefinition apiDefinition, byte[] rowVersion)
    {
        // EF Core uses the original value in the UPDATE/DELETE WHERE clause, so the version check
        // and the write happen atomically in the database.
        _dbContext.Entry(apiDefinition).Property(entity => entity.RowVersion).OriginalValue = rowVersion;
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
