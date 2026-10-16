using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using QueryForge.Api.Health;

namespace QueryForge.Api.Tests;

public sealed class HealthTests
{
    [Fact]
    public async Task ResponseWriter_ReportsEachDependencyIndependently()
    {
        var report = CreateReport(
            HealthStatus.Degraded,
            ("sqlServer", new HealthReportEntry(HealthStatus.Healthy, "SQL Server is reachable.", TimeSpan.FromMilliseconds(12), null, null)),
            ("redis", new HealthReportEntry(HealthStatus.Degraded, "Redis is unreachable.", TimeSpan.FromMilliseconds(3), null, null)));

        using var json = JsonDocument.Parse(await WriteAsync(report));

        Assert.Equal("Degraded", json.RootElement.GetProperty("status").GetString());
        var checks = json.RootElement.GetProperty("checks");
        Assert.Equal("Healthy", checks.GetProperty("sqlServer").GetProperty("status").GetString());
        Assert.Equal("Degraded", checks.GetProperty("redis").GetProperty("status").GetString());
        Assert.Equal("Redis is unreachable.", checks.GetProperty("redis").GetProperty("description").GetString());
    }

    [Fact]
    public async Task ResponseWriter_NeverExposesExceptionDetails()
    {
        const string sensitive = "Login failed for user 'sa'. Password=hunter2";
        var exception = new InvalidOperationException(sensitive);
        var report = CreateReport(
            HealthStatus.Unhealthy,
            // The health check service copies exception messages into the description when a check throws.
            ("sqlServer", new HealthReportEntry(HealthStatus.Unhealthy, sensitive, TimeSpan.FromMilliseconds(5), exception, null)));

        var body = await WriteAsync(report);

        Assert.DoesNotContain("hunter2", body);
        Assert.DoesNotContain("Login failed", body);
        using var json = JsonDocument.Parse(body);
        Assert.Equal("Unhealthy", json.RootElement.GetProperty("checks").GetProperty("sqlServer").GetProperty("status").GetString());
    }

    [Fact]
    public async Task ResponseWriter_KeepsCheckDescriptionWhenExceptionIsOnlyAttachedForLogging()
    {
        var report = CreateReport(
            HealthStatus.Unhealthy,
            ("sqlServer", new HealthReportEntry(
                HealthStatus.Unhealthy,
                "SQL Server is unreachable.",
                TimeSpan.FromMilliseconds(5),
                new InvalidOperationException("Login failed for user 'sa'."),
                null)));

        var body = await WriteAsync(report);

        Assert.DoesNotContain("Login failed", body);
        using var json = JsonDocument.Parse(body);
        Assert.Equal("SQL Server is unreachable.", json.RootElement.GetProperty("checks").GetProperty("sqlServer").GetProperty("description").GetString());
    }

    [Fact]
    public async Task ResponseWriter_ReportsTimeoutWithoutExceptionDetails()
    {
        var report = CreateReport(
            HealthStatus.Degraded,
            ("redis", new HealthReportEntry(HealthStatus.Degraded, "internal timeout detail", TimeSpan.FromSeconds(3), new TaskCanceledException(), null)));

        using var json = JsonDocument.Parse(await WriteAsync(report));

        Assert.Equal("The check timed out.", json.RootElement.GetProperty("checks").GetProperty("redis").GetProperty("description").GetString());
    }

    [Fact]
    public async Task RedisHealthCheck_WhenRedisIsNotConfigured_ReportsHealthy()
    {
        var check = new RedisHealthCheck(multiplexer: null);
        var context = new HealthCheckContext
        {
            Registration = new HealthCheckRegistration("redis", check, HealthStatus.Degraded, tags: null)
        };

        var result = await check.CheckHealthAsync(context);

        Assert.Equal(HealthStatus.Healthy, result.Status);
    }

    private static HealthReport CreateReport(HealthStatus status, params (string Name, HealthReportEntry Entry)[] entries)
    {
        return new HealthReport(
            entries.ToDictionary(entry => entry.Name, entry => entry.Entry),
            status,
            TimeSpan.FromMilliseconds(20));
    }

    private static async Task<string> WriteAsync(HealthReport report)
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        await HealthResponseWriter.WriteAsync(context, report);

        context.Response.Body.Position = 0;
        using var reader = new StreamReader(context.Response.Body);
        return await reader.ReadToEndAsync();
    }
}
