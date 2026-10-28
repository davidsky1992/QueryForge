using Microsoft.EntityFrameworkCore;
using QueryForge.Application.Auditing;
using QueryForge.Application.Common;
using QueryForge.Domain.Auditing;
using QueryForge.Infrastructure.Persistence;

namespace QueryForge.Infrastructure.Auditing;

internal sealed class EfAuditEventRepository : IAuditEventRepository
{
    private readonly QueryForgeDbContext _dbContext;

    public EfAuditEventRepository(QueryForgeDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<PagedResult<AuditEvent>> ListAsync(AuditEventQuery query, CancellationToken cancellationToken)
    {
        IQueryable<AuditEvent> events = _dbContext.AuditEvents.AsNoTracking();

        if (!string.IsNullOrEmpty(query.Action))
        {
            events = events.Where(item => item.Action == query.Action);
        }

        if (!string.IsNullOrEmpty(query.TargetType))
        {
            events = events.Where(item => item.TargetType == query.TargetType);
        }

        if (!string.IsNullOrEmpty(query.TargetId))
        {
            events = events.Where(item => item.TargetId == query.TargetId);
        }

        if (query.Outcome is { } outcome)
        {
            events = events.Where(item => item.Outcome == outcome);
        }

        if (query.From is { } from)
        {
            events = events.Where(item => item.OccurredAtUtc >= from);
        }

        if (query.To is { } to)
        {
            events = events.Where(item => item.OccurredAtUtc < to);
        }

        var totalCount = await events.CountAsync(cancellationToken);
        var items = await events
            .OrderByDescending(item => item.OccurredAtUtc)
            .ThenBy(item => item.Id)
            .Skip(query.Skip)
            .Take(query.NormalizedPageSize)
            .ToListAsync(cancellationToken);

        return new PagedResult<AuditEvent>(items, query.NormalizedPageNumber, query.NormalizedPageSize, totalCount);
    }

    public Task<int> DeleteOlderThanAsync(DateTimeOffset cutoff, CancellationToken cancellationToken)
    {
        return _dbContext.AuditEvents.Where(item => item.OccurredAtUtc < cutoff).ExecuteDeleteAsync(cancellationToken);
    }
}
