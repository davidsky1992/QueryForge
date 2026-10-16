using System.Text.Json;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace QueryForge.Api.Health;

public static class HealthResponseWriter
{
    private const string FailedCheckDescription = "The check failed.";
    private const string TimedOutCheckDescription = "The check timed out.";

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public static Task WriteAsync(HttpContext context, HealthReport report)
    {
        var response = new
        {
            status = report.Status.ToString(),
            totalDurationMs = Math.Round(report.TotalDuration.TotalMilliseconds, 1),
            checks = report.Entries.ToDictionary(
                entry => entry.Key,
                entry => new
                {
                    status = entry.Value.Status.ToString(),
                    description = GetSafeDescription(entry.Value),
                    durationMs = Math.Round(entry.Value.Duration.TotalMilliseconds, 1)
                })
        };

        context.Response.ContentType = "application/json; charset=utf-8";
        return JsonSerializer.SerializeAsync(context.Response.Body, response, SerializerOptions, context.RequestAborted);
    }

    private static string? GetSafeDescription(HealthReportEntry entry)
    {
        return entry.Exception switch
        {
            null => entry.Description,
            OperationCanceledException => TimedOutCheckDescription,
            // When a check throws, the health check service copies the exception message into the description,
            // which may contain connection or credential details. Descriptions written by our checks are fixed text.
            var exception when string.IsNullOrEmpty(entry.Description) || entry.Description == exception.Message => FailedCheckDescription,
            _ => entry.Description
        };
    }
}
