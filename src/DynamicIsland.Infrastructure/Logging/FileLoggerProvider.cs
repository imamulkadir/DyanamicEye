using DynamicIsland.Core.Diagnostics;
using Microsoft.Extensions.Logging;

namespace DynamicIsland.Infrastructure.Logging;

/// <summary>
/// Local rolling JSON-lines log sink (guide §2 Logging, §4.3). Callers only enqueue; a background writer
/// redacts, writes and rotates. I/O failure disables the sink instead of disturbing the app.
/// </summary>
[ProviderAlias("File")]
public sealed class FileLoggerProvider : ILoggerProvider
{
    private readonly FileLoggerOptions _options;
    private readonly RollingLogWriter _writer;

    public FileLoggerProvider(FileLoggerOptions options, LogRedactor redactor, TimeProvider timeProvider)
        : this(options, redactor, timeProvider, startGate: null)
    {
    }

    internal FileLoggerProvider(FileLoggerOptions options, LogRedactor redactor, TimeProvider timeProvider, Task? startGate)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
        _writer = new RollingLogWriter(options, redactor, timeProvider, startGate);
    }

    public ILogger CreateLogger(string categoryName) => new FileLogger(categoryName, _options.MinimumLevel, _writer);

    public void Dispose() => _writer.Dispose();

    private sealed class FileLogger(string category, LogLevel minimumLevel, RollingLogWriter writer) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) =>
            logLevel != LogLevel.None && logLevel >= minimumLevel && writer.IsEnabled;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            ArgumentNullException.ThrowIfNull(formatter);
            if (!IsEnabled(logLevel))
            {
                return;
            }

            writer.Enqueue(new LogEntry(writer.UtcNow, logLevel, category, eventId, formatter(state, exception), exception is null ? null : ExceptionText.Describe(exception)));
        }
    }
}
