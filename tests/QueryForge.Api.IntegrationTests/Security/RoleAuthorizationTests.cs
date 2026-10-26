using System.Net;
using System.Net.Http.Json;
using QueryForge.Api.IntegrationTests.Infrastructure;
using QueryForge.Application.ApiDefinitions;
using QueryForge.Application.ApiOperations;
using QueryForge.Domain.ApiOperations;

namespace QueryForge.Api.IntegrationTests.Security;

/// <summary>
/// Role matrix for the management endpoints: reader reads, admin reads and writes, executor has no access at all.
/// </summary>
public sealed class RoleAuthorizationTests : ApiTestBase
{
    public RoleAuthorizationTests(QueryForgeApiFactory factory)
        : base(factory)
    {
    }

    [Fact]
    public async Task ExecutorKey_IsForbiddenFromEveryManagementEndpoint()
    {
        var (definition, operation) = await SeedAsync();
        using var executor = Factory.CreateClient(Factory.ExecutorApiKey);

        foreach (var request in ManagementRequests(definition, operation))
        {
            var response = await executor.SendAsync(request);

            var problem = await AssertProblemAsync(response, HttpStatusCode.Forbidden);
            Assert.Equal("Forbidden", problem.GetProperty("title").GetString());
        }

        await AssertUnchangedAsync(definition, operation);
    }

    [Fact]
    public async Task ReaderKey_CanReadOperationsButNotChangeThem()
    {
        var (definition, operation) = await SeedAsync();
        using var reader = Factory.CreateClient(Factory.ReaderApiKey);
        var operationPath = $"{OperationsPath(definition.Id)}/{operation.Id}";

        Assert.Equal(HttpStatusCode.OK, (await reader.GetAsync(OperationsPath(definition.Id))).StatusCode);
        AssertJsonEqual(operation, await reader.GetFromJsonAsync<ApiOperationDto>(operationPath));

        var writes = new[]
        {
            await reader.PostAsJsonAsync(OperationsPath(definition.Id), NewOperation("by-reader")),
            await reader.PutAsJsonAsync(operationPath, UpdateFrom(operation)),
            await reader.DeleteAsync($"{operationPath}?rowVersion={Uri.EscapeDataString(operation.RowVersion)}")
        };

        foreach (var response in writes)
        {
            await AssertProblemAsync(response, HttpStatusCode.Forbidden);
        }

        await AssertUnchangedAsync(definition, operation);
    }

    [Fact]
    public async Task AnonymousCaller_IsUnauthorizedOnOperationEndpoints()
    {
        var (definition, operation) = await SeedAsync();
        using var anonymous = Factory.CreateClient(apiKey: null);

        foreach (var request in ManagementRequests(definition, operation).Where(request => request.RequestUri!.OriginalString.Contains("/operations")))
        {
            await AssertProblemAsync(await anonymous.SendAsync(request), HttpStatusCode.Unauthorized);
        }
    }

    [Fact]
    public async Task ExecutorKey_IsNeverWrittenToLogs()
    {
        var (definition, operation) = await SeedAsync();
        using var executor = Factory.CreateClient(Factory.ExecutorApiKey);

        foreach (var request in ManagementRequests(definition, operation))
        {
            await executor.SendAsync(request);
        }

        Assert.DoesNotContain(Factory.ExecutorApiKey, string.Join(Environment.NewLine, Factory.Logs.Messages));
    }

    private async Task<(ApiDefinitionDto Definition, ApiOperationDto Operation)> SeedAsync()
    {
        var definition = await CreateAsync(NewDefinition("role-matrix"));
        var response = await Client.PostAsJsonAsync(OperationsPath(definition.Id), NewOperation("lookup"));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (definition, (await response.Content.ReadFromJsonAsync<ApiOperationDto>())!);
    }

    private async Task AssertUnchangedAsync(ApiDefinitionDto definition, ApiOperationDto operation)
    {
        Assert.Equal(definition, await Client.GetFromJsonAsync<ApiDefinitionDto>($"{BasePath}/{definition.Id}"));
        AssertJsonEqual(new[] { operation }, await Client.GetFromJsonAsync<ApiOperationDto[]>(OperationsPath(definition.Id)));
    }

    private static IEnumerable<HttpRequestMessage> ManagementRequests(ApiDefinitionDto definition, ApiOperationDto operation)
    {
        var definitionPath = $"{BasePath}/{definition.Id}";
        var operationPath = $"{OperationsPath(definition.Id)}/{operation.Id}";

        yield return new HttpRequestMessage(HttpMethod.Get, BasePath);
        yield return new HttpRequestMessage(HttpMethod.Get, definitionPath);
        yield return new HttpRequestMessage(HttpMethod.Post, BasePath) { Content = JsonContent.Create(NewDefinition("forbidden-create")) };
        yield return new HttpRequestMessage(HttpMethod.Put, definitionPath) { Content = JsonContent.Create(UpdateFrom(definition, name: "Changed")) };
        yield return new HttpRequestMessage(HttpMethod.Delete, DeleteUri(definition.Id, definition.RowVersion));

        yield return new HttpRequestMessage(HttpMethod.Get, OperationsPath(definition.Id));
        yield return new HttpRequestMessage(HttpMethod.Get, operationPath);
        yield return new HttpRequestMessage(HttpMethod.Post, OperationsPath(definition.Id)) { Content = JsonContent.Create(NewOperation("forbidden-create")) };
        yield return new HttpRequestMessage(HttpMethod.Put, operationPath) { Content = JsonContent.Create(UpdateFrom(operation)) };
        yield return new HttpRequestMessage(HttpMethod.Delete, $"{operationPath}?rowVersion={Uri.EscapeDataString(operation.RowVersion)}");
    }

    private static string OperationsPath(Guid definitionId) => $"{BasePath}/{definitionId}/operations";

    private static CreateApiOperationRequest NewOperation(string key) =>
        new(key, OperationHttpMethod.Get, "/items", [new OperationParameterRequest("q", ParameterLocation.Query, ParameterType.String, Required: false)]);

    private static UpdateApiOperationRequest UpdateFrom(ApiOperationDto current) =>
        new(current.Key, current.Method, current.PathTemplate, [], 25, current.MaxResponseBytes, current.Enabled, current.RowVersion);
}
