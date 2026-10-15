using QueryForge.Application.ApiDefinitions;
using QueryForge.Application.Common;
using QueryForge.Domain.ApiDefinitions;

namespace QueryForge.Application.Tests;

public sealed class ApiDefinitionServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 15, 11, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task CreateAsync_WithValidRequest_CreatesDefinition()
    {
        var repository = new FakeApiDefinitionRepository();
        var service = CreateService(repository);

        var result = await service.CreateAsync(ValidCreateRequest(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("orders-api", result.Value!.Slug);
        Assert.Single(repository.Items);
    }

    [Fact]
    public async Task CreateAsync_WithInvalidUrl_ReturnsValidationFailure()
    {
        var service = CreateService(new FakeApiDefinitionRepository());
        var request = ValidCreateRequest() with { BaseUrl = "not-a-url" };

        var result = await service.CreateAsync(request, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(Errors.Validation, result.ErrorCode);
        Assert.Contains(result.ValidationIssues, issue => issue.Field == "baseUrl");
    }

    [Fact]
    public async Task CreateAsync_WithDuplicateSlug_ReturnsConflict()
    {
        var repository = new FakeApiDefinitionRepository();
        repository.Items.Add(ApiDefinition.Create(
            "Orders API",
            "orders-api",
            null,
            "https://api.example.test",
            ApiDefinitionStatus.Active,
            ApiAuthenticationType.None,
            null,
            null,
            Now));
        var service = CreateService(repository);

        var result = await service.CreateAsync(ValidCreateRequest(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(Errors.Conflict, result.ErrorCode);
    }

    [Fact]
    public async Task UpdateAsync_WhenDefinitionDoesNotExist_ReturnsNotFound()
    {
        var service = CreateService(new FakeApiDefinitionRepository());

        var result = await service.UpdateAsync(Guid.NewGuid(), ValidUpdateRequest(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(Errors.NotFound, result.ErrorCode);
    }

    [Fact]
    public async Task ListAsync_NormalizesPageSize()
    {
        var repository = new FakeApiDefinitionRepository();
        var service = CreateService(repository);

        var result = await service.ListAsync(new ApiDefinitionQuery(PageNumber: -2, PageSize: 500), CancellationToken.None);

        Assert.Equal(1, result.PageNumber);
        Assert.Equal(PagedRequest.MaxPageSize, result.PageSize);
    }

    private static ApiDefinitionService CreateService(FakeApiDefinitionRepository repository)
    {
        return new ApiDefinitionService(repository, new FixedTimeProvider(Now));
    }

    private static CreateApiDefinitionRequest ValidCreateRequest()
    {
        return new CreateApiDefinitionRequest(
            "Orders API",
            "orders-api",
            "Customer order operations.",
            "https://api.example.test",
            ApiDefinitionStatus.Active,
            ApiAuthenticationType.BearerToken,
            "https://docs.example.test/orders",
            "orders,internal");
    }

    private static UpdateApiDefinitionRequest ValidUpdateRequest()
    {
        return new UpdateApiDefinitionRequest(
            "Orders API",
            "orders-api",
            "Customer order operations.",
            "https://api.example.test",
            ApiDefinitionStatus.Active,
            ApiAuthenticationType.BearerToken,
            "https://docs.example.test/orders",
            "orders,internal");
    }

    private sealed class FakeApiDefinitionRepository : IApiDefinitionRepository
    {
        public List<ApiDefinition> Items { get; } = [];

        public Task<ApiDefinition?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
        {
            return Task.FromResult(Items.FirstOrDefault(item => item.Id == id));
        }

        public Task<bool> SlugExistsAsync(string slug, Guid? excludedId, CancellationToken cancellationToken)
        {
            return Task.FromResult(Items.Any(item =>
                item.Slug == slug &&
                (!excludedId.HasValue || item.Id != excludedId.Value)));
        }

        public Task<PagedResult<ApiDefinition>> ListAsync(ApiDefinitionQuery query, CancellationToken cancellationToken)
        {
            var items = Items
                .Skip(query.Skip)
                .Take(query.NormalizedPageSize)
                .ToArray();

            return Task.FromResult(new PagedResult<ApiDefinition>(
                items,
                query.NormalizedPageNumber,
                query.NormalizedPageSize,
                Items.Count));
        }

        public Task AddAsync(ApiDefinition apiDefinition, CancellationToken cancellationToken)
        {
            Items.Add(apiDefinition);
            return Task.CompletedTask;
        }

        public void Remove(ApiDefinition apiDefinition)
        {
            Items.Remove(apiDefinition);
        }

        public Task SaveChangesAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _now;

        public FixedTimeProvider(DateTimeOffset now)
        {
            _now = now;
        }

        public override DateTimeOffset GetUtcNow()
        {
            return _now;
        }
    }
}
