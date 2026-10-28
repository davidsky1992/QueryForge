using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QueryForge.Api.IntegrationTests.Infrastructure;
using QueryForge.Application.ApiDefinitions;
using QueryForge.Domain.ApiDefinitions;
using QueryForge.Infrastructure.Persistence;

namespace QueryForge.Api.IntegrationTests.ApiDefinitions;

/// <summary>
/// ApiKey definitions reference a configured secret by name. The value lives in configuration only: it is never
/// stored in the database, returned by any endpoint or written to logs.
/// </summary>
public sealed class ApiDefinitionSecretReferenceTests : ApiTestBase
{
    public ApiDefinitionSecretReferenceTests(QueryForgeApiFactory factory)
        : base(factory)
    {
    }

    [Fact]
    public async Task SecretReference_RoundTrips_ReportingOnlyWhetherAValueIsConfigured()
    {
        var created = await CreateAsync(KeyedDefinition("weather", QueryForgeApiFactory.UpstreamSecretName));
        var missing = await CreateAsync(KeyedDefinition("other", "not-configured"));

        Assert.Equal(QueryForgeApiFactory.UpstreamSecretName, created.UpstreamSecretName);
        Assert.Equal("X-API-Key", created.ApiKeyHeaderName);
        Assert.True(created.SecretConfigured);
        Assert.False(missing.SecretConfigured);
        Assert.Equal(created, await Client.GetFromJsonAsync<ApiDefinitionDto>($"{BasePath}/{created.Id}"));
    }

    [Fact]
    public async Task SecretValue_NeverAppearsInResponsesDatabaseOrLogs()
    {
        var created = await CreateAsync(KeyedDefinition("weather", QueryForgeApiFactory.UpstreamSecretName));
        using var reader = Factory.CreateClient(Factory.ReaderApiKey);

        var bodies = new[]
        {
            await Client.GetStringAsync($"{BasePath}/{created.Id}"),
            await Client.GetStringAsync(BasePath),
            await reader.GetStringAsync($"{BasePath}/{created.Id}"),
            await (await Client.PutAsJsonAsync($"{BasePath}/{created.Id}", UpdateFrom(created, name: "Renamed"))).Content.ReadAsStringAsync()
        };

        Assert.All(bodies, body => Assert.DoesNotContain(Factory.UpstreamSecretValue, body));
        Assert.DoesNotContain(Factory.UpstreamSecretValue, string.Join(Environment.NewLine, Factory.Logs.Messages));

        // Only the name is persisted.
        var stored = await WithDbContextAsync(dbContext => dbContext.ApiDefinitions.AsNoTracking().SingleAsync(item => item.Id == created.Id));
        Assert.Equal(QueryForgeApiFactory.UpstreamSecretName, stored.UpstreamSecretName);
        Assert.DoesNotContain(Factory.UpstreamSecretValue, System.Text.Json.JsonSerializer.Serialize(stored));
    }

    [Fact]
    public async Task UpdateThatSendsTheReferenceBack_KeepsIt()
    {
        var created = await CreateAsync(KeyedDefinition("weather", QueryForgeApiFactory.UpstreamSecretName));

        var response = await Client.PutAsJsonAsync($"{BasePath}/{created.Id}", UpdateFrom(created, name: "Renamed"));

        var updated = (await response.Content.ReadFromJsonAsync<ApiDefinitionDto>())!;
        Assert.Equal("Renamed", updated.Name);
        Assert.Equal(QueryForgeApiFactory.UpstreamSecretName, updated.UpstreamSecretName);
        Assert.Equal("X-API-Key", updated.ApiKeyHeaderName);
    }

    [Theory]
    [InlineData(ApiAuthenticationType.None, "test-upstream", "X-API-Key", "upstreamSecretName")]
    [InlineData(ApiAuthenticationType.ApiKey, "test-upstream", "Host", "apiKeyHeaderName")]
    [InlineData(ApiAuthenticationType.ApiKey, "test-upstream", null, "apiKeyHeaderName")]
    [InlineData(ApiAuthenticationType.ApiKey, "bad name", "X-API-Key", "upstreamSecretName")]
    public async Task InvalidReferences_AreRejectedPerField(ApiAuthenticationType authentication, string? secretName, string? headerName, string field)
    {
        var request = KeyedDefinition("invalid", secretName) with { AuthenticationType = authentication, ApiKeyHeaderName = headerName };

        var problem = await AssertProblemAsync(await Client.PostAsJsonAsync(BasePath, request), HttpStatusCode.BadRequest);

        Assert.True(problem.GetProperty("errors").TryGetProperty(field, out _));
    }

    private static CreateApiDefinitionRequest KeyedDefinition(string slug, string? secretName) =>
        new($"API {slug}", slug, null, "https://api.example.test", ApiDefinitionStatus.Active, ApiAuthenticationType.ApiKey, null, null,
            secretName, "X-API-Key");

    private async Task<T> WithDbContextAsync<T>(Func<QueryForgeDbContext, Task<T>> action)
    {
        using var scope = Factory.Services.CreateScope();
        return await action(scope.ServiceProvider.GetRequiredService<QueryForgeDbContext>());
    }
}
