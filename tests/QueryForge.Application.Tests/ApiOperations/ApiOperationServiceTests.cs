using QueryForge.Application.ApiOperations;
using QueryForge.Application.Auditing;
using QueryForge.Application.Common;
using QueryForge.Domain.ApiOperations;
using QueryForge.Domain.Auditing;

namespace QueryForge.Application.Tests.ApiOperations;

public sealed class ApiOperationServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 26, 9, 0, 0, TimeSpan.Zero);
    private static readonly byte[] CurrentVersion = [0, 0, 0, 0, 0, 0, 0, 2];
    private static readonly byte[] StaleVersion = [0, 0, 0, 0, 0, 0, 0, 1];
    private static readonly Guid DefinitionId = Guid.NewGuid();

    private readonly FakeRepository _repository = new();
    private readonly FakeAuditLog _auditLog = new();

    [Fact]
    public async Task Create_ValidRequest_SavesAndAuditsWithActorOnly()
    {
        var result = await Service().CreateAsync(DefinitionId, CreateRequest(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("current-weather", result.Value!.Key);
        Assert.Single(_repository.Items);
        var audit = Assert.Single(_auditLog.Events);
        Assert.Equal(AuditActions.OperationCreated, audit.Action);
        Assert.Equal(AuditTargets.ApiOperation, audit.TargetType);
        Assert.Equal(result.Value.Id.ToString(), audit.TargetId);
        Assert.Equal("test-admin", audit.Actor);
        Assert.Equal("trace-1", audit.TraceId);
        Assert.Equal(AuditOutcome.Succeeded, audit.Outcome);
    }

    [Fact]
    public async Task Create_Invalid_ReturnsValidationAndAuditsNothing()
    {
        var result = await Service().CreateAsync(DefinitionId, CreateRequest() with { PathTemplate = "/v1/../admin" }, CancellationToken.None);

        Assert.Equal(Errors.Validation, result.ErrorCode);
        Assert.Contains(result.ValidationIssues, issue => issue.Field == "pathTemplate");
        Assert.Empty(_repository.Items);
        Assert.Empty(_auditLog.Events);
    }

    [Fact]
    public async Task Create_UnknownDefinition_ReturnsNotFound()
    {
        var result = await Service().CreateAsync(Guid.NewGuid(), CreateRequest(), CancellationToken.None);

        Assert.Equal(Errors.NotFound, result.ErrorCode);
        Assert.Empty(_auditLog.Events);
    }

    [Fact]
    public async Task Create_DuplicateKeyInSameDefinition_ReturnsConflict()
    {
        _repository.Seed(Operation("current-weather"), CurrentVersion);

        var result = await Service().CreateAsync(DefinitionId, CreateRequest(), CancellationToken.None);

        Assert.Equal(Errors.Conflict, result.ErrorCode);
        Assert.Single(_repository.Items);
    }

    [Fact]
    public async Task Update_WithCurrentVersion_UpdatesAndAudits()
    {
        var existing = _repository.Seed(Operation("current-weather"), CurrentVersion);

        var result = await Service().UpdateAsync(DefinitionId, existing.Id, UpdateRequest(CurrentVersion) with { TimeoutSeconds = 20 }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(20, result.Value!.TimeoutSeconds);
        Assert.Equal(Now, result.Value.UpdatedAtUtc);
        Assert.Equal(AuditOutcome.Succeeded, Assert.Single(_auditLog.Events).Outcome);
    }

    [Fact]
    public async Task Update_WithStaleVersion_ReturnsConcurrencyConflictAndAuditsTheRejection()
    {
        var existing = _repository.Seed(Operation("current-weather"), CurrentVersion);

        var result = await Service().UpdateAsync(DefinitionId, existing.Id, UpdateRequest(StaleVersion), CancellationToken.None);

        Assert.Equal(Errors.ConcurrencyConflict, result.ErrorCode);
        var audit = Assert.Single(_auditLog.Events);
        Assert.Equal(AuditActions.OperationUpdated, audit.Action);
        Assert.Equal(AuditOutcome.Rejected, audit.Outcome);
        Assert.Equal(Errors.ConcurrencyConflict, audit.ErrorCode);
    }

    [Fact]
    public async Task Update_ToAnotherOperationsKey_ReturnsConflict()
    {
        _repository.Seed(Operation("taken"), CurrentVersion);
        var existing = _repository.Seed(Operation("current-weather"), CurrentVersion);

        var result = await Service().UpdateAsync(DefinitionId, existing.Id, UpdateRequest(CurrentVersion) with { Key = "taken" }, CancellationToken.None);

        Assert.Equal(Errors.Conflict, result.ErrorCode);
        Assert.Empty(_auditLog.Events);
    }

    [Fact]
    public async Task Update_KeepingItsOwnKey_IsNotAConflict()
    {
        var existing = _repository.Seed(Operation("current-weather"), CurrentVersion);

        var result = await Service().UpdateAsync(DefinitionId, existing.Id, UpdateRequest(CurrentVersion), CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task Update_OperationOfAnotherDefinition_ReturnsNotFound()
    {
        var existing = _repository.Seed(Operation("current-weather"), CurrentVersion);

        var result = await Service().UpdateAsync(Guid.NewGuid(), existing.Id, UpdateRequest(CurrentVersion), CancellationToken.None);

        Assert.Equal(Errors.NotFound, result.ErrorCode);
    }

    [Fact]
    public async Task Delete_WithCurrentVersion_RemovesAndAudits()
    {
        var existing = _repository.Seed(Operation("current-weather"), CurrentVersion);

        var result = await Service().DeleteAsync(DefinitionId, existing.Id, Convert.ToBase64String(CurrentVersion), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(_repository.Items);
        Assert.Equal(AuditActions.OperationDeleted, Assert.Single(_auditLog.Events).Action);
    }

    [Fact]
    public async Task Delete_WithStaleVersion_KeepsTheOperationAndAuditsTheRejection()
    {
        var existing = _repository.Seed(Operation("current-weather"), CurrentVersion);

        var result = await Service().DeleteAsync(DefinitionId, existing.Id, Convert.ToBase64String(StaleVersion), CancellationToken.None);

        Assert.Equal(Errors.ConcurrencyConflict, result.ErrorCode);
        Assert.Single(_repository.Items);
        Assert.Equal(AuditOutcome.Rejected, Assert.Single(_auditLog.Events).Outcome);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("not-base64!")]
    public async Task Delete_WithMissingOrMalformedVersion_IsValidationError(string? rowVersion)
    {
        var existing = _repository.Seed(Operation("current-weather"), CurrentVersion);

        var result = await Service().DeleteAsync(DefinitionId, existing.Id, rowVersion, CancellationToken.None);

        Assert.Equal(Errors.Validation, result.ErrorCode);
        Assert.Single(_repository.Items);
    }

    [Fact]
    public async Task List_UnknownDefinition_ReturnsNotFound()
    {
        var result = await Service().ListAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(Errors.NotFound, result.ErrorCode);
    }

    [Fact]
    public async Task Audit_IsWrittenEvenIfTheClientDisconnects()
    {
        using var cancelled = new CancellationTokenSource();

        // The save itself completes; the client disconnects afterwards.
        _repository.OnSave = cancelled.Cancel;
        await Service().CreateAsync(DefinitionId, CreateRequest(), cancelled.Token);

        Assert.Single(_auditLog.Events);
        Assert.False(_auditLog.LastToken.CanBeCanceled);
    }

    [Fact]
    public async Task Audit_NeverContainsRequestContent()
    {
        const string sentinel = "SENTINEL-7d1f";
        var request = CreateRequest() with
        {
            Parameters =
            [
                new OperationParameterRequest("city", ParameterLocation.Path, ParameterType.String, Required: true),
                new OperationParameterRequest("q", ParameterLocation.Query, ParameterType.String, false, Default: sentinel)
            ]
        };

        var result = await Service().CreateAsync(DefinitionId, request, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var audit = Assert.Single(_auditLog.Events);
        var values = typeof(AuditEvent).GetProperties().Select(property => property.GetValue(audit)?.ToString() ?? string.Empty);
        Assert.DoesNotContain(values, value => value.Contains(sentinel, StringComparison.Ordinal) || value.Contains("/v1/forecast", StringComparison.Ordinal));
    }

    private ApiOperationService Service() =>
        new(_repository, _auditLog, new FakeRequestContext(), new FixedTimeProvider(Now));

    private static CreateApiOperationRequest CreateRequest() =>
        new("current-weather", OperationHttpMethod.Get, "/v1/forecast/{city}",
            [new OperationParameterRequest("city", ParameterLocation.Path, ParameterType.String, Required: true)]);

    private static UpdateApiOperationRequest UpdateRequest(byte[] rowVersion) =>
        new("current-weather", OperationHttpMethod.Get, "/v1/forecast/{city}",
            [new OperationParameterRequest("city", ParameterLocation.Path, ParameterType.String, Required: true)],
            10, 1024, true, Convert.ToBase64String(rowVersion));

    private static ApiOperation Operation(string key) =>
        ApiOperation.Create(DefinitionId, key, OperationHttpMethod.Get, "/items", 10, 1024, true, [], Now.AddDays(-1));

    private sealed class FakeRequestContext : IRequestContext
    {
        public string Actor => "test-admin";
        public string? TraceId => "trace-1";
    }

    private sealed class FakeAuditLog : IAuditLog
    {
        public List<AuditEvent> Events { get; } = [];
        public CancellationToken LastToken { get; private set; }

        public Task WriteAsync(AuditEvent auditEvent, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LastToken = cancellationToken;
            Events.Add(auditEvent);
            return Task.CompletedTask;
        }
    }

    /// <summary>Mimics the database's rowversion check, like the API definition service tests.</summary>
    private sealed class FakeRepository : IApiOperationRepository
    {
        private readonly Dictionary<Guid, byte[]> _storedVersions = [];
        private readonly Dictionary<Guid, byte[]> _originalVersions = [];
        private readonly List<ApiOperation> _pendingRemovals = [];

        public List<ApiOperation> Items { get; } = [];
        public Action? OnSave { get; set; }

        public ApiOperation Seed(ApiOperation operation, byte[] version)
        {
            Items.Add(operation);
            _storedVersions[operation.Id] = version;
            return operation;
        }

        public Task<bool> DefinitionExistsAsync(Guid apiDefinitionId, CancellationToken cancellationToken) =>
            Task.FromResult(apiDefinitionId == DefinitionId);

        public Task<ApiOperation?> GetByIdAsync(Guid apiDefinitionId, Guid operationId, CancellationToken cancellationToken) =>
            Task.FromResult(Items.FirstOrDefault(item => item.Id == operationId && item.ApiDefinitionId == apiDefinitionId));

        public Task<IReadOnlyList<ApiOperation>> ListByDefinitionAsync(Guid apiDefinitionId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ApiOperation>>(Items.Where(item => item.ApiDefinitionId == apiDefinitionId).ToArray());

        public Task<bool> KeyExistsAsync(Guid apiDefinitionId, string key, Guid? excludedId, CancellationToken cancellationToken) =>
            Task.FromResult(Items.Any(item => item.ApiDefinitionId == apiDefinitionId && item.Key == key && item.Id != excludedId));

        public Task AddAsync(ApiOperation operation, CancellationToken cancellationToken)
        {
            Items.Add(operation);
            return Task.CompletedTask;
        }

        public void Remove(ApiOperation operation) => _pendingRemovals.Add(operation);

        public void SetOriginalRowVersion(ApiOperation operation, byte[] rowVersion) => _originalVersions[operation.Id] = rowVersion;

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
            }

            _originalVersions.Clear();
            _pendingRemovals.Clear();
            OnSave?.Invoke();
            return Task.CompletedTask;
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
