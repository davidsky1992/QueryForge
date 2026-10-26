using QueryForge.Domain.Auditing;

namespace QueryForge.Application.Auditing;

/// <summary>
/// Records audit events. Implementations write independently of the caller's unit of work, so an event is recorded
/// even when the audited change itself was rejected (for example by a concurrency conflict).
/// </summary>
public interface IAuditLog
{
    Task WriteAsync(AuditEvent auditEvent, CancellationToken cancellationToken);
}

/// <summary>Who is acting and how the request can be correlated, as known to the host.</summary>
public interface IRequestContext
{
    /// <summary>The authenticated API key name (never the key itself).</summary>
    string Actor { get; }

    string? TraceId { get; }
}

public static class AuditActions
{
    public const string OperationCreated = "operation.created";
    public const string OperationUpdated = "operation.updated";
    public const string OperationDeleted = "operation.deleted";
    public const string OperationExecuted = "operation.executed";
}

public static class AuditTargets
{
    public const string ApiOperation = "ApiOperation";
}
