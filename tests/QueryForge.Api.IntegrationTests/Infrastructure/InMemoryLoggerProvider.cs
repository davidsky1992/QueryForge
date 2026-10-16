using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace QueryForge.Api.IntegrationTests.Infrastructure;

/// <summary>Captures every log message (including exceptions) so tests can assert that secrets are never logged.</summary>
public sealed class InMemoryLoggerProvider : ILoggerProvider
{
    private readonly ConcurrentQueue<string> _messages = new();

    public IReadOnlyCollection<string> Messages => _messages;

    public ILogger CreateLogger(string categoryName) => new Logger(categoryName, _messages);

    public void Dispose()
    {
    }

    private sealed class Logger : ILogger
    {
        private readonly string _category;
        private readonly ConcurrentQueue<string> _messages;

        public Logger(string category, ConcurrentQueue<string> messages)
        {
            _category = category;
            _messages = messages;
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            _messages.Enqueue($"{logLevel} {_category}: {formatter(state, exception)} {exception}");
        }
    }
}
