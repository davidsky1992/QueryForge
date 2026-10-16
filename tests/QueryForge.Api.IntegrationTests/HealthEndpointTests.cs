using System.Net;
using System.Text.Json;
using QueryForge.Api.IntegrationTests.Infrastructure;

namespace QueryForge.Api.IntegrationTests;

public sealed class HealthEndpointTests : ApiTestBase
{
    public HealthEndpointTests(QueryForgeApiFactory factory)
        : base(factory)
    {
    }

    [Fact]
    public async Task Live_ReturnsHealthyWithoutCheckingDependencies()
    {
        var response = await Client.GetAsync("/health/live");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("Healthy", json.RootElement.GetProperty("status").GetString());
        Assert.Empty(json.RootElement.GetProperty("checks").EnumerateObject());
    }

    [Fact]
    public async Task Health_ReportsSqlServerContainerAsHealthy()
    {
        var response = await Client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        using var json = JsonDocument.Parse(body);
        var checks = json.RootElement.GetProperty("checks");
        Assert.Equal("Healthy", json.RootElement.GetProperty("status").GetString());
        Assert.Equal("Healthy", checks.GetProperty("sqlServer").GetProperty("status").GetString());

        // The test host disables Redis, which also proves configuration overrides reach the API.
        Assert.Contains("not configured", checks.GetProperty("redis").GetProperty("description").GetString());
        Assert.DoesNotContain("Password", body, StringComparison.OrdinalIgnoreCase);
    }
}
