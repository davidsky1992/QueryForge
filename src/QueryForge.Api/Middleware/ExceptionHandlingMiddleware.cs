using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace QueryForge.Api.Middleware;

public sealed class ExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;
    private readonly IWebHostEnvironment _environment;

    public ExceptionHandlingMiddleware(
        RequestDelegate next,
        ILogger<ExceptionHandlingMiddleware> logger,
        IWebHostEnvironment environment)
    {
        _next = next;
        _logger = logger;
        _environment = environment;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            await WriteProblemAsync(context, exception, HttpStatusCode.Conflict, "The resource was modified by another process.");
        }
        catch (Exception exception)
        {
            await WriteProblemAsync(context, exception, HttpStatusCode.InternalServerError, "An unexpected server error occurred.");
        }
    }

    private async Task WriteProblemAsync(HttpContext context, Exception exception, HttpStatusCode statusCode, string title)
    {
        _logger.LogError(exception, "Unhandled request failure.");

        if (context.Response.HasStarted)
        {
            throw exception;
        }

        context.Response.Clear();
        context.Response.ContentType = "application/problem+json";
        context.Response.StatusCode = (int)statusCode;

        var problem = new ProblemDetails
        {
            Title = title,
            Status = (int)statusCode,
            Detail = _environment.IsDevelopment() ? exception.Message : null
        };

        await JsonSerializer.SerializeAsync(context.Response.Body, problem);
    }
}
