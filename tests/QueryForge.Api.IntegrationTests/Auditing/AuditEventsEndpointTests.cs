using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QueryForge.Api.IntegrationTests.Infrastructure;
using QueryForge.Application.Auditing;
using QueryForge.Application.Common;
using QueryForge.Domain.Auditing;
using QueryForge.Infrastructure.Persistence;

namespace QueryForge.Api.IntegrationTests.Auditing;

public sealed class AuditEventsEndpointTests : ApiTestBase
{
    private const string Path = "/api/audit-events";
    private static readonly DateTimeOffset Base = DateTimeOffset.UtcNow.AddHours(-1);

    public AuditEventsEndpointTests(QueryForgeApiFactory factory)
        : base(factory)
    {
    }

    [Fact]
    public async Task Admin_ReadsEventsNewestFirst()
    {
        var older = Event(Base.AddMinutes(-10), "operation.created");
        var newer = Event(Base, "operation.executed");
        await SeedAsync(older, newer);

        var page = await Client.GetFromJsonAsync<PagedResult<AuditEventDto>>(Path);

        Assert.Equal(2, page!.TotalCount);
        Assert.Equal([newer.Id, older.Id], page.Items.Select(item => item.Id));
        Assert.Equal("test-admin", page.Items[0].Actor);
    }

    [Fact]
    public async Task Filters_AreApplied()
    {
        var target = Guid.NewGuid().ToString();
        await SeedAsync(
            Event(Base.AddMinutes(-30), "operation.executed", target, AuditOutcome.Succeeded),
            Event(Base.AddMinutes(-20), "operation.executed", target, AuditOutcome.Failed),
            Event(Base.AddMinutes(-10), "operation.updated", target, AuditOutcome.Succeeded),
            Event(Base, "operation.executed", Guid.NewGuid().ToString(), AuditOutcome.Failed));

        Assert.Equal(3, (await ListAsync("action=operation.executed")).TotalCount);
        Assert.Equal(3, (await ListAsync($"targetId={target}")).TotalCount);
        Assert.Equal(2, (await ListAsync("outcome=Failed")).TotalCount);
        Assert.Equal(1, (await ListAsync($"action=operation.executed&targetId={target}&outcome=Failed")).TotalCount);
        Assert.Equal(4, (await ListAsync("targetType=ApiOperation")).TotalCount);
        Assert.Equal(0, (await ListAsync("targetType=Other")).TotalCount);

        // From is inclusive, To exclusive.
        var from = Uri.EscapeDataString(Base.AddMinutes(-20).ToString("O"));
        var to = Uri.EscapeDataString(Base.ToString("O"));
        Assert.Equal(2, (await ListAsync($"from={from}&to={to}")).TotalCount);
    }

    [Fact]
    public async Task Paging_IsApplied()
    {
        await SeedAsync(Enumerable.Range(0, 5).Select(index => Event(Base.AddMinutes(-index), "operation.executed")).ToArray());

        var second = await ListAsync("pageNumber=2&pageSize=2");

        Assert.Equal(5, second.TotalCount);
        Assert.Equal(3, second.TotalPages);
        Assert.Equal(2, second.Items.Count);
        Assert.Equal(Base.AddMinutes(-2), second.Items[0].OccurredAtUtc);
    }

    [Fact]
    public async Task InvalidFilters_Return400()
    {
        var from = Uri.EscapeDataString(Base.ToString("O"));
        var to = Uri.EscapeDataString(Base.AddMinutes(-1).ToString("O"));

        var problem = await AssertProblemAsync(await Client.GetAsync($"{Path}?from={from}&to={to}"), HttpStatusCode.BadRequest);

        Assert.True(problem.GetProperty("errors").TryGetProperty("from", out _));
    }

    [Fact]
    public async Task OnlyAdminKeys_CanReadTheAuditTrail()
    {
        using var reader = Factory.CreateClient(Factory.ReaderApiKey);
        using var executor = Factory.CreateClient(Factory.ExecutorApiKey);
        using var anonymous = Factory.CreateClient(apiKey: null);

        await AssertProblemAsync(await reader.GetAsync(Path), HttpStatusCode.Forbidden);
        await AssertProblemAsync(await executor.GetAsync(Path), HttpStatusCode.Forbidden);
        await AssertProblemAsync(await anonymous.GetAsync(Path), HttpStatusCode.Unauthorized);
        Assert.Equal(HttpStatusCode.OK, (await Client.GetAsync(Path)).StatusCode);
    }

    [Fact]
    public async Task DeleteOlderThan_RemovesOnlyEventsBeforeTheCutoff()
    {
        var cutoff = Base.AddDays(-30);
        var expired = Event(cutoff.AddTicks(-10), "operation.executed");
        var boundary = Event(cutoff, "operation.executed");
        var recent = Event(Base, "operation.executed");
        await SeedAsync(expired, boundary, recent);

        using var scope = Factory.Services.CreateScope();
        var deleted = await scope.ServiceProvider.GetRequiredService<IAuditEventRepository>().DeleteOlderThanAsync(cutoff, CancellationToken.None);

        Assert.Equal(1, deleted);
        Assert.Equal(new[] { boundary.Id, recent.Id }.Order(), (await RemainingIdsAsync()).Order());
    }

    [Fact]
    public async Task RetentionService_PurgesExpiredEventsOnSchedule()
    {
        var expired = Event(DateTimeOffset.UtcNow.AddDays(-31), "operation.executed");
        var recent = Event(DateTimeOffset.UtcNow.AddDays(-29), "operation.executed");
        await SeedAsync(expired, recent);

        await using var host = Factory.WithHost(new Dictionary<string, string>
        {
            ["Audit:RetentionEnabled"] = "true",
            ["Audit:RetentionDays"] = "30",
            ["Audit:RetentionInterval"] = "00:00:01"
        });
        using var client = host.CreateClient(); // starts the host and its hosted services

        var deadline = DateTime.UtcNow.AddSeconds(15);
        while ((await RemainingIdsAsync()).Contains(expired.Id) && DateTime.UtcNow < deadline)
        {
            await Task.Delay(100);
        }

        Assert.Equal([recent.Id], await RemainingIdsAsync());
        Assert.Contains(Factory.Logs.Messages, message => message.Contains("Deleted 1 audit events older than", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("Audit:RetentionDays", "0")]
    [InlineData("Audit:RetentionDays", "3651")]
    [InlineData("Audit:RetentionInterval", "00:00:00")]
    public async Task InvalidRetentionSettings_StopStartup(string key, string value)
    {
        await using var host = Factory.WithHost(new Dictionary<string, string> { [key] = value });

        var exception = Assert.ThrowsAny<Exception>(() => host.CreateClient());

        Assert.Contains(key, exception.ToString());
    }

    private static AuditEvent Event(DateTimeOffset occurredAt, string action, string? targetId = null, AuditOutcome outcome = AuditOutcome.Succeeded) =>
        AuditEvent.Record(occurredAt, "test-admin", action, "ApiOperation", targetId ?? Guid.NewGuid().ToString(), outcome);

    private async Task<PagedResult<AuditEventDto>> ListAsync(string query) =>
        (await Client.GetFromJsonAsync<PagedResult<AuditEventDto>>($"{Path}?{query}"))!;

    private async Task SeedAsync(params AuditEvent[] events)
    {
        using var scope = Factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<QueryForgeDbContext>();
        dbContext.AuditEvents.AddRange(events);
        await dbContext.SaveChangesAsync();
    }

    private async Task<List<Guid>> RemainingIdsAsync()
    {
        using var scope = Factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<QueryForgeDbContext>().AuditEvents.AsNoTracking().Select(item => item.Id).ToListAsync();
    }
}
