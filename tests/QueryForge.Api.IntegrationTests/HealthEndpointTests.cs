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

    [Theory]
    [InlineData("/health/live")]
    [InlineData("/health/ready")]
    public async Task PublicProbes_WithoutApiKey_ReturnOnlyOverallStatus(string path)
    {
        using var anonymous = Factory.CreateClient(apiKey: null);

        var response = await anonymous.GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        using var json = JsonDocument.Parse(body);
        Assert.Equal("Healthy", json.RootElement.GetProperty("status").GetString());

        // Nothing beyond the status: no dependency names, descriptions, or timings.
        Assert.Single(json.RootElement.EnumerateObject());
        Assert.DoesNotContain("sqlServer", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("redis", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DetailedHealth_WithAdminKey_ReportsSqlServerContainerAsHealthy()
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

    [Fact]
    public async Task DetailedHealth_WithoutApiKey_ReturnsUnauthorized()
    {
        using var anonymous = Factory.CreateClient(apiKey: null);

        var response = await anonymous.GetAsync("/health");

        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.DoesNotContain("sqlServer", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DetailedHealth_WithReaderKey_ReturnsForbidden()
    {
        using var reader = Factory.CreateClient(Factory.ReaderApiKey);

        var response = await reader.GetAsync("/health");

        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.DoesNotContain("sqlServer", body, StringComparison.OrdinalIgnoreCase);
    }
}
