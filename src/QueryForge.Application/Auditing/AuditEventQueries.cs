using QueryForge.Application.Common;
using QueryForge.Domain.Auditing;

namespace QueryForge.Application.Auditing;

/// <summary>Filters for reading audit events. Text filters match exactly; <c>From</c> is inclusive, <c>To</c> exclusive.</summary>
public sealed record AuditEventQuery(
    int PageNumber = 1,
    int PageSize = 50,
    string? Action = null,
    string? TargetType = null,
    string? TargetId = null,
    AuditOutcome? Outcome = null,
    DateTimeOffset? From = null,
    DateTimeOffset? To = null)
    : PagedRequest(PageNumber, PageSize);

public sealed record AuditEventDto(
    Guid Id,
    DateTimeOffset OccurredAtUtc,
    string Actor,
    string Action,
    string TargetType,
    string? TargetId,
    AuditOutcome Outcome,
    int? UpstreamStatus,
    long? DurationMs,
    string? ErrorCode,
    string? TraceId);

public interface IAuditEventRepository
{
    /// <summary>Newest first.</summary>
    Task<PagedResult<AuditEvent>> ListAsync(AuditEventQuery query, CancellationToken cancellationToken);

    /// <summary>Deletes events that occurred before <paramref name="cutoff"/> and returns how many were removed.</summary>
    Task<int> DeleteOlderThanAsync(DateTimeOffset cutoff, CancellationToken cancellationToken);
}

public interface IAuditEventService
{
    Task<Result<PagedResult<AuditEventDto>>> ListAsync(AuditEventQuery query, CancellationToken cancellationToken);
}

public sealed class AuditEventService : IAuditEventService
{
    private const int TextFilterMaxLength = 64;

    private readonly IAuditEventRepository _repository;

    public AuditEventService(IAuditEventRepository repository)
    {
        _repository = repository;
    }

    public async Task<Result<PagedResult<AuditEventDto>>> ListAsync(AuditEventQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var issues = new List<ValidationIssue>();
        foreach (var (field, value) in new[] { ("action", query.Action), ("targetType", query.TargetType), ("targetId", query.TargetId) })
        {
            if (value?.Length > TextFilterMaxLength)
            {
                issues.Add(new ValidationIssue(field, $"{field} must be {TextFilterMaxLength} characters or fewer."));
            }
        }

        if (query.Outcome is { } outcome && !Enum.IsDefined(outcome))
        {
            issues.Add(new ValidationIssue("outcome", "outcome is not supported."));
        }

        if (query.From > query.To)
        {
            issues.Add(new ValidationIssue("from", "from must not be later than to."));
        }

        if (issues.Count > 0)
        {
            return Result<PagedResult<AuditEventDto>>.Invalid(issues);
        }

        var page = await _repository.ListAsync(query, cancellationToken);
        var items = page.Items.Select(ToDto).ToArray();
        return Result<PagedResult<AuditEventDto>>.Success(new PagedResult<AuditEventDto>(items, page.PageNumber, page.PageSize, page.TotalCount));
    }

    private static AuditEventDto ToDto(AuditEvent auditEvent) => new(
        auditEvent.Id,
        auditEvent.OccurredAtUtc,
        auditEvent.Actor,
        auditEvent.Action,
        auditEvent.TargetType,
        auditEvent.TargetId,
        auditEvent.Outcome,
        auditEvent.UpstreamStatus,
        auditEvent.DurationMs,
        auditEvent.ErrorCode,
        auditEvent.TraceId);
}

/// <summary>Retention settings for audit events, bound from the <c>Audit</c> configuration section.</summary>
public sealed class AuditRetentionOptions
{
    public const string SectionName = "Audit";

    public bool RetentionEnabled { get; set; } = true;

    public int RetentionDays { get; set; } = 90;

    /// <summary>How often expired events are purged.</summary>
    public TimeSpan RetentionInterval { get; set; } = TimeSpan.FromDays(1);
}
