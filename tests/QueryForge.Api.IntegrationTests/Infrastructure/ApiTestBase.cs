using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using QueryForge.Application.ApiDefinitions;
using QueryForge.Domain.ApiDefinitions;

namespace QueryForge.Api.IntegrationTests.Infrastructure;

[Collection(ApiCollection.Name)]
public abstract class ApiTestBase : IAsyncLifetime
{
    protected const string BasePath = "/api/api-definitions";

    protected ApiTestBase(QueryForgeApiFactory factory)
    {
        Factory = factory;
        Client = factory.CreateClient(factory.AdminApiKey);
    }

    protected QueryForgeApiFactory Factory { get; }

    /// <summary>Sends the admin API key, so functional tests exercise the API as an authorized operator.</summary>
    protected HttpClient Client { get; }

    public Task InitializeAsync() => Factory.ResetDatabaseAsync();

    public Task DisposeAsync()
    {
        Client.Dispose();
        return Task.CompletedTask;
    }

    protected static CreateApiDefinitionRequest NewDefinition(string slug, string? name = null, ApiDefinitionStatus status = ApiDefinitionStatus.Active)
    {
        return new CreateApiDefinitionRequest(
            name ?? $"API {slug}",
            slug,
            "Integration test definition.",
            "https://api.example.test",
            status,
            ApiAuthenticationType.BearerToken,
            "https://docs.example.test",
            "integration,test");
    }

    protected static UpdateApiDefinitionRequest UpdateFrom(ApiDefinitionDto current, string? name = null, string? slug = null, string? rowVersion = null)
    {
        return new UpdateApiDefinitionRequest(
            name ?? current.Name,
            slug ?? current.Slug,
            current.Description,
            current.BaseUrl,
            current.Status,
            current.AuthenticationType,
            current.DocumentationUrl,
            current.Tags,
            rowVersion ?? current.RowVersion,
            current.UpstreamSecretName,
            current.ApiKeyHeaderName);
    }

    protected async Task<ApiDefinitionDto> CreateAsync(CreateApiDefinitionRequest request)
    {
        var response = await Client.PostAsJsonAsync(BasePath, request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ApiDefinitionDto>())!;
    }

    protected static string DeleteUri(Guid id, string rowVersion)
    {
        return $"{BasePath}/{id}?rowVersion={Uri.EscapeDataString(rowVersion)}";
    }

    /// <summary>
    /// Compares by JSON content. Needed for DTOs with list properties, whose record equality compares the lists by
    /// reference.
    /// </summary>
    protected static void AssertJsonEqual<T>(T expected, T? actual)
    {
        Assert.NotNull(actual);
        Assert.Equal(JsonSerializer.Serialize(expected), JsonSerializer.Serialize(actual));
    }

    /// <summary>Asserts an RFC 9457 ProblemDetails response and returns its JSON for further checks.</summary>
    protected static async Task<JsonElement> AssertProblemAsync(HttpResponseMessage response, HttpStatusCode expectedStatus)
    {
        Assert.Equal(expectedStatus, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var problem = document.RootElement.Clone();

        Assert.Equal((int)expectedStatus, problem.GetProperty("status").GetInt32());
        Assert.False(string.IsNullOrWhiteSpace(problem.GetProperty("title").GetString()));
        return problem;
    }
}
