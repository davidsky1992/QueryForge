using System.Text.Json;
using QueryForge.Application.ApiOperations;
using QueryForge.Application.Auditing;
using QueryForge.Application.Common;
using QueryForge.Application.Execution;
using QueryForge.Domain.ApiDefinitions;
using QueryForge.Domain.ApiOperations;
using QueryForge.Domain.Auditing;

namespace QueryForge.Application.Tests.Execution;

public sealed class ExecutionServiceTests
{
    private const string SecretValue = "SECRET-VALUE-91c2";
    private static readonly DateTimeOffset Now = new(2026, 10, 26, 9, 0, 0, TimeSpan.Zero);

    private readonly FakeUpstreamClient _upstream = new();
    private readonly FakeAuditLog _auditLog = new();

    [Fact]
    public async Task Success_ReturnsEnvelopeAndSendsTheBuiltRequest()
    {
        _upstream.Respond = _ => Success(new UpstreamResponse(200, "application/json", Json("""{"temp":31}"""), null, TimeSpan.FromMilliseconds(143)));
        var (service, operation) = Create();

        var result = await service.ExecuteAsync("weather", "current", Request("""{"city":"Hanoi","days":2}"""), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(200, result.Value!.UpstreamStatus);
        Assert.Equal("application/json", result.Value.ContentType);
        Assert.Equal(31, result.Value.Body!.Value.GetProperty("temp").GetInt32());
        Assert.Equal(143, result.Value.DurationMs);

        var sent = Assert.Single(_upstream.Requests);
        Assert.Equal("https://api.example.test/v1/forecast/Hanoi?days=2", sent.Url.AbsoluteUri);
        Assert.Equal(TimeSpan.FromSeconds(operation.TimeoutSeconds), sent.Timeout);
        Assert.Equal(operation.MaxResponseBytes, sent.MaxResponseBytes);

        var audit = Assert.Single(_auditLog.Events);
        Assert.Equal(AuditActions.OperationExecuted, audit.Action);
        Assert.Equal(operation.Id.ToString(), audit.TargetId);
        Assert.Equal(AuditOutcome.Succeeded, audit.Outcome);
        Assert.Equal(200, audit.UpstreamStatus);
        Assert.Equal(143, audit.DurationMs);
        Assert.Equal("test-executor", audit.Actor);
    }

    [Fact]
    public async Task UpstreamErrorStatus_IsStillASuccessfulExecution()
    {
        _upstream.Respond = _ => Success(new UpstreamResponse(503, "application/problem+json", Json("""{"title":"down"}"""), null, TimeSpan.FromMilliseconds(5)));

        var result = await Create().Service.ExecuteAsync("weather", "current", Request("""{"city":"Hanoi"}"""), CancellationToken.None);

        Assert.Equal(503, result.Value!.UpstreamStatus);
        Assert.Equal(AuditOutcome.Succeeded, Assert.Single(_auditLog.Events).Outcome);
    }

    [Fact]
    public async Task TextAndEmptyBodies_AreMappedIntoTheEnvelope()
    {
        _upstream.Respond = _ => Success(new UpstreamResponse(200, "text/plain", null, "sunny", TimeSpan.Zero));
        var text = await Create().Service.ExecuteAsync("weather", "current", Request("""{"city":"Hanoi"}"""), CancellationToken.None);

        _upstream.Respond = _ => Success(new UpstreamResponse(204, null, null, null, TimeSpan.Zero));
        var empty = await Create().Service.ExecuteAsync("weather", "current", Request("""{"city":"Hanoi"}"""), CancellationToken.None);

        Assert.Equal(JsonValueKind.String, text.Value!.Body!.Value.ValueKind);
        Assert.Equal("sunny", text.Value.Body.Value.GetString());
        Assert.Null(empty.Value!.Body);
    }

    [Fact]
    public async Task SlugAndKey_AreMatchedCaseInsensitively()
    {
        var result = await Create().Service.ExecuteAsync(" Weather ", "CURRENT", Request("""{"city":"Hanoi"}"""), CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task UnknownOperation_IsNotFound_WithoutCallingOrAuditing()
    {
        var result = await Create().Service.ExecuteAsync("weather", "missing", Request("{}"), CancellationToken.None);

        Assert.Equal(Errors.NotFound, result.ErrorCode);
        Assert.Empty(_upstream.Requests);
        Assert.Empty(_auditLog.Events);
    }

    [Theory]
    [InlineData(ApiDefinitionStatus.Draft, true)]
    [InlineData(ApiDefinitionStatus.Deprecated, true)]
    [InlineData(ApiDefinitionStatus.Archived, true)]
    [InlineData(ApiDefinitionStatus.Active, false)]
    public async Task InactiveDefinitionOrDisabledOperation_IsRejected(ApiDefinitionStatus status, bool operationEnabled)
    {
        var result = await Create(status: status, operationEnabled: operationEnabled).Service
            .ExecuteAsync("weather", "current", Request("""{"city":"Hanoi"}"""), CancellationToken.None);

        AssertRejected(result, ExecutionErrors.OperationDisabled);
    }

    [Theory]
    [InlineData(ApiAuthenticationType.ApiKey)]
    [InlineData(ApiAuthenticationType.BearerToken)]
    [InlineData(ApiAuthenticationType.Basic)]
    public async Task UpstreamAuthentication_IsNotSupportedYet(ApiAuthenticationType authenticationType)
    {
        var result = await Create(authenticationType: authenticationType).Service
            .ExecuteAsync("weather", "current", Request("""{"city":"Hanoi"}"""), CancellationToken.None);

        AssertRejected(result, ExecutionErrors.UpstreamAuthNotSupported);
    }

    [Fact]
    public async Task InvalidParameters_AreRejectedAndAuditedWithoutValues()
    {
        var result = await Create().Service.ExecuteAsync("weather", "current", Request($$"""{"city":"Hanoi","days":"{{SecretValue}}"}"""), CancellationToken.None);

        AssertRejected(result, Errors.Validation);
        Assert.Contains(result.ValidationIssues, issue => issue.Field == "parameters.days");
        AssertNoSecretInAudit();
    }

    [Theory]
    [InlineData("http://api.example.test")]
    [InlineData("https://api.example.test:8443")]
    [InlineData("https://user:pass@api.example.test")]
    public async Task NonExecutableBaseUrl_IsDestinationBlocked(string baseUrl)
    {
        var result = await Create(baseUrl: baseUrl).Service.ExecuteAsync("weather", "current", Request("""{"city":"Hanoi"}"""), CancellationToken.None);

        AssertRejected(result, UpstreamErrors.DestinationBlocked);
    }

    [Theory]
    [InlineData("..")]
    [InlineData(".")]
    [InlineData("")]
    public async Task DotOrEmptyPathValue_IsAValidationErrorBeforeAnyCall(string value)
    {
        var result = await Create().Service.ExecuteAsync("weather", "current", Request($$"""{"city":"{{value}}"}"""), CancellationToken.None);

        AssertRejected(result, Errors.Validation);
        Assert.Contains(result.ValidationIssues, issue => issue.Field == "parameters.city");
    }

    [Theory]
    [InlineData(UpstreamErrors.Timeout)]
    [InlineData(UpstreamErrors.DestinationBlocked)]
    [InlineData(UpstreamErrors.ResponseTooLarge)]
    public async Task UpstreamFailures_ArePassedThroughAndAuditedAsFailed(string errorCode)
    {
        _upstream.Respond = _ => Result<UpstreamResponse>.Failure(errorCode, "failure");

        var result = await Create().Service.ExecuteAsync("weather", "current", Request("""{"city":"Hanoi"}"""), CancellationToken.None);

        Assert.Equal(errorCode, result.ErrorCode);
        var audit = Assert.Single(_auditLog.Events);
        Assert.Equal(AuditOutcome.Failed, audit.Outcome);
        Assert.Equal(errorCode, audit.ErrorCode);
    }

    [Fact]
    public async Task ClientDisconnect_IsAuditedAndPropagated()
    {
        using var client = new CancellationTokenSource();
        _upstream.Respond = token =>
        {
            client.Cancel();
            token.ThrowIfCancellationRequested();
            return Success(new UpstreamResponse(200, null, null, null, TimeSpan.Zero));
        };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => Create().Service.ExecuteAsync("weather", "current", Request("""{"city":"Hanoi"}"""), client.Token));

        var audit = Assert.Single(_auditLog.Events);
        Assert.Equal(AuditOutcome.Failed, audit.Outcome);
        Assert.Equal("client_cancelled", audit.ErrorCode);
    }

    [Fact]
    public async Task SuccessfulAudit_NeverContainsParameterValues()
    {
        await Create().Service.ExecuteAsync("weather", "current", Request($$"""{"city":"{{SecretValue}}"}"""), CancellationToken.None);

        Assert.Single(_upstream.Requests);
        AssertNoSecretInAudit();
    }

    private void AssertRejected(Result<ExecutionResultDto> result, string errorCode)
    {
        Assert.Equal(errorCode, result.ErrorCode);
        Assert.Empty(_upstream.Requests);
        var audit = Assert.Single(_auditLog.Events);
        Assert.Equal(AuditOutcome.Rejected, audit.Outcome);
        Assert.Equal(errorCode, audit.ErrorCode);
    }

    private void AssertNoSecretInAudit()
    {
        var values = _auditLog.Events.SelectMany(item => typeof(AuditEvent).GetProperties().Select(property => property.GetValue(item)?.ToString() ?? string.Empty));
        Assert.DoesNotContain(values, value => value.Contains(SecretValue, StringComparison.Ordinal));
    }

    private (ExecutionService Service, ApiOperation Operation) Create(
        ApiDefinitionStatus status = ApiDefinitionStatus.Active,
        bool operationEnabled = true,
        ApiAuthenticationType authenticationType = ApiAuthenticationType.None,
        string baseUrl = "https://api.example.test")
    {
        var definition = ApiDefinition.Create("Weather", "weather", null, baseUrl, status, authenticationType, null, null, Now);
        var operation = ApiOperation.Create(definition.Id, "current", OperationHttpMethod.Get, "/v1/forecast/{city}", 12, 4096, operationEnabled,
        [
            new OperationParameter("city", ParameterLocation.Path, ParameterType.String, true, 64, null, null, null, null, null),
            new OperationParameter("days", ParameterLocation.Query, ParameterType.Integer, false, null, 1, 7, null, null, null)
        ], Now);

        var service = new ExecutionService(
            new FakeRepository(new ExecutionTarget(definition, operation)),
            _upstream,
            _auditLog,
            new FakeRequestContext(),
            new ExecutionOptions(),
            new FixedTimeProvider(Now));

        return (service, operation);
    }

    private static ExecuteOperationRequest Request(string json) =>
        new(JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json));

    private static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement.Clone();

    private static Result<UpstreamResponse> Success(UpstreamResponse response) => Result<UpstreamResponse>.Success(response);

    private sealed class FakeUpstreamClient : IUpstreamClient
    {
        public Func<CancellationToken, Result<UpstreamResponse>> Respond { get; set; } =
            _ => Result<UpstreamResponse>.Success(new UpstreamResponse(200, "application/json", JsonDocument.Parse("{}").RootElement.Clone(), null, TimeSpan.FromMilliseconds(1)));

        public List<UpstreamRequest> Requests { get; } = [];

        public Task<Result<UpstreamResponse>> SendAsync(UpstreamRequest request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(Respond(cancellationToken));
        }
    }

    private sealed class FakeAuditLog : IAuditLog
    {
        public List<AuditEvent> Events { get; } = [];

        public Task WriteAsync(AuditEvent auditEvent, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Events.Add(auditEvent);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeRequestContext : IRequestContext
    {
        public string Actor => "test-executor";
        public string? TraceId => "trace-9";
    }

    private sealed class FakeRepository(ExecutionTarget target) : IApiOperationRepository
    {
        public Task<ExecutionTarget?> FindForExecutionAsync(string definitionSlug, string operationKey, CancellationToken cancellationToken) =>
            Task.FromResult(definitionSlug == target.Definition.Slug && operationKey == target.Operation.Key ? target : null);

        public Task<bool> DefinitionExistsAsync(Guid apiDefinitionId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ApiOperation?> GetByIdAsync(Guid apiDefinitionId, Guid operationId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<ApiOperation>> ListByDefinitionAsync(Guid apiDefinitionId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> KeyExistsAsync(Guid apiDefinitionId, string key, Guid? excludedId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task AddAsync(ApiOperation operation, CancellationToken cancellationToken) => throw new NotSupportedException();
        public void Remove(ApiOperation operation) => throw new NotSupportedException();
        public void SetOriginalRowVersion(ApiOperation operation, byte[] rowVersion) => throw new NotSupportedException();
        public Task SaveChangesAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
