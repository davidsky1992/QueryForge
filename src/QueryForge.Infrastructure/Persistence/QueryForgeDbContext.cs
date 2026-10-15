using Microsoft.EntityFrameworkCore;
using QueryForge.Domain.ApiDefinitions;

namespace QueryForge.Infrastructure.Persistence;

public sealed class QueryForgeDbContext : DbContext
{
    public QueryForgeDbContext(DbContextOptions<QueryForgeDbContext> options)
        : base(options)
    {
    }

    public DbSet<ApiDefinition> ApiDefinitions => Set<ApiDefinition>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(QueryForgeDbContext).Assembly);
    }
}
