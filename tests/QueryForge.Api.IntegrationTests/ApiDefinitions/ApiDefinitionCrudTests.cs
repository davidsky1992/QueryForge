using System.Net;
using System.Net.Http.Json;
using QueryForge.Api.IntegrationTests.Infrastructure;
using QueryForge.Application.ApiDefinitions;

namespace QueryForge.Api.IntegrationTests.ApiDefinitions;

public sealed class ApiDefinitionCrudTests : ApiTestBase
{
    public ApiDefinitionCrudTests(QueryForgeApiFactory factory)
        : base(factory)
    {
    }

    [Fact]
    public async Task Create_ReturnsCreatedWithLocationAndNormalizedValues()
    {
        var request = NewDefinition("orders-api") with { Name = "  Orders API  ", Description = "   " };

        var response = await Client.PostAsJsonAsync(BasePath, request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = (await response.Content.ReadFromJsonAsync<ApiDefinitionDto>())!;
        Assert.Equal($"{BasePath}/{created.Id}", response.Headers.Location?.AbsolutePath, ignoreCase: true);
        Assert.Equal("Orders API", created.Name);
        Assert.Null(created.Description);
        Assert.Null(created.UpdatedAtUtc);
        Assert.False(string.IsNullOrEmpty(created.RowVersion));
    }

    [Fact]
    public async Task GetById_ReturnsStoredDefinition()
    {
        var created = await CreateAsync(NewDefinition("billing-api"));

        var fetched = await Client.GetFromJsonAsync<ApiDefinitionDto>($"{BasePath}/{created.Id}");

        Assert.Equal(created, fetched);
    }

    [Fact]
    public async Task Update_PersistsChangesAndIssuesNewRowVersion()
    {
        var created = await CreateAsync(NewDefinition("catalog-api"));

        var response = await Client.PutAsJsonAsync($"{BasePath}/{created.Id}", UpdateFrom(created, name: "Catalog API v2", slug: "catalog-api-v2"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = (await response.Content.ReadFromJsonAsync<ApiDefinitionDto>())!;
        Assert.Equal("Catalog API v2", updated.Name);
        Assert.Equal("catalog-api-v2", updated.Slug);
        Assert.NotNull(updated.UpdatedAtUtc);
        Assert.NotEqual(created.RowVersion, updated.RowVersion);

        var fetched = await Client.GetFromJsonAsync<ApiDefinitionDto>($"{BasePath}/{created.Id}");
        Assert.Equal(updated, fetched);
    }

    [Fact]
    public async Task Delete_RemovesDefinition()
    {
        var created = await CreateAsync(NewDefinition("inventory-api"));

        var response = await Client.DeleteAsync(DeleteUri(created.Id, created.RowVersion));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var getResponse = await Client.GetAsync($"{BasePath}/{created.Id}");
        Assert.Equal(HttpStatusCode.NotFound, getResponse.StatusCode);
    }

    [Fact]
    public async Task Create_WithDuplicateSlug_ReturnsConflictProblem()
    {
        await CreateAsync(NewDefinition("payments-api"));

        // The service checks first; the unique index on Slug backs it up in the database.
        var response = await Client.PostAsJsonAsync(BasePath, NewDefinition("payments-api", name: "Another payments API"));

        var problem = await AssertProblemAsync(response, HttpStatusCode.Conflict);
        Assert.Equal("Conflict", problem.GetProperty("title").GetString());
        Assert.Contains("slug", problem.GetProperty("detail").GetString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Update_ToAnotherDefinitionsSlug_ReturnsConflictProblem()
    {
        await CreateAsync(NewDefinition("users-api"));
        var second = await CreateAsync(NewDefinition("accounts-api"));

        var response = await Client.PutAsJsonAsync($"{BasePath}/{second.Id}", UpdateFrom(second, slug: "users-api"));

        await AssertProblemAsync(response, HttpStatusCode.Conflict);
    }

    [Theory]
    [InlineData("GET")]
    [InlineData("PUT")]
    [InlineData("DELETE")]
    public async Task UnknownId_ReturnsNotFoundProblem(string method)
    {
        var unknownId = Guid.NewGuid();
        var placeholder = await CreateAsync(NewDefinition("placeholder-api"));

        var response = method switch
        {
            "GET" => await Client.GetAsync($"{BasePath}/{unknownId}"),
            "PUT" => await Client.PutAsJsonAsync($"{BasePath}/{unknownId}", UpdateFrom(placeholder)),
            _ => await Client.DeleteAsync(DeleteUri(unknownId, placeholder.RowVersion))
        };

        var problem = await AssertProblemAsync(response, HttpStatusCode.NotFound);
        Assert.Equal("Resource not found", problem.GetProperty("title").GetString());
        Assert.Equal("API definition was not found.", problem.GetProperty("detail").GetString());
    }
}
