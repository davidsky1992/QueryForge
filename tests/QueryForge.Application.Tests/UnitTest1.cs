using QueryForge.Application.ApiDefinitions;
using QueryForge.Application.Common;
using QueryForge.Domain.ApiDefinitions;

namespace QueryForge.Application.Tests;

public sealed class ApiDefinitionServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 15, 11, 0, 0, TimeSpan.Zero);
    private static readonly byte[] CurrentVersion = [0, 0, 0, 0, 0, 0, 0, 2];
    private static readonly byte[] StaleVersion = [0, 0, 0, 0, 0, 0, 0, 1];

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
        repository.Seed(CreateDefinition(), CurrentVersion);
        var service = CreateService(repository);

        var result = await service.CreateAsync(ValidCreateRequest(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(Errors.Conflict, result.ErrorCode);
    }

    [Fact]
    public async Task UpdateAsync_WhenDefinitionDoesNotExist_ReturnsNotFound()
    {
        var service = CreateService(new FakeApiDefinitionRepository());

        var result = await service.UpdateAsync(Guid.NewGuid(), ValidUpdateRequest(CurrentVersion), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(Errors.NotFound, result.ErrorCode);
    }

    [Fact]
    public async Task UpdateAsync_WithCurrentRowVersion_UpdatesDefinition()
    {
        var repository = new FakeApiDefinitionRepository();
        var existing = repository.Seed(CreateDefinition(), CurrentVersion);
        var service = CreateService(repository);
        var request = ValidUpdateRequest(CurrentVersion) with { Name = "Orders API v2" };

        var result = await service.UpdateAsync(existing.Id, request, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Orders API v2", result.Value!.Name);
        Assert.Equal(Now, result.Value.UpdatedAtUtc);
        Assert.Equal(1, repository.SuccessfulSaves);
    }

    [Fact]
    public async Task UpdateAsync_WithStaleRowVersion_ReturnsConcurrencyConflict()
    {
        var repository = new FakeApiDefinitionRepository();
        var existing = repository.Seed(CreateDefinition(), CurrentVersion);
        var service = CreateService(repository);

        var result = await service.UpdateAsync(existing.Id, ValidUpdateRequest(StaleVersion), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(Errors.ConcurrencyConflict, result.ErrorCode);
        Assert.Equal(0, repository.SuccessfulSaves);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-base64!")]
    [InlineData("AAAAAAAAAAAAAAAA")] // valid base64, but 12 bytes instead of 8
    public async Task UpdateAsync_WithMissingOrMalformedRowVersion_ReturnsValidationFailure(string? rowVersion)
    {
        var repository = new FakeApiDefinitionRepository();
        var existing = repository.Seed(CreateDefinition(), CurrentVersion);
        var service = CreateService(repository);
        var request = ValidUpdateRequest(CurrentVersion) with { RowVersion = rowVersion! };

        var result = await service.UpdateAsync(existing.Id, request, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(Errors.Validation, result.ErrorCode);
        Assert.Contains(result.ValidationIssues, issue => issue.Field == "rowVersion");
    }

    [Fact]
    public async Task DeleteAsync_WithCurrentRowVersion_RemovesDefinition()
    {
        var repository = new FakeApiDefinitionRepository();
        var existing = repository.Seed(CreateDefinition(), CurrentVersion);
        var service = CreateService(repository);

        var result = await service.DeleteAsync(existing.Id, Convert.ToBase64String(CurrentVersion), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(repository.Items);
    }

    [Fact]
    public async Task DeleteAsync_WithStaleRowVersion_ReturnsConcurrencyConflictAndKeepsDefinition()
    {
        var repository = new FakeApiDefinitionRepository();
        var existing = repository.Seed(CreateDefinition(), CurrentVersion);
        var service = CreateService(repository);

        var result = await service.DeleteAsync(existing.Id, Convert.ToBase64String(StaleVersion), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(Errors.ConcurrencyConflict, result.ErrorCode);
        Assert.Single(repository.Items);
    }

    [Fact]
    public async Task DeleteAsync_WithoutRowVersion_ReturnsValidationFailure()
    {
        var repository = new FakeApiDefinitionRepository();
        var existing = repository.Seed(CreateDefinition(), CurrentVersion);
        var service = CreateService(repository);

        var result = await service.DeleteAsync(existing.Id, rowVersion: null, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(Errors.Validation, result.ErrorCode);
        Assert.Single(repository.Items);
    }

    [Fact]
    public async Task DeleteAsync_WhenDefinitionDoesNotExist_ReturnsNotFound()
    {
        var service = CreateService(new FakeApiDefinitionRepository());

        var result = await service.DeleteAsync(Guid.NewGuid(), Convert.ToBase64String(CurrentVersion), CancellationToken.None);

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

    [Fact]
    public async Task CreateAsync_WithApiKeySecretReference_StoresNameAndHeaderAndReportsConfigured()
    {
        var service = CreateService(new FakeApiDefinitionRepository(), new FakeSecretProvider("weather-api"));
        var request = ValidCreateRequest() with
        {
            AuthenticationType = ApiAuthenticationType.ApiKey,
            UpstreamSecretName = " Weather-API ",
            ApiKeyHeaderName = " X-API-Key "
        };

        var result = await service.CreateAsync(request, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("weather-api", result.Value!.UpstreamSecretName);
        Assert.Equal("X-API-Key", result.Value.ApiKeyHeaderName);
        Assert.True(result.Value.SecretConfigured);
    }

    [Fact]
    public async Task CreateAsync_WithSecretNameWithoutValue_ReportsNotConfigured()
    {
        var service = CreateService(new FakeApiDefinitionRepository(), new FakeSecretProvider());
        var request = ValidCreateRequest() with
        {
            AuthenticationType = ApiAuthenticationType.ApiKey,
            UpstreamSecretName = "weather-api",
            ApiKeyHeaderName = "X-API-Key"
        };

        var result = await service.CreateAsync(request, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value!.SecretConfigured);
    }

    [Fact]
    public async Task CreateAsync_ApiKeyWithoutSecretReference_IsAllowedForCatalogUse()
    {
        var service = CreateService(new FakeApiDefinitionRepository());

        var result = await service.CreateAsync(ValidCreateRequest() with { AuthenticationType = ApiAuthenticationType.ApiKey }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value!.UpstreamSecretName);
        Assert.False(result.Value.SecretConfigured);
    }

    [Theory]
    [InlineData(ApiAuthenticationType.None)]
    [InlineData(ApiAuthenticationType.BearerToken)]
    [InlineData(ApiAuthenticationType.Basic)]
    public async Task CreateAsync_SecretReferenceWithOtherAuthentication_IsRejected(ApiAuthenticationType authenticationType)
    {
        var request = ValidCreateRequest() with { AuthenticationType = authenticationType, UpstreamSecretName = "weather-api", ApiKeyHeaderName = "X-API-Key" };

        await AssertValidationIssueAsync(request, "upstreamSecretName");
    }

    [Theory]
    [InlineData("weather-api", null, "apiKeyHeaderName")]
    [InlineData(null, "X-API-Key", "upstreamSecretName")]
    [InlineData("weather api", "X-API-Key", "upstreamSecretName")]
    [InlineData("weather_api", "X-API-Key", "upstreamSecretName")]
    [InlineData("-weather", "X-API-Key", "upstreamSecretName")]
    [InlineData("weather-api", "X API Key", "apiKeyHeaderName")]
    [InlineData("weather-api", "X-Key:", "apiKeyHeaderName")]
    [InlineData("weather-api", "X-Key\r\nX-Injected", "apiKeyHeaderName")]
    public async Task CreateAsync_IncompleteOrMalformedSecretReference_IsRejected(string? secretName, string? headerName, string field)
    {
        var request = ValidCreateRequest() with { AuthenticationType = ApiAuthenticationType.ApiKey, UpstreamSecretName = secretName, ApiKeyHeaderName = headerName };

        await AssertValidationIssueAsync(request, field);
    }

    [Theory]
    [InlineData("Host")]
    [InlineData("host")]
    [InlineData("Content-Length")]
    [InlineData("Transfer-Encoding")]
    [InlineData("Connection")]
    [InlineData("Cookie")]
    [InlineData("User-Agent")]
    [InlineData("Accept")]
    [InlineData("Proxy-Authorization")]
    [InlineData("X-Forwarded-For")]
    [InlineData("X-Forwarded-Host")]
    [InlineData("Forwarded")]
    [InlineData("Sec-Fetch-Site")]
    public async Task CreateAsync_ForbiddenHeaderNames_AreRejected(string headerName)
    {
        var request = ValidCreateRequest() with { AuthenticationType = ApiAuthenticationType.ApiKey, UpstreamSecretName = "weather-api", ApiKeyHeaderName = headerName };

        await AssertValidationIssueAsync(request, "apiKeyHeaderName");
    }

    [Theory]
    [InlineData("X-API-Key")]
    [InlineData("Authorization")]
    [InlineData("api-key")]
    [InlineData("Ocp-Apim-Subscription-Key")]
    public async Task CreateAsync_CommonApiKeyHeaders_AreAccepted(string headerName)
    {
        var request = ValidCreateRequest() with { AuthenticationType = ApiAuthenticationType.ApiKey, UpstreamSecretName = "weather-api", ApiKeyHeaderName = headerName };

        var result = await CreateService(new FakeApiDefinitionRepository()).CreateAsync(request, CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task UpdateAsync_ReplacesTheReference_SoClientsMustSendItBack()
    {
        // Updates replace the whole definition: sending the reference keeps it, omitting it clears it.
        var repository = new FakeApiDefinitionRepository();
        var existing = repository.Seed(ApiDefinition.Create("Orders API", "orders-api", null, "https://api.example.test",
            ApiDefinitionStatus.Active, ApiAuthenticationType.ApiKey, null, null, Now, "weather-api", "X-API-Key"), CurrentVersion);
        var service = CreateService(repository);
        var request = ValidUpdateRequest(CurrentVersion) with { AuthenticationType = ApiAuthenticationType.ApiKey };

        var kept = await service.UpdateAsync(existing.Id, request with { UpstreamSecretName = "weather-api", ApiKeyHeaderName = "X-API-Key" }, CancellationToken.None);
        Assert.Equal("weather-api", kept.Value!.UpstreamSecretName);
        Assert.Equal("X-API-Key", kept.Value.ApiKeyHeaderName);

        var cleared = await service.UpdateAsync(existing.Id, request, CancellationToken.None);
        Assert.Null(cleared.Value!.UpstreamSecretName);
        Assert.Null(cleared.Value.ApiKeyHeaderName);
    }

    [Fact]
    public async Task Dto_NeverContainsTheSecretValue()
    {
        const string secretValue = "SECRET-VALUE-0c7d";
        var service = CreateService(new FakeApiDefinitionRepository(), FakeSecretProvider.WithValue("weather-api", secretValue));
        var request = ValidCreateRequest() with { AuthenticationType = ApiAuthenticationType.ApiKey, UpstreamSecretName = "weather-api", ApiKeyHeaderName = "X-API-Key" };

        var result = await service.CreateAsync(request, CancellationToken.None);

        Assert.DoesNotContain(secretValue, System.Text.Json.JsonSerializer.Serialize(result.Value));
    }

    private static async Task AssertValidationIssueAsync(CreateApiDefinitionRequest request, string field)
    {
        var result = await CreateService(new FakeApiDefinitionRepository()).CreateAsync(request, CancellationToken.None);

        Assert.Equal(Errors.Validation, result.ErrorCode);
        Assert.Contains(result.ValidationIssues, issue => issue.Field == field);
    }

    private static ApiDefinitionService CreateService(FakeApiDefinitionRepository repository, FakeSecretProvider? secretProvider = null)
    {
        return new ApiDefinitionService(repository, secretProvider ?? new FakeSecretProvider(), new FixedTimeProvider(Now));
    }

    private sealed class FakeSecretProvider : QueryForge.Application.Secrets.ISecretProvider
    {
        private readonly Dictionary<string, string> _secrets;

        public FakeSecretProvider(params string[] configuredNames)
        {
            _secrets = configuredNames.ToDictionary(name => name, _ => "configured-value");
        }

        private FakeSecretProvider(Dictionary<string, string> secrets)
        {
            _secrets = secrets;
        }

        public static FakeSecretProvider WithValue(string name, string value) => new(new Dictionary<string, string> { [name] = value });

        public bool TryGetSecret(string name, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out string? value) =>
            _secrets.TryGetValue(name, out value);
    }

    private static ApiDefinition CreateDefinition()
    {
        return ApiDefinition.Create(
            "Orders API",
            "orders-api",
            null,
            "https://api.example.test",
            ApiDefinitionStatus.Active,
            ApiAuthenticationType.None,
            null,
            null,
            Now.AddDays(-1));
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

    private static UpdateApiDefinitionRequest ValidUpdateRequest(byte[] rowVersion)
    {
        return new UpdateApiDefinitionRequest(
            "Orders API",
            "orders-api",
            "Customer order operations.",
            "https://api.example.test",
            ApiDefinitionStatus.Active,
            ApiAuthenticationType.BearerToken,
            "https://docs.example.test/orders",
            "orders,internal",
            Convert.ToBase64String(rowVersion));
    }

    /// <summary>
    /// In-memory repository that mimics the database's rowversion check: a save fails when the
    /// original row version set by the caller differs from the stored version.
    /// </summary>
    private sealed class FakeApiDefinitionRepository : IApiDefinitionRepository
    {
        private readonly Dictionary<Guid, byte[]> _storedVersions = [];
        private readonly Dictionary<Guid, byte[]> _originalVersions = [];
        private readonly List<ApiDefinition> _pendingRemovals = [];

        public List<ApiDefinition> Items { get; } = [];
        public int SuccessfulSaves { get; private set; }

        public ApiDefinition Seed(ApiDefinition apiDefinition, byte[] storedVersion)
        {
            Items.Add(apiDefinition);
            _storedVersions[apiDefinition.Id] = storedVersion;
            return apiDefinition;
        }

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
            _pendingRemovals.Add(apiDefinition);
        }

        public void SetOriginalRowVersion(ApiDefinition apiDefinition, byte[] rowVersion)
        {
            _originalVersions[apiDefinition.Id] = rowVersion;
        }

        public Task SaveChangesAsync(CancellationToken cancellationToken)
        {
            foreach (var (id, original) in _originalVersions)
            {
                if (!_storedVersions.TryGetValue(id, out var stored) || !stored.AsSpan().SequenceEqual(original))
                {
                    _originalVersions.Clear();
                    _pendingRemovals.Clear();
                    throw new ConcurrencyConflictException("Stale row version.", new InvalidOperationException());
                }
            }

            foreach (var removed in _pendingRemovals)
            {
                Items.Remove(removed);
                _storedVersions.Remove(removed.Id);
            }

            _originalVersions.Clear();
            _pendingRemovals.Clear();
            SuccessfulSaves++;
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
