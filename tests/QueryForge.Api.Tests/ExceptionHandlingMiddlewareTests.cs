using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using QueryForge.Api.Middleware;
using QueryForge.Application.Common;

namespace QueryForge.Api.Tests;

public sealed class ExceptionHandlingMiddlewareTests
{
    [Fact]
    public async Task UnhandledException_InProduction_Returns500WithoutExceptionDetails()
    {
        var logger = new RecordingLogger();
        var middleware = CreateMiddleware(_ => throw new InvalidOperationException("secret connection detail"), logger, Environments.Production);
        var context = CreateContext();

        await middleware.InvokeAsync(context);

        Assert.Equal(StatusCodes.Status500InternalServerError, context.Response.StatusCode);
        Assert.Equal("application/problem+json", context.Response.ContentType);
        var body = await ReadBodyAsync(context);
        Assert.DoesNotContain("secret connection detail", body);
        using var json = JsonDocument.Parse(body);
        Assert.Equal("An unexpected server error occurred.", json.RootElement.GetProperty("title").GetString());
        Assert.Contains(logger.Entries, entry => entry.Level == LogLevel.Error);
    }

    [Fact]
    public async Task ConcurrencyConflict_Returns409AndLogsWarning()
    {
        var logger = new RecordingLogger();
        var middleware = CreateMiddleware(
            _ => throw new ConcurrencyConflictException("stale", new InvalidOperationException()),
            logger,
            Environments.Production);
        var context = CreateContext();

        await middleware.InvokeAsync(context);

        Assert.Equal(StatusCodes.Status409Conflict, context.Response.StatusCode);
        Assert.Contains(logger.Entries, entry => entry.Level == LogLevel.Warning);
        Assert.DoesNotContain(logger.Entries, entry => entry.Level >= LogLevel.Error);
    }

    [Fact]
    public async Task ClientDisconnect_IsNotLoggedAsError()
    {
        var logger = new RecordingLogger();
        using var aborted = new CancellationTokenSource();
        var middleware = CreateMiddleware(context =>
        {
            aborted.Cancel();
            context.RequestAborted.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }, logger, Environments.Production);
        var context = CreateContext();
        context.RequestAborted = aborted.Token;

        await middleware.InvokeAsync(context);

        Assert.Equal(StatusCodes.Status499ClientClosedRequest, context.Response.StatusCode);
        Assert.DoesNotContain(logger.Entries, entry => entry.Level >= LogLevel.Warning);
    }

    [Fact]
    public async Task CancellationNotCausedByClient_IsTreatedAsServerError()
    {
        var logger = new RecordingLogger();
        var middleware = CreateMiddleware(_ => throw new OperationCanceledException(), logger, Environments.Production);
        var context = CreateContext();

        await middleware.InvokeAsync(context);

        Assert.Equal(StatusCodes.Status500InternalServerError, context.Response.StatusCode);
        Assert.Contains(logger.Entries, entry => entry.Level == LogLevel.Error);
    }

    [Fact]
    public async Task ExceptionAfterResponseStarted_PropagatesOriginalException()
    {
        var original = new InvalidOperationException("boom");
        var middleware = CreateMiddleware(_ => throw original, new RecordingLogger(), Environments.Production);
        var context = CreateContext(responseHasStarted: true);

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() => middleware.InvokeAsync(context));

        Assert.Same(original, thrown);
        Assert.Contains(nameof(ExceptionAfterResponseStarted_PropagatesOriginalException), thrown.StackTrace);
    }

    private static ExceptionHandlingMiddleware CreateMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger, string environment)
    {
        return new ExceptionHandlingMiddleware(next, logger, new TestHostEnvironment(environment));
    }

    private static DefaultHttpContext CreateContext(bool responseHasStarted = false)
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        if (responseHasStarted)
        {
            context.Features.Set<Microsoft.AspNetCore.Http.Features.IHttpResponseFeature>(new StartedResponseFeature());
        }

        return context;
    }

    private static async Task<string> ReadBodyAsync(HttpContext context)
    {
        context.Response.Body.Position = 0;
        using var reader = new StreamReader(context.Response.Body);
        return await reader.ReadToEndAsync();
    }

    private sealed class StartedResponseFeature : Microsoft.AspNetCore.Http.Features.HttpResponseFeature
    {
        public override bool HasStarted => true;
    }

    private sealed class TestHostEnvironment : IWebHostEnvironment
    {
        public TestHostEnvironment(string environmentName)
        {
            EnvironmentName = environmentName;
        }

        public string EnvironmentName { get; set; }
        public string ApplicationName { get; set; } = "QueryForge.Api.Tests";
        public string WebRootPath { get; set; } = string.Empty;
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string ContentRootPath { get; set; } = string.Empty;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private sealed class RecordingLogger : ILogger<ExceptionHandlingMiddleware>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            Entries.Add((logLevel, formatter(state, exception)));
        }
    }
}
