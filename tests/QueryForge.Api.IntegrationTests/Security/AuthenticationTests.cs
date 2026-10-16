using System.Net;
using System.Net.Http.Json;
using QueryForge.Api.IntegrationTests.Infrastructure;
using QueryForge.Application.ApiDefinitions;

namespace QueryForge.Api.IntegrationTests.Security;

public sealed class AuthenticationTests : ApiTestBase
{
    private const string WrongKey = "qf-wrong-key-7f3c9d1e4b2a";

    public AuthenticationTests(QueryForgeApiFactory factory)
        : base(factory)
    {
    }

    public static TheoryData<string, string> ProtectedEndpoints => new()
    {
        { "GET", BasePath },
        { "GET", $"{BasePath}/{Guid.Empty}" },
        { "POST", BasePath },
        { "PUT", $"{BasePath}/{Guid.Empty}" },
        { "DELETE", $"{BasePath}/{Guid.Empty}?rowVersion=AAAAAAAAB9E%3D" }
    };

    [Theory]
    [MemberData(nameof(ProtectedEndpoints))]
    public async Task ProtectedEndpoint_WithoutApiKey_ReturnsUnauthorizedProblem(string method, string path)
    {
        using var anonymous = Factory.CreateClient(apiKey: null);

        var response = await anonymous.SendAsync(CreateRequest(method, path));

        var problem = await AssertProblemAsync(response, HttpStatusCode.Unauthorized);
        Assert.Equal("Authentication required", problem.GetProperty("title").GetString());
        Assert.Contains(response.Headers.WwwAuthenticate, header => header.Scheme == "ApiKey");
    }

    [Theory]
    [MemberData(nameof(ProtectedEndpoints))]
    public async Task ProtectedEndpoint_WithInvalidApiKey_ReturnsUnauthorizedWithoutEchoingKey(string method, string path)
    {
        using var client = Factory.CreateClient(WrongKey);

        var response = await client.SendAsync(CreateRequest(method, path));

        var problem = await AssertProblemAsync(response, HttpStatusCode.Unauthorized);
        Assert.Equal("Authentication required", problem.GetProperty("title").GetString());
        Assert.DoesNotContain(WrongKey, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task InvalidAndMissingKeys_ReceiveIdenticalResponses()
    {
        using var anonymous = Factory.CreateClient(apiKey: null);
        using var wrongKey = Factory.CreateClient(WrongKey);

        var missing = await anonymous.GetAsync(BasePath);
        var invalid = await wrongKey.GetAsync(BasePath);

        // Callers cannot tell a wrong key from a missing one.
        Assert.Equal(await missing.Content.ReadAsStringAsync(), await invalid.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task MultipleApiKeyHeaders_AreRejected()
    {
        using var client = Factory.CreateClient(apiKey: null);
        using var request = new HttpRequestMessage(HttpMethod.Get, BasePath);
        request.Headers.TryAddWithoutValidation("X-Api-Key", [Factory.AdminApiKey, Factory.ReaderApiKey]);

        var response = await client.SendAsync(request);

        await AssertProblemAsync(response, HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task OverlongApiKey_IsRejected()
    {
        using var client = Factory.CreateClient(new string('a', 10_000));

        var response = await client.GetAsync(BasePath);

        await AssertProblemAsync(response, HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ReaderKey_CanReadDefinitions()
    {
        var created = await CreateAsync(NewDefinition("reader-visible-api"));
        using var reader = Factory.CreateClient(Factory.ReaderApiKey);

        var list = await reader.GetAsync(BasePath);
        var single = await reader.GetFromJsonAsync<ApiDefinitionDto>($"{BasePath}/{created.Id}");

        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        Assert.Equal(created, single);
    }

    [Fact]
    public async Task ReaderKey_CannotModifyDefinitions()
    {
        var created = await CreateAsync(NewDefinition("reader-protected-api"));
        using var reader = Factory.CreateClient(Factory.ReaderApiKey);

        var create = await reader.PostAsJsonAsync(BasePath, NewDefinition("reader-created-api"));
        var update = await reader.PutAsJsonAsync($"{BasePath}/{created.Id}", UpdateFrom(created, name: "Changed by reader"));
        var delete = await reader.DeleteAsync(DeleteUri(created.Id, created.RowVersion));

        foreach (var response in new[] { create, update, delete })
        {
            var problem = await AssertProblemAsync(response, HttpStatusCode.Forbidden);
            Assert.Equal("Forbidden", problem.GetProperty("title").GetString());
        }

        // Nothing changed.
        var stored = await Client.GetFromJsonAsync<ApiDefinitionDto>($"{BasePath}/{created.Id}");
        Assert.Equal(created, stored);
        var listAfter = await Client.GetFromJsonAsync<QueryForge.Application.Common.PagedResult<ApiDefinitionDto>>(BasePath);
        Assert.Equal(1, listAfter!.TotalCount);
    }

    [Fact]
    public async Task ApiResponses_IncludeSecurityHeaders()
    {
        var authorized = await Client.GetAsync(BasePath);
        using var anonymous = Factory.CreateClient(apiKey: null);
        var unauthorized = await anonymous.GetAsync(BasePath);

        foreach (var response in new[] { authorized, unauthorized })
        {
            Assert.Equal("nosniff", Assert.Single(response.Headers.GetValues("X-Content-Type-Options")));
            Assert.Equal("DENY", Assert.Single(response.Headers.GetValues("X-Frame-Options")));
            Assert.Equal("no-referrer", Assert.Single(response.Headers.GetValues("Referrer-Policy")));
            Assert.Contains("default-src 'none'", Assert.Single(response.Headers.GetValues("Content-Security-Policy")));
            Assert.True(response.Headers.CacheControl?.NoStore);
        }
    }

    [Fact]
    public async Task ApiKeys_AreNeverWrittenToLogs()
    {
        using var reader = Factory.CreateClient(Factory.ReaderApiKey);
        using var wrongKey = Factory.CreateClient(WrongKey);

        await Client.GetAsync(BasePath);
        await reader.PostAsJsonAsync(BasePath, NewDefinition("log-check-api"));
        await wrongKey.GetAsync(BasePath);

        var logs = string.Join(Environment.NewLine, Factory.Logs.Messages);
        Assert.NotEmpty(Factory.Logs.Messages);
        Assert.DoesNotContain(Factory.AdminApiKey, logs);
        Assert.DoesNotContain(Factory.ReaderApiKey, logs);
        Assert.DoesNotContain(WrongKey, logs);
    }

    private static HttpRequestMessage CreateRequest(string method, string path)
    {
        var request = new HttpRequestMessage(new HttpMethod(method), path);
        if (method is "POST" or "PUT")
        {
            request.Content = JsonContent.Create(NewDefinition("unauthenticated-api"));
        }

        return request;
    }
}
