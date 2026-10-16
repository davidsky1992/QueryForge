using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QueryForge.Infrastructure.Persistence;
using Testcontainers.MsSql;

namespace QueryForge.Api.IntegrationTests.Infrastructure;

/// <summary>
/// Hosts the real API pipeline against a disposable SQL Server container.
/// One container is shared by every test in the <see cref="ApiCollection"/>; tests reset the data before running.
/// </summary>
public sealed class QueryForgeApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    // Same image as docker-compose.yml, so local runs reuse the already-pulled image.
    private const string SqlServerImage = "mcr.microsoft.com/mssql/server:2022-latest";

    private readonly MsSqlContainer _sqlServer = new MsSqlBuilder(SqlServerImage).Build();

    public async Task InitializeAsync()
    {
        await _sqlServer.StartAsync();

        using var scope = Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<QueryForgeDbContext>();
        await dbContext.Database.MigrateAsync();
    }

    public async Task ResetDatabaseAsync()
    {
        using var scope = Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<QueryForgeDbContext>();
        await dbContext.ApiDefinitions.ExecuteDeleteAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // "Testing" keeps developer user secrets, Swagger, and local CORS out of the test host,
        // so tests can never reach a developer database.
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:SqlServer", _sqlServer.GetConnectionString());

        // An empty Redis connection string selects the in-memory distributed cache.
        builder.UseSetting("ConnectionStrings:Redis", string.Empty);
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await base.DisposeAsync();
        await _sqlServer.DisposeAsync();
    }
}
