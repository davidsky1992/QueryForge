using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using QueryForge.Api.IntegrationTests.Infrastructure;
using QueryForge.Domain.ApiDefinitions;

namespace QueryForge.Api.IntegrationTests.ApiDefinitions;

public sealed class ApiDefinitionValidationTests : ApiTestBase
{
    public ApiDefinitionValidationTests(QueryForgeApiFactory factory)
        : base(factory)
    {
    }

    [Fact]
    public async Task Create_WithInvalidFields_ReturnsValidationProblemPerField()
    {
        var request = NewDefinition("Not A Valid Slug") with
        {
            Name = " ",
            BaseUrl = "ftp://files.example.test",
            DocumentationUrl = "not-a-url",
            Status = (ApiDefinitionStatus)99
        };

        var response = await Client.PostAsJsonAsync(BasePath, request);

        var problem = await AssertProblemAsync(response, HttpStatusCode.BadRequest);
        Assert.Equal("One or more validation errors occurred.", problem.GetProperty("title").GetString());
        var errors = problem.GetProperty("errors");
        Assert.True(errors.TryGetProperty("name", out _));
        Assert.True(errors.TryGetProperty("slug", out _));
        Assert.True(errors.TryGetProperty("baseUrl", out _));
        Assert.True(errors.TryGetProperty("documentationUrl", out _));
        Assert.True(errors.TryGetProperty("status", out _));
        Assert.Equal(JsonValueKind.Array, errors.GetProperty("slug").ValueKind);
    }

    [Fact]
    public async Task Create_WithTooLongName_ReturnsValidationProblem()
    {
        var response = await Client.PostAsJsonAsync(BasePath, NewDefinition("long-name-api") with { Name = new string('a', 161) });

        var problem = await AssertProblemAsync(response, HttpStatusCode.BadRequest);
        Assert.True(problem.GetProperty("errors").TryGetProperty("name", out _));
    }

    [Fact]
    public async Task Create_WithMalformedJson_ReturnsBadRequestProblem()
    {
        using var content = new StringContent("{ \"name\": ", Encoding.UTF8, "application/json");

        var response = await Client.PostAsync(BasePath, content);

        await AssertProblemAsync(response, HttpStatusCode.BadRequest);
    }

    [Theory]
    [InlineData("not-base64!")]
    [InlineData("AAAAAAAAAAAAAAAA")] // valid base64, but not an 8-byte rowversion
    public async Task Update_WithMalformedRowVersion_ReturnsValidationProblem(string rowVersion)
    {
        var created = await CreateAsync(NewDefinition("malformed-version-api"));

        var response = await Client.PutAsJsonAsync($"{BasePath}/{created.Id}", UpdateFrom(created, rowVersion: rowVersion));

        var problem = await AssertProblemAsync(response, HttpStatusCode.BadRequest);
        Assert.True(problem.GetProperty("errors").TryGetProperty("rowVersion", out _));
    }

    [Fact]
    public async Task Delete_WithoutRowVersion_ReturnsValidationProblemAndKeepsDefinition()
    {
        var created = await CreateAsync(NewDefinition("unversioned-delete-api"));

        var response = await Client.DeleteAsync($"{BasePath}/{created.Id}");

        var problem = await AssertProblemAsync(response, HttpStatusCode.BadRequest);
        Assert.True(problem.GetProperty("errors").TryGetProperty("rowVersion", out _));
        Assert.Equal(HttpStatusCode.OK, (await Client.GetAsync($"{BasePath}/{created.Id}")).StatusCode);
    }
}
