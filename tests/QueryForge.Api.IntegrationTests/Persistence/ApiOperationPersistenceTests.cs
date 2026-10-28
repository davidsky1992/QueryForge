using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using QueryForge.Api.IntegrationTests.Infrastructure;
using QueryForge.Application.ApiOperations;
using QueryForge.Application.Common;
using QueryForge.Domain.ApiDefinitions;
using QueryForge.Domain.ApiOperations;
using QueryForge.Domain.Auditing;
using QueryForge.Infrastructure.Persistence;

namespace QueryForge.Api.IntegrationTests.Persistence;

/// <summary>
/// Storage of operations and audit events against SQL Server with the real migrations. No endpoint exists yet;
/// these tests use the repository and DbContext directly.
/// </summary>
public sealed class ApiOperationPersistenceTests : ApiTestBase
{
    private const string InitialMigration = "20261015113404_InitialCreate";
    private const string OperationsMigration = "20261026024029_AddApiOperationsAndAuditEvents";
    private static readonly DateTimeOffset Now = new(2026, 10, 26, 9, 0, 0, TimeSpan.Zero);

    public ApiOperationPersistenceTests(QueryForgeApiFactory factory)
        : base(factory)
    {
    }

    [Fact]
    public async Task Operation_WithParameters_RoundTrips()
    {
        var definitionId = await SeedDefinitionAsync("weather");
        var created = ApiOperation.Create(definitionId, "current-weather", OperationHttpMethod.Get, "/v1/forecast/{city}", 12, 4096, true,
        [
            new OperationParameter("city", ParameterLocation.Path, ParameterType.String, true, 64, null, null, "[A-Za-z ]+", null, null),
            new OperationParameter("days", ParameterLocation.Query, ParameterType.Number, false, null, 0.5m, 7.25m, null, ["1", "2.5"], "1")
        ], Now);

        await WithRepositoryAsync(async repository =>
        {
            await repository.AddAsync(created, CancellationToken.None);
            await repository.SaveChangesAsync(CancellationToken.None);
        });

        var loaded = await WithRepositoryAsync(repository => repository.GetByIdAsync(definitionId, created.Id, CancellationToken.None));

        Assert.NotNull(loaded);
        Assert.Equal("current-weather", loaded.Key);
        Assert.Equal("/v1/forecast/{city}", loaded.PathTemplate);
        Assert.Equal(12, loaded.TimeoutSeconds);
        Assert.Equal(4096, loaded.MaxResponseBytes);
        Assert.Equal(8, loaded.RowVersion.Length);
        Assert.Equal(["city", "days"], loaded.Parameters.Select(parameter => parameter.Name).Order());

        var days = loaded.Parameters.Single(parameter => parameter.Name == "days");
        Assert.Equal(ParameterLocation.Query, days.Location);
        Assert.Equal(ParameterType.Number, days.Type);
        Assert.Equal(0.5m, days.Minimum);
        Assert.Equal(7.25m, days.Maximum);
        Assert.Equal(["1", "2.5"], days.AllowedValues);
        Assert.Equal("1", days.DefaultValue);

        var city = loaded.Parameters.Single(parameter => parameter.Name == "city");
        Assert.Equal("[A-Za-z ]+", city.Pattern);
        Assert.Empty(city.AllowedValues);
    }

    [Fact]
    public async Task FindForExecution_LoadsDefinitionOperationAndParametersBySlugAndKey()
    {
        var weather = await SeedDefinitionAsync("weather");
        var other = await SeedDefinitionAsync("other");
        await AddOperationAsync(weather, "lookup", withParameter: true);
        await AddOperationAsync(other, "lookup");

        var target = await WithRepositoryAsync(repository => repository.FindForExecutionAsync("weather", "lookup", CancellationToken.None));
        var wrongPair = await WithRepositoryAsync(repository => repository.FindForExecutionAsync("weather", "missing", CancellationToken.None));

        Assert.NotNull(target);
        Assert.Equal(weather, target.Definition.Id);
        Assert.Equal(weather, target.Operation.ApiDefinitionId);
        Assert.Equal("https://api.example.test", target.Definition.BaseUrl);
        Assert.Equal(["q"], target.Operation.Parameters.Select(parameter => parameter.Name));
        Assert.Null(wrongPair);
    }

    [Fact]
    public async Task OperationKey_IsUniquePerDefinitionOnly()
    {
        var first = await SeedDefinitionAsync("first");
        var second = await SeedDefinitionAsync("second");
        await AddOperationAsync(first, "lookup");

        // The same key under another definition is fine.
        await AddOperationAsync(second, "lookup");

        // The database rejects a duplicate under the same definition even if the application check is bypassed.
        await Assert.ThrowsAsync<DbUpdateException>(() => AddOperationAsync(first, "lookup"));

        Assert.True(await WithRepositoryAsync(repository => repository.KeyExistsAsync(first, "lookup", null, CancellationToken.None)));
        Assert.False(await WithRepositoryAsync(repository => repository.KeyExistsAsync(first, "other", null, CancellationToken.None)));
    }

    [Fact]
    public async Task KeyExists_IgnoresTheOperationBeingUpdated()
    {
        var definitionId = await SeedDefinitionAsync("weather");
        var operation = await AddOperationAsync(definitionId, "lookup");

        Assert.False(await WithRepositoryAsync(repository => repository.KeyExistsAsync(definitionId, "lookup", operation.Id, CancellationToken.None)));
    }

    [Fact]
    public async Task Operations_AreScopedToTheirDefinition()
    {
        var first = await SeedDefinitionAsync("first");
        var second = await SeedDefinitionAsync("second");
        var operation = await AddOperationAsync(first, "b-op");
        await AddOperationAsync(first, "a-op");
        await AddOperationAsync(second, "c-op");

        var listed = await WithRepositoryAsync(repository => repository.ListByDefinitionAsync(first, CancellationToken.None));
        var throughOtherDefinition = await WithRepositoryAsync(repository => repository.GetByIdAsync(second, operation.Id, CancellationToken.None));

        Assert.Equal(["a-op", "b-op"], listed.Select(item => item.Key));
        Assert.Null(throughOtherDefinition);
        Assert.True(await WithRepositoryAsync(repository => repository.DefinitionExistsAsync(first, CancellationToken.None)));
        Assert.False(await WithRepositoryAsync(repository => repository.DefinitionExistsAsync(Guid.NewGuid(), CancellationToken.None)));
    }

    [Fact]
    public async Task DeletingADefinition_CascadesToOperationsAndParameters()
    {
        var definitionId = await SeedDefinitionAsync("weather");
        await AddOperationAsync(definitionId, "lookup", withParameter: true);

        await WithDbContextAsync(dbContext => dbContext.ApiDefinitions.Where(item => item.Id == definitionId).ExecuteDeleteAsync());

        Assert.Equal(0, await CountRowsAsync("ApiOperations"));
        Assert.Equal(0, await CountRowsAsync("ApiOperationParameters"));
    }

    [Fact]
    public async Task ParameterOnlyChange_BumpsRowVersion_AndAStaleWriterConflicts()
    {
        var definitionId = await SeedDefinitionAsync("weather");
        var operation = await AddOperationAsync(definitionId, "lookup", withParameter: true);
        var originalVersion = operation.RowVersion;

        // First writer changes only the parameters.
        await WithRepositoryAsync(async repository =>
        {
            var current = (await repository.GetByIdAsync(definitionId, operation.Id, CancellationToken.None))!;
            repository.SetOriginalRowVersion(current, originalVersion);
            current.Update(current.Key, current.Method, current.PathTemplate, current.TimeoutSeconds, current.MaxResponseBytes, current.IsEnabled,
                [Parameter("renamed")], Now.AddMinutes(1));
            await repository.SaveChangesAsync(CancellationToken.None);
        });

        var afterFirst = (await WithRepositoryAsync(repository => repository.GetByIdAsync(definitionId, operation.Id, CancellationToken.None)))!;
        Assert.NotEqual(originalVersion, afterFirst.RowVersion);
        Assert.Equal(["renamed"], afterFirst.Parameters.Select(parameter => parameter.Name));
        Assert.Equal(1, await CountRowsAsync("ApiOperationParameters"));

        // Second writer still holds the original version and must be rejected without changing anything.
        await Assert.ThrowsAsync<ConcurrencyConflictException>(() => WithRepositoryAsync(async repository =>
        {
            var current = (await repository.GetByIdAsync(definitionId, operation.Id, CancellationToken.None))!;
            repository.SetOriginalRowVersion(current, originalVersion);
            current.Update(current.Key, current.Method, current.PathTemplate, 30, current.MaxResponseBytes, current.IsEnabled,
                [Parameter("stale")], Now.AddMinutes(2));
            await repository.SaveChangesAsync(CancellationToken.None);
        }));

        var afterSecond = (await WithRepositoryAsync(repository => repository.GetByIdAsync(definitionId, operation.Id, CancellationToken.None)))!;
        Assert.Equal(["renamed"], afterSecond.Parameters.Select(parameter => parameter.Name));
        Assert.Equal(afterFirst.TimeoutSeconds, afterSecond.TimeoutSeconds);
    }

    [Fact]
    public async Task StaleDelete_Conflicts_AndKeepsTheOperation()
    {
        var definitionId = await SeedDefinitionAsync("weather");
        var operation = await AddOperationAsync(definitionId, "lookup");

        await Assert.ThrowsAsync<ConcurrencyConflictException>(() => WithRepositoryAsync(async repository =>
        {
            var current = (await repository.GetByIdAsync(definitionId, operation.Id, CancellationToken.None))!;
            repository.SetOriginalRowVersion(current, [0, 0, 0, 0, 0, 0, 0, 1]);
            repository.Remove(current);
            await repository.SaveChangesAsync(CancellationToken.None);
        }));

        Assert.Equal(1, await CountRowsAsync("ApiOperations"));
    }

    [Fact]
    public async Task AuditEvent_RoundTrips()
    {
        var recorded = AuditEvent.Record(Now, "test-admin", "operation.created", "ApiOperation", Guid.NewGuid().ToString(),
            AuditOutcome.Succeeded, upstreamStatus: 200, durationMs: 143, errorCode: null, traceId: "00-abc-01");

        await WithDbContextAsync(async dbContext =>
        {
            dbContext.AuditEvents.Add(recorded);
            return await dbContext.SaveChangesAsync();
        });

        var loaded = await WithDbContextAsync(dbContext => dbContext.AuditEvents.AsNoTracking().SingleAsync(item => item.Id == recorded.Id));

        Assert.Equal(Now, loaded.OccurredAtUtc);
        Assert.Equal("test-admin", loaded.Actor);
        Assert.Equal("operation.created", loaded.Action);
        Assert.Equal(AuditOutcome.Succeeded, loaded.Outcome);
        Assert.Equal(200, loaded.UpstreamStatus);
        Assert.Equal(143, loaded.DurationMs);
        Assert.Equal("00-abc-01", loaded.TraceId);
    }

    [Fact]
    public async Task Migration_IsAdditive_AndDownRemovesOnlyTheNewTables()
    {
        // A throwaway database in the same container, so the shared test database is not affected.
        var databaseName = $"QueryForgeMigration_{Guid.NewGuid():N}";
        var connectionString = new SqlConnectionStringBuilder(Factory.Services.GetRequiredService<IConfiguration>().GetConnectionString("SqlServer"))
        {
            InitialCatalog = databaseName
        }.ConnectionString;
        await using var dbContext = new QueryForgeDbContext(new DbContextOptionsBuilder<QueryForgeDbContext>().UseSqlServer(connectionString).Options);
        var migrator = dbContext.GetService<IMigrator>();

        try
        {
            // Database as it was before this migration, with existing data.
            await migrator.MigrateAsync(InitialMigration);
            // Inserted with SQL matching the initial schema: the current EF model has columns that do not exist yet.
            var existingId = Guid.NewGuid();
            await dbContext.Database.ExecuteSqlAsync(
                $"INSERT INTO ApiDefinitions (Id, Name, Slug, BaseUrl, Status, AuthenticationType, CreatedAtUtc) VALUES ({existingId}, N'Existing', N'existing', N'https://api.example.test', N'Active', N'None', {Now})");
            Assert.False(await TableExistsAsync(dbContext, "ApiOperations"));

            // Up: new tables appear and the existing row is untouched.
            await migrator.MigrateAsync();
            Assert.True(await TableExistsAsync(dbContext, "ApiOperations"));
            Assert.True(await TableExistsAsync(dbContext, "ApiOperationParameters"));
            Assert.True(await TableExistsAsync(dbContext, "AuditEvents"));
            dbContext.ChangeTracker.Clear();
            var afterUp = await dbContext.ApiDefinitions.AsNoTracking().SingleAsync();
            Assert.Equal(existingId, afterUp.Id);
            Assert.Null(afterUp.UpstreamSecretName);
            Assert.Equal("existing", afterUp.Slug);

            // Down: only the new tables are removed; the definition survives.
            await migrator.MigrateAsync(InitialMigration);
            Assert.False(await TableExistsAsync(dbContext, "ApiOperations"));
            Assert.False(await TableExistsAsync(dbContext, "ApiOperationParameters"));
            Assert.False(await TableExistsAsync(dbContext, "AuditEvents"));
            Assert.True(await TableExistsAsync(dbContext, "ApiDefinitions"));
            Assert.Equal(1, await dbContext.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS [Value] FROM ApiDefinitions").SingleAsync());

            // And Up again from there.
            await migrator.MigrateAsync();
            Assert.True(await TableExistsAsync(dbContext, "ApiOperations"));

            // The upstream secret reference migration only adds two nullable columns, and Down removes only those.
            Assert.True(await ColumnExistsAsync(dbContext, "UpstreamSecretName"));
            Assert.True(await ColumnExistsAsync(dbContext, "ApiKeyHeaderName"));
            await migrator.MigrateAsync(OperationsMigration);
            Assert.False(await ColumnExistsAsync(dbContext, "UpstreamSecretName"));
            Assert.False(await ColumnExistsAsync(dbContext, "ApiKeyHeaderName"));
            Assert.True(await TableExistsAsync(dbContext, "ApiOperations"));
            Assert.Equal(1, await dbContext.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS [Value] FROM ApiDefinitions").SingleAsync());
            await migrator.MigrateAsync();
            Assert.True(await ColumnExistsAsync(dbContext, "UpstreamSecretName"));
        }
        finally
        {
            await dbContext.Database.EnsureDeletedAsync();
        }
    }

    private async Task<Guid> SeedDefinitionAsync(string slug)
    {
        var definition = ApiDefinition.Create($"API {slug}", slug, null, "https://api.example.test", ApiDefinitionStatus.Active,
            ApiAuthenticationType.None, null, null, Now);
        await WithDbContextAsync(async dbContext =>
        {
            dbContext.ApiDefinitions.Add(definition);
            return await dbContext.SaveChangesAsync();
        });

        return definition.Id;
    }

    private async Task<ApiOperation> AddOperationAsync(Guid definitionId, string key, bool withParameter = false)
    {
        var operation = ApiOperation.Create(definitionId, key, OperationHttpMethod.Get, "/items", 10, 1024, true,
            withParameter ? [Parameter("q")] : [], Now);

        await WithRepositoryAsync(async repository =>
        {
            await repository.AddAsync(operation, CancellationToken.None);
            await repository.SaveChangesAsync(CancellationToken.None);
        });

        return operation;
    }

    // Fixed statements per table: identifiers cannot be parameterized, so no SQL is built from strings.
    private Task<int> CountRowsAsync(string table)
    {
        var sql = table switch
        {
            "ApiOperations" => "SELECT COUNT(*) AS [Value] FROM [ApiOperations]",
            "ApiOperationParameters" => "SELECT COUNT(*) AS [Value] FROM [ApiOperationParameters]",
            _ => throw new ArgumentOutOfRangeException(nameof(table), table, null)
        };

        return WithDbContextAsync(dbContext => dbContext.Database.SqlQueryRaw<int>(sql).SingleAsync());
    }

    private static Task<bool> TableExistsAsync(QueryForgeDbContext dbContext, string table) =>
        dbContext.Database.SqlQuery<int>($"SELECT COUNT(*) AS [Value] FROM sys.tables WHERE name = {table}").SingleAsync()
            .ContinueWith(count => count.Result == 1, TaskScheduler.Default);

    private static async Task<bool> ColumnExistsAsync(QueryForgeDbContext dbContext, string column) =>
        await dbContext.Database.SqlQuery<int>(
            $"SELECT COUNT(*) AS [Value] FROM sys.columns WHERE object_id = OBJECT_ID('ApiDefinitions') AND name = {column}").SingleAsync() == 1;

    private async Task WithRepositoryAsync(Func<IApiOperationRepository, Task> action)
    {
        using var scope = Factory.Services.CreateScope();
        await action(scope.ServiceProvider.GetRequiredService<IApiOperationRepository>());
    }

    private async Task<T> WithRepositoryAsync<T>(Func<IApiOperationRepository, Task<T>> action)
    {
        using var scope = Factory.Services.CreateScope();
        return await action(scope.ServiceProvider.GetRequiredService<IApiOperationRepository>());
    }

    private async Task<T> WithDbContextAsync<T>(Func<QueryForgeDbContext, Task<T>> action)
    {
        using var scope = Factory.Services.CreateScope();
        return await action(scope.ServiceProvider.GetRequiredService<QueryForgeDbContext>());
    }

    private static OperationParameter Parameter(string name) =>
        new(name, ParameterLocation.Query, ParameterType.String, false, null, null, null, null, null, null);
}
