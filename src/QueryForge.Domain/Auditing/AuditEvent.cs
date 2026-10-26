namespace QueryForge.Domain.Auditing;

public enum AuditOutcome
{
    Succeeded = 0,
    Failed = 1,
    Rejected = 2
}

/// <summary>
/// One audited action. Holds identifiers and outcomes only: never parameters, request or response bodies, headers,
/// or credentials. <see cref="Actor"/> is the API key name, not the key.
/// </summary>
public sealed class AuditEvent
{
    public const int ActorMaxLength = 64;
    public const int ActionMaxLength = 64;
    public const int TargetTypeMaxLength = 64;
    public const int TargetIdMaxLength = 64;
    public const int ErrorCodeMaxLength = 64;
    public const int TraceIdMaxLength = 64;

    private AuditEvent()
    {
    }

    private AuditEvent(
        Guid id,
        DateTimeOffset occurredAtUtc,
        string actor,
        string action,
        string targetType,
        string? targetId,
        AuditOutcome outcome,
        int? upstreamStatus,
        long? durationMs,
        string? errorCode,
        string? traceId)
    {
        Id = id;
        OccurredAtUtc = occurredAtUtc;
        Actor = actor;
        Action = action;
        TargetType = targetType;
        TargetId = targetId;
        Outcome = outcome;
        UpstreamStatus = upstreamStatus;
        DurationMs = durationMs;
        ErrorCode = errorCode;
        TraceId = traceId;
    }

    public Guid Id { get; private set; }
    public DateTimeOffset OccurredAtUtc { get; private set; }
    public string Actor { get; private set; } = string.Empty;
    public string Action { get; private set; } = string.Empty;
    public string TargetType { get; private set; } = string.Empty;
    public string? TargetId { get; private set; }
    public AuditOutcome Outcome { get; private set; }
    public int? UpstreamStatus { get; private set; }
    public long? DurationMs { get; private set; }
    public string? ErrorCode { get; private set; }
    public string? TraceId { get; private set; }

    public static AuditEvent Record(
        DateTimeOffset occurredAtUtc,
        string actor,
        string action,
        string targetType,
        string? targetId,
        AuditOutcome outcome,
        int? upstreamStatus = null,
        long? durationMs = null,
        string? errorCode = null,
        string? traceId = null)
    {
        // Values come from the application, not from callers, but are bounded so an audit write can never fail
        // on length and lose the event.
        return new AuditEvent(
            Guid.NewGuid(),
            occurredAtUtc,
            Truncate(actor, ActorMaxLength)!,
            Truncate(action, ActionMaxLength)!,
            Truncate(targetType, TargetTypeMaxLength)!,
            Truncate(targetId, TargetIdMaxLength),
            outcome,
            upstreamStatus,
            durationMs,
            Truncate(errorCode, ErrorCodeMaxLength),
            Truncate(traceId, TraceIdMaxLength));
    }

    private static string? Truncate(string? value, int maxLength) =>
        value is null || value.Length <= maxLength ? value : value[..maxLength];
}
