using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using QueryForge.Application.Auditing;
using QueryForge.Domain.Auditing;
using QueryForge.Infrastructure.Persistence;

namespace QueryForge.Infrastructure.Auditing;

/// <summary>
/// Writes each audit event through its own short-lived DbContext, independent of the request's unit of work: the
/// request's context may still hold a change that was just rejected, which a shared SaveChanges would retry.
/// </summary>
internal sealed class EfAuditLog : IAuditLog
{
    private readonly DbContextOptions<QueryForgeDbContext> _options;
    private readonly ILogger<EfAuditLog> _logger;

    public EfAuditLog(DbContextOptions<QueryForgeDbContext> options, ILogger<EfAuditLog> logger)
    {
        _options = options;
        _logger = logger;
    }

    public async Task WriteAsync(AuditEvent auditEvent, CancellationToken cancellationToken)
    {
        try
        {
            await using var dbContext = new QueryForgeDbContext(_options);
            dbContext.AuditEvents.Add(auditEvent);
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // The audited change is already committed, so failing the request would misreport it. The failure is
            // logged with identifiers only, so it can be investigated and reconciled.
            _logger.LogError(
                exception,
                "Audit event {AuditEventId} ({Action} on {TargetType} {TargetId}) could not be written.",
                auditEvent.Id,
                auditEvent.Action,
                auditEvent.TargetType,
                auditEvent.TargetId);
        }
    }
}
