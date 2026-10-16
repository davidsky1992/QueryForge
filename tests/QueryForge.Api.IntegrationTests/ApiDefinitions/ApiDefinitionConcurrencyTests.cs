using System.Net;
using System.Net.Http.Json;
using QueryForge.Api.IntegrationTests.Infrastructure;
using QueryForge.Application.ApiDefinitions;

namespace QueryForge.Api.IntegrationTests.ApiDefinitions;

/// <summary>
/// Exercises SQL Server rowversion checks end to end; these cannot be verified with fakes.
/// </summary>
public sealed class ApiDefinitionConcurrencyTests : ApiTestBase
{
    public ApiDefinitionConcurrencyTests(QueryForgeApiFactory factory)
        : base(factory)
    {
    }

    [Fact]
    public async Task SecondWriterWithSameOriginalVersion_GetsConflictAndFirstWriteIsKept()
    {
        var original = await CreateAsync(NewDefinition("shared-api"));
        using var secondClient = Factory.CreateClient();

        // Both clients read version V1; the first writer wins.
        var first = await Client.PutAsJsonAsync($"{BasePath}/{original.Id}", UpdateFrom(original, name: "First writer"));
        var second = await secondClient.PutAsJsonAsync($"{BasePath}/{original.Id}", UpdateFrom(original, name: "Second writer"));

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var problem = await AssertProblemAsync(second, HttpStatusCode.Conflict);
        Assert.Equal("Concurrency conflict", problem.GetProperty("title").GetString());
        Assert.Equal("concurrency_conflict", problem.GetProperty("code").GetString());

        var stored = await Client.GetFromJsonAsync<ApiDefinitionDto>($"{BasePath}/{original.Id}");
        Assert.Equal("First writer", stored!.Name);
    }

    [Fact]
    public async Task UpdateWithLatestVersion_AfterConflict_Succeeds()
    {
        var original = await CreateAsync(NewDefinition("retry-api"));
        await Client.PutAsJsonAsync($"{BasePath}/{original.Id}", UpdateFrom(original, name: "Changed elsewhere"));

        var stale = await Client.PutAsJsonAsync($"{BasePath}/{original.Id}", UpdateFrom(original, name: "Stale edit"));
        var latest = await Client.GetFromJsonAsync<ApiDefinitionDto>($"{BasePath}/{original.Id}");
        var retried = await Client.PutAsJsonAsync($"{BasePath}/{original.Id}", UpdateFrom(latest!, name: "Retried edit"));

        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Equal(HttpStatusCode.OK, retried.StatusCode);
        Assert.Equal("Retried edit", (await retried.Content.ReadFromJsonAsync<ApiDefinitionDto>())!.Name);
    }

    [Fact]
    public async Task DeleteWithStaleVersion_ReturnsConflictAndKeepsDefinition()
    {
        var original = await CreateAsync(NewDefinition("stale-delete-api"));
        var update = await Client.PutAsJsonAsync($"{BasePath}/{original.Id}", UpdateFrom(original, name: "Updated"));
        var updated = (await update.Content.ReadFromJsonAsync<ApiDefinitionDto>())!;

        var staleDelete = await Client.DeleteAsync(DeleteUri(original.Id, original.RowVersion));

        var problem = await AssertProblemAsync(staleDelete, HttpStatusCode.Conflict);
        Assert.Equal("concurrency_conflict", problem.GetProperty("code").GetString());
        var stored = await Client.GetFromJsonAsync<ApiDefinitionDto>($"{BasePath}/{original.Id}");
        Assert.Equal(updated.RowVersion, stored!.RowVersion);
    }

    [Fact]
    public async Task UpdateAfterDeletion_ReturnsNotFound()
    {
        var original = await CreateAsync(NewDefinition("deleted-api"));
        await Client.DeleteAsync(DeleteUri(original.Id, original.RowVersion));

        var response = await Client.PutAsJsonAsync($"{BasePath}/{original.Id}", UpdateFrom(original, name: "Too late"));

        await AssertProblemAsync(response, HttpStatusCode.NotFound);
    }
}
