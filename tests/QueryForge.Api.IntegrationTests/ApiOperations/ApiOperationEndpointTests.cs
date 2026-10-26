using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QueryForge.Api.IntegrationTests.Infrastructure;
using QueryForge.Application.ApiDefinitions;
using QueryForge.Application.ApiOperations;
using QueryForge.Domain.ApiOperations;
using QueryForge.Domain.Auditing;
using QueryForge.Infrastructure.Persistence;

namespace QueryForge.Api.IntegrationTests.ApiOperations;

public sealed class ApiOperationEndpointTests : ApiTestBase
{
    public ApiOperationEndpointTests(QueryForgeApiFactory factory)
        : base(factory)
    {
    }

    [Fact]
    public async Task Crud_RoundTripsThroughTheApi()
    {
        var definition = await CreateAsync(NewDefinition("weather"));
        var basePath = OperationsPath(definition.Id);

        // Create
        var createResponse = await Client.PostAsJsonAsync(basePath, NewOperation("current-weather"));
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var created = (await createResponse.Content.ReadFromJsonAsync<ApiOperationDto>())!;
        Assert.Equal($"{basePath}/{created.Id}", createResponse.Headers.Location!.AbsolutePath);
        Assert.Equal(definition.Id, created.ApiDefinitionId);
        Assert.Equal(["city", "days"], created.Parameters.Select(parameter => parameter.Name));
        Assert.False(string.IsNullOrEmpty(created.RowVersion));

        // Read
        AssertJsonEqual(created, await Client.GetFromJsonAsync<ApiOperationDto>($"{basePath}/{created.Id}"));
        AssertJsonEqual(new[] { created }, await Client.GetFromJsonAsync<ApiOperationDto[]>(basePath));

        // Update
        var updateResponse = await Client.PutAsJsonAsync($"{basePath}/{created.Id}", UpdateFrom(created, timeoutSeconds: 20));
        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);
        var updated = (await updateResponse.Content.ReadFromJsonAsync<ApiOperationDto>())!;
        Assert.Equal(20, updated.TimeoutSeconds);
        Assert.NotEqual(created.RowVersion, updated.RowVersion);

        // Delete
        var deleteResponse = await Client.DeleteAsync($"{basePath}/{created.Id}?rowVersion={Uri.EscapeDataString(updated.RowVersion)}");
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Client.GetAsync($"{basePath}/{created.Id}")).StatusCode);
    }

    [Fact]
    public async Task UnknownDefinition_ReturnsNotFound()
    {
        var basePath = OperationsPath(Guid.NewGuid());

        await AssertProblemAsync(await Client.GetAsync(basePath), HttpStatusCode.NotFound);
        await AssertProblemAsync(await Client.PostAsJsonAsync(basePath, NewOperation("lookup")), HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task OperationOfAnotherDefinition_IsNotFound()
    {
        var first = await CreateAsync(NewDefinition("first"));
        var second = await CreateAsync(NewDefinition("second"));
        var operation = await CreateOperationAsync(first.Id, "lookup");

        await AssertProblemAsync(await Client.GetAsync($"{OperationsPath(second.Id)}/{operation.Id}"), HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task DuplicateKey_ReturnsConflict()
    {
        var definition = await CreateAsync(NewDefinition("weather"));
        await CreateOperationAsync(definition.Id, "lookup");

        var response = await Client.PostAsJsonAsync(OperationsPath(definition.Id), NewOperation("lookup"));

        await AssertProblemAsync(response, HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task InvalidOperation_ReturnsValidationProblemPerField()
    {
        var definition = await CreateAsync(NewDefinition("weather"));
        var request = NewOperation("lookup") with { PathTemplate = "/v1/../admin", TimeoutSeconds = 99 };

        var problem = await AssertProblemAsync(await Client.PostAsJsonAsync(OperationsPath(definition.Id), request), HttpStatusCode.BadRequest);

        var errors = problem.GetProperty("errors");
        Assert.True(errors.TryGetProperty("pathTemplate", out _));
        Assert.True(errors.TryGetProperty("timeoutSeconds", out _));
    }

    [Fact]
    public async Task StaleUpdateAndDelete_ReturnConcurrencyConflict()
    {
        var definition = await CreateAsync(NewDefinition("weather"));
        var created = await CreateOperationAsync(definition.Id, "lookup");
        var basePath = OperationsPath(definition.Id);
        var updated = await (await Client.PutAsJsonAsync($"{basePath}/{created.Id}", UpdateFrom(created, timeoutSeconds: 15)))
            .Content.ReadFromJsonAsync<ApiOperationDto>();

        var staleUpdate = await Client.PutAsJsonAsync($"{basePath}/{created.Id}", UpdateFrom(created, timeoutSeconds: 25));
        var staleDelete = await Client.DeleteAsync($"{basePath}/{created.Id}?rowVersion={Uri.EscapeDataString(created.RowVersion)}");

        foreach (var response in new[] { staleUpdate, staleDelete })
        {
            var problem = await AssertProblemAsync(response, HttpStatusCode.Conflict);
            Assert.Equal("concurrency_conflict", problem.GetProperty("code").GetString());
        }

        AssertJsonEqual(updated, await Client.GetFromJsonAsync<ApiOperationDto>($"{basePath}/{created.Id}"));
    }

    [Fact]
    public async Task DeletingTheDefinition_RemovesItsOperations()
    {
        var definition = await CreateAsync(NewDefinition("weather"));
        await CreateOperationAsync(definition.Id, "lookup");

        var response = await Client.DeleteAsync(DeleteUri(definition.Id, definition.RowVersion));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        await AssertProblemAsync(await Client.GetAsync(OperationsPath(definition.Id)), HttpStatusCode.NotFound);
        Assert.Equal(0, await WithDbContextAsync(dbContext => dbContext.ApiOperations.CountAsync()));
    }

    [Fact]
    public async Task Changes_AreAuditedWithKeyNameAndOutcome_WithoutRequestContent()
    {
        const string sentinel = "SENTINEL-c41e";
        var definition = await CreateAsync(NewDefinition("weather"));
        var basePath = OperationsPath(definition.Id);
        var request = NewOperation("lookup") with
        {
            Parameters =
            [
                new OperationParameterRequest("city", ParameterLocation.Path, ParameterType.String, Required: true),
                new OperationParameterRequest("q", ParameterLocation.Query, ParameterType.String, false, Default: sentinel)
            ]
        };

        var createResponse = await Client.PostAsJsonAsync(basePath, request);
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var created = (await createResponse.Content.ReadFromJsonAsync<ApiOperationDto>())!;
        await Client.PutAsJsonAsync($"{basePath}/{created.Id}", UpdateFrom(created, timeoutSeconds: 15));
        await Client.PutAsJsonAsync($"{basePath}/{created.Id}", UpdateFrom(created, timeoutSeconds: 25)); // stale
        // Validation failures and forbidden attempts are not audited as changes.
        await Client.PostAsJsonAsync(basePath, NewOperation("bad") with { PathTemplate = "relative" });

        var events = await WithDbContextAsync(dbContext => dbContext.AuditEvents.AsNoTracking().OrderBy(item => item.OccurredAtUtc).ToListAsync());

        Assert.Equal(
            [
                ("operation.created", AuditOutcome.Succeeded),
                ("operation.updated", AuditOutcome.Succeeded),
                ("operation.updated", AuditOutcome.Rejected)
            ],
            events.Select(item => (item.Action, item.Outcome)));
        Assert.All(events, item =>
        {
            Assert.Equal("test-admin", item.Actor);
            Assert.Equal("ApiOperation", item.TargetType);
            Assert.Equal(created.Id.ToString(), item.TargetId);
            Assert.False(string.IsNullOrEmpty(item.TraceId));
        });
        Assert.Equal("concurrency_conflict", events[2].ErrorCode);

        var stored = string.Join("|", events.SelectMany(item => typeof(AuditEvent).GetProperties().Select(property => property.GetValue(item)?.ToString())));
        Assert.DoesNotContain(sentinel, stored);
        Assert.DoesNotContain(Factory.AdminApiKey, stored);
    }

    private static string OperationsPath(Guid definitionId) => $"{BasePath}/{definitionId}/operations";

    private static CreateApiOperationRequest NewOperation(string key) =>
        new(key, OperationHttpMethod.Get, "/v1/forecast/{city}",
        [
            new OperationParameterRequest("city", ParameterLocation.Path, ParameterType.String, Required: true, MaxLength: 64),
            new OperationParameterRequest("days", ParameterLocation.Query, ParameterType.Integer, Required: false, Minimum: 1, Maximum: 7, Default: "3")
        ]);

    private static UpdateApiOperationRequest UpdateFrom(ApiOperationDto current, int? timeoutSeconds = null) =>
        new(current.Key, current.Method, current.PathTemplate,
            current.Parameters.Select(parameter => new OperationParameterRequest(
                parameter.Name, parameter.Location, parameter.Type, parameter.Required, parameter.MaxLength,
                parameter.Minimum, parameter.Maximum, parameter.Pattern, parameter.AllowedValues, parameter.Default)).ToArray(),
            timeoutSeconds ?? current.TimeoutSeconds, current.MaxResponseBytes, current.Enabled, current.RowVersion);

    private async Task<ApiOperationDto> CreateOperationAsync(Guid definitionId, string key)
    {
        var response = await Client.PostAsJsonAsync(OperationsPath(definitionId), NewOperation(key));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ApiOperationDto>())!;
    }

    private async Task<T> WithDbContextAsync<T>(Func<QueryForgeDbContext, Task<T>> action)
    {
        using var scope = Factory.Services.CreateScope();
        return await action(scope.ServiceProvider.GetRequiredService<QueryForgeDbContext>());
    }
}
