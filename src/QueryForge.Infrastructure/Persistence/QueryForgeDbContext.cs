using Microsoft.EntityFrameworkCore;
using QueryForge.Domain.ApiDefinitions;
using QueryForge.Domain.ApiOperations;
using QueryForge.Domain.Auditing;

namespace QueryForge.Infrastructure.Persistence;

public sealed class QueryForgeDbContext : DbContext
{
    public QueryForgeDbContext(DbContextOptions<QueryForgeDbContext> options)
        : base(options)
    {
    }

    public DbSet<ApiDefinition> ApiDefinitions => Set<ApiDefinition>();

    public DbSet<ApiOperation> ApiOperations => Set<ApiOperation>();

    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(QueryForgeDbContext).Assembly);
    }
}
