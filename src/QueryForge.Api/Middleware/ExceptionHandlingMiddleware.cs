using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QueryForge.Application.Common;

namespace QueryForge.Api.Middleware;

public sealed class ExceptionHandlingMiddleware
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

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

    // Each handler is guarded by "when (!HasStarted)": once the response has started it can no longer be replaced,
    // so the exception is left to propagate unchanged with its original stack trace.
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            // The client disconnected; this is not a server error and nobody is left to read a response.
            _logger.LogDebug("Request {Method} {Path} was cancelled by the client.", context.Request.Method, context.Request.Path);

            if (!context.Response.HasStarted)
            {
                context.Response.StatusCode = StatusCodes.Status499ClientClosedRequest;
            }
        }
        catch (Exception exception) when (
            exception is ConcurrencyConflictException or DbUpdateConcurrencyException &&
            !context.Response.HasStarted)
        {
            _logger.LogWarning(exception, "Concurrency conflict while processing {Method} {Path}.", context.Request.Method, context.Request.Path);
            await WriteProblemAsync(context, exception, StatusCodes.Status409Conflict, "The resource was modified by another process.");
        }
        catch (Exception exception) when (!context.Response.HasStarted)
        {
            _logger.LogError(exception, "Unhandled exception while processing {Method} {Path}.", context.Request.Method, context.Request.Path);
            await WriteProblemAsync(context, exception, StatusCodes.Status500InternalServerError, "An unexpected server error occurred.");
        }
    }

    private async Task WriteProblemAsync(HttpContext context, Exception exception, int statusCode, string title)
    {
        context.Response.Clear();
        context.Response.ContentType = "application/problem+json";
        context.Response.StatusCode = statusCode;

        var problem = new ProblemDetails
        {
            Title = title,
            Status = statusCode,
            Detail = _environment.IsDevelopment() ? exception.Message : null
        };

        await JsonSerializer.SerializeAsync(context.Response.Body, problem, SerializerOptions);
    }
}
