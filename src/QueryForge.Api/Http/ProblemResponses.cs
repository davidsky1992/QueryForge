using System.Text.Json;
using Microsoft.AspNetCore.Mvc;

namespace QueryForge.Api.Http;

/// <summary>Writes RFC 9457 ProblemDetails responses outside MVC (authentication and rate limiting).</summary>
public static class ProblemResponses
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public static Task WriteAsync(HttpContext context, int statusCode, string title, string detail)
    {
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/problem+json";

        var problem = new ProblemDetails
        {
            Status = statusCode,
            Title = title,
            Detail = detail
        };

        return JsonSerializer.SerializeAsync(context.Response.Body, problem, SerializerOptions, context.RequestAborted);
    }
}
