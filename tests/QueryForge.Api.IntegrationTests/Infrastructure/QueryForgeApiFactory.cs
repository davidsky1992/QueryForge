using System.Security.Cryptography;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using QueryForge.Api.Security;
using QueryForge.Infrastructure.Persistence;
using Testcontainers.MsSql;

namespace QueryForge.Api.IntegrationTests.Infrastructure;

/// <summary>
/// Hosts the real API pipeline against a disposable SQL Server container.
/// One container is shared by every test in the <see cref="ApiCollection"/>; tests reset the data before running.
/// API keys are generated per test run and only their hashes are given to the API, so no credentials exist in source.
/// </summary>
public sealed class QueryForgeApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    // Same image as docker-compose.yml, so local runs reuse the already-pulled image.
    private const string SqlServerImage = "mcr.microsoft.com/mssql/server:2022-latest";

    // High enough that ordinary tests never hit a limit; rate-limit tests use a separate host with low limits.
    private const string UnrestrictedPermitLimit = "100000";

    private readonly MsSqlContainer _sqlServer = new MsSqlBuilder(SqlServerImage).Build();

    public string AdminApiKey { get; } = GenerateApiKey();
    public string ReaderApiKey { get; } = GenerateApiKey();
    public InMemoryLoggerProvider Logs { get; } = new();

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

    /// <summary>Creates a client that sends the given API key, or no key when null.</summary>
    public HttpClient CreateClient(string? apiKey)
    {
        var client = CreateClient();
        if (apiKey is not null)
        {
            client.DefaultRequestHeaders.Add(ApiKeyAuthenticationDefaults.HeaderName, apiKey);
        }

        return client;
    }

    /// <summary>A separate API host on the same database with additional settings (for example low rate limits).</summary>
    public WebApplicationFactory<Program> WithSettings(IReadOnlyDictionary<string, string> settings)
    {
        return WithWebHostBuilder(builder =>
        {
            foreach (var (key, value) in settings)
            {
                builder.UseSetting(key, value);
            }
        });
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // "Testing" keeps developer user secrets, Swagger, and local CORS out of the test host,
        // so tests can never reach a developer database.
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:SqlServer", _sqlServer.GetConnectionString());

        // An empty Redis connection string selects the in-memory distributed cache.
        builder.UseSetting("ConnectionStrings:Redis", string.Empty);

        builder.UseSetting("Security:ApiKeys:0:Name", "test-admin");
        builder.UseSetting("Security:ApiKeys:0:Hash", ApiKeyStore.ComputeHash(AdminApiKey));
        builder.UseSetting("Security:ApiKeys:0:Roles", ApiKeyRoles.Admin);
        builder.UseSetting("Security:ApiKeys:1:Name", "test-reader");
        builder.UseSetting("Security:ApiKeys:1:Hash", ApiKeyStore.ComputeHash(ReaderApiKey));
        builder.UseSetting("Security:ApiKeys:1:Roles", ApiKeyRoles.Reader);

        foreach (var policy in new[] { "Reads", "Writes", "Anonymous", "Health" })
        {
            builder.UseSetting($"RateLimiting:{policy}:PermitLimit", UnrestrictedPermitLimit);
        }

        builder.ConfigureLogging(logging =>
        {
            logging.SetMinimumLevel(LogLevel.Trace);
            logging.AddFilter<InMemoryLoggerProvider>(null, LogLevel.Trace);
            logging.AddProvider(Logs);
        });
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await base.DisposeAsync();
        await _sqlServer.DisposeAsync();
    }

    private static string GenerateApiKey()
    {
        return Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }
}
