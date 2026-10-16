using System.Net;
using System.Net.Http.Json;
using QueryForge.Api.IntegrationTests.Infrastructure;
using QueryForge.Api.Security;

namespace QueryForge.Api.IntegrationTests.Security;

/// <summary>
/// Each test uses its own API host with low limits, so limiter state never leaks into other tests.
/// The window is long enough that a limit cannot reset in the middle of a test.
/// </summary>
public sealed class RateLimitingTests : ApiTestBase
{
    private const string WindowSeconds = "300";

    public RateLimitingTests(QueryForgeApiFactory factory)
        : base(factory)
    {
    }

    [Fact]
    public async Task Reads_OverLimit_Return429WithRetryAfterPerApiKey()
    {
        await using var host = Factory.WithSettings(Limit("Reads", 2));
        using var admin = CreateClient(host, Factory.AdminApiKey);
        using var reader = CreateClient(host, Factory.ReaderApiKey);

        var first = await admin.GetAsync(BasePath);
        var second = await admin.GetAsync(BasePath);
        var third = await admin.GetAsync(BasePath);
        var otherKey = await reader.GetAsync(BasePath);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        var problem = await AssertProblemAsync(third, HttpStatusCode.TooManyRequests);
        Assert.Equal("Too many requests", problem.GetProperty("title").GetString());
        Assert.True(third.Headers.RetryAfter?.Delta > TimeSpan.Zero);

        // Limits are partitioned per API key: another key is unaffected.
        Assert.Equal(HttpStatusCode.OK, otherKey.StatusCode);
    }

    [Fact]
    public async Task Writes_HaveTheirOwnLimit()
    {
        await using var host = Factory.WithSettings(Limit("Writes", 1));
        using var admin = CreateClient(host, Factory.AdminApiKey);

        var firstWrite = await admin.PostAsJsonAsync(BasePath, NewDefinition("rate-limited-write-1"));
        var secondWrite = await admin.PostAsJsonAsync(BasePath, NewDefinition("rate-limited-write-2"));
        var read = await admin.GetAsync(BasePath);

        Assert.Equal(HttpStatusCode.Created, firstWrite.StatusCode);
        await AssertProblemAsync(secondWrite, HttpStatusCode.TooManyRequests);
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
    }

    [Fact]
    public async Task UnauthenticatedRequests_AreLimitedBeforeBeingRejected()
    {
        await using var host = Factory.WithSettings(Limit("Anonymous", 2));
        using var anonymous = CreateClient(host, apiKey: null);
        using var admin = CreateClient(host, Factory.AdminApiKey);

        var first = await anonymous.GetAsync(BasePath);
        var second = await anonymous.GetAsync(BasePath);
        var third = await anonymous.GetAsync(BasePath);
        var authenticated = await admin.GetAsync(BasePath);

        Assert.Equal(HttpStatusCode.Unauthorized, first.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, second.StatusCode);
        await AssertProblemAsync(third, HttpStatusCode.TooManyRequests);

        // Authenticated clients use their own per-key partition.
        Assert.Equal(HttpStatusCode.OK, authenticated.StatusCode);
    }

    [Fact]
    public async Task Readiness_IsLimitedButLivenessIsNot()
    {
        await using var host = Factory.WithSettings(Limit("Health", 1));
        using var anonymous = CreateClient(host, apiKey: null);

        var firstReady = await anonymous.GetAsync("/health/ready");
        var secondReady = await anonymous.GetAsync("/health/ready");
        var liveResponses = new List<HttpResponseMessage>();
        for (var attempt = 0; attempt < 5; attempt++)
        {
            liveResponses.Add(await anonymous.GetAsync("/health/live"));
        }

        Assert.Equal(HttpStatusCode.OK, firstReady.StatusCode);
        await AssertProblemAsync(secondReady, HttpStatusCode.TooManyRequests);
        Assert.All(liveResponses, response => Assert.Equal(HttpStatusCode.OK, response.StatusCode));
    }

    private static Dictionary<string, string> Limit(string policy, int permitLimit)
    {
        return new Dictionary<string, string>
        {
            [$"RateLimiting:{policy}:PermitLimit"] = permitLimit.ToString(),
            [$"RateLimiting:{policy}:WindowSeconds"] = WindowSeconds
        };
    }

    private static HttpClient CreateClient(Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> host, string? apiKey)
    {
        var client = host.CreateClient();
        if (apiKey is not null)
        {
            client.DefaultRequestHeaders.Add(ApiKeyAuthenticationDefaults.HeaderName, apiKey);
        }

        return client;
    }
}
