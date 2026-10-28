using QueryForge.Application.Auditing;
using QueryForge.Application.Common;
using QueryForge.Domain.Auditing;

namespace QueryForge.Application.Tests.Auditing;

public sealed class AuditEventServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 28, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task List_MapsEveryFieldAndPassesTheQueryThrough()
    {
        var recorded = AuditEvent.Record(Now, "ops-admin", "operation.executed", "ApiOperation", "abc", AuditOutcome.Failed, 502, 12, "upstream_timeout", "trace-1");
        var repository = new FakeRepository(recorded);
        var query = new AuditEventQuery(2, 10, "operation.executed", "ApiOperation", "abc", AuditOutcome.Failed, Now.AddDays(-1), Now.AddDays(1));

        var result = await new AuditEventService(repository).ListAsync(query, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Same(query, repository.LastQuery);
        var item = Assert.Single(result.Value!.Items);
        Assert.Equal(recorded.Id, item.Id);
        Assert.Equal("ops-admin", item.Actor);
        Assert.Equal("operation.executed", item.Action);
        Assert.Equal("ApiOperation", item.TargetType);
        Assert.Equal("abc", item.TargetId);
        Assert.Equal(AuditOutcome.Failed, item.Outcome);
        Assert.Equal(502, item.UpstreamStatus);
        Assert.Equal(12, item.DurationMs);
        Assert.Equal("upstream_timeout", item.ErrorCode);
        Assert.Equal("trace-1", item.TraceId);
    }

    [Fact]
    public async Task FromAfterTo_IsRejected()
    {
        await AssertInvalidAsync(new AuditEventQuery(From: Now, To: Now.AddMinutes(-1)), "from");
    }

    [Fact]
    public async Task UndefinedOutcome_IsRejected()
    {
        await AssertInvalidAsync(new AuditEventQuery(Outcome: (AuditOutcome)42), "outcome");
    }

    [Theory]
    [InlineData("action")]
    [InlineData("targetType")]
    [InlineData("targetId")]
    public async Task OverlongTextFilters_AreRejected(string field)
    {
        var text = new string('x', 65);
        var query = field switch
        {
            "action" => new AuditEventQuery(Action: text),
            "targetType" => new AuditEventQuery(TargetType: text),
            _ => new AuditEventQuery(TargetId: text)
        };

        await AssertInvalidAsync(query, field);
    }

    [Fact]
    public void Paging_DefaultsTo50AndCapsAt100()
    {
        Assert.Equal(50, new AuditEventQuery().NormalizedPageSize);
        Assert.Equal(PagedRequest.MaxPageSize, new AuditEventQuery(PageSize: 1000).NormalizedPageSize);
    }

    [Fact]
    public void RetentionDefaults_Are90DaysDaily()
    {
        var options = new AuditRetentionOptions();

        Assert.True(options.RetentionEnabled);
        Assert.Equal(90, options.RetentionDays);
        Assert.Equal(TimeSpan.FromDays(1), options.RetentionInterval);
    }

    private static async Task AssertInvalidAsync(AuditEventQuery query, string field)
    {
        var repository = new FakeRepository();

        var result = await new AuditEventService(repository).ListAsync(query, CancellationToken.None);

        Assert.Equal(Errors.Validation, result.ErrorCode);
        Assert.Contains(result.ValidationIssues, issue => issue.Field == field);
        Assert.Null(repository.LastQuery);
    }

    private sealed class FakeRepository(params AuditEvent[] events) : IAuditEventRepository
    {
        public AuditEventQuery? LastQuery { get; private set; }

        public Task<PagedResult<AuditEvent>> ListAsync(AuditEventQuery query, CancellationToken cancellationToken)
        {
            LastQuery = query;
            return Task.FromResult(new PagedResult<AuditEvent>(events, query.NormalizedPageNumber, query.NormalizedPageSize, events.Length));
        }

        public Task<int> DeleteOlderThanAsync(DateTimeOffset cutoff, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
