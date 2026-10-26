using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using QueryForge.Domain.Auditing;

namespace QueryForge.Infrastructure.Persistence.EntityConfigurations;

internal sealed class AuditEventConfiguration : IEntityTypeConfiguration<AuditEvent>
{
    public void Configure(EntityTypeBuilder<AuditEvent> builder)
    {
        builder.ToTable("AuditEvents");

        builder.HasKey(auditEvent => auditEvent.Id);

        builder.Property(auditEvent => auditEvent.Actor).HasMaxLength(AuditEvent.ActorMaxLength).IsRequired();
        builder.Property(auditEvent => auditEvent.Action).HasMaxLength(AuditEvent.ActionMaxLength).IsRequired();
        builder.Property(auditEvent => auditEvent.TargetType).HasMaxLength(AuditEvent.TargetTypeMaxLength).IsRequired();
        builder.Property(auditEvent => auditEvent.TargetId).HasMaxLength(AuditEvent.TargetIdMaxLength);
        builder.Property(auditEvent => auditEvent.Outcome).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(auditEvent => auditEvent.ErrorCode).HasMaxLength(AuditEvent.ErrorCodeMaxLength);
        builder.Property(auditEvent => auditEvent.TraceId).HasMaxLength(AuditEvent.TraceIdMaxLength);

        builder.HasIndex(auditEvent => auditEvent.OccurredAtUtc);
        builder.HasIndex(auditEvent => new { auditEvent.TargetType, auditEvent.TargetId });
    }
}
