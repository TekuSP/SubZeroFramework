namespace SubZeroFramework.Services;

/// <summary>
/// Logging provider that mirrors every log entry into an <see cref="InMemoryLogBuffer"/> so a process can
/// display its own logs. Sits alongside the platform sinks (Event Log / journald / console) rather than
/// replacing them.
/// </summary>
[ProviderAlias("InMemory")]
public sealed class InMemoryLogProvider : ILoggerProvider
{
    private readonly InMemoryLogBuffer _buffer;

    public InMemoryLogProvider(InMemoryLogBuffer buffer)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        _buffer = buffer;
    }

    public ILogger CreateLogger(string categoryName) => new BufferLogger(_buffer, categoryName);

    public void Dispose()
    {
        // Nothing to release: the buffer outlives the provider and is owned by the container.
    }

    private sealed class BufferLogger : ILogger
    {
        private readonly InMemoryLogBuffer _buffer;
        private readonly string _category;

        public BufferLogger(InMemoryLogBuffer buffer, string category)
        {
            _buffer = buffer;
            _category = category;
        }

        // Scopes are not recorded: the buffer is a flat, human-readable list, and holding scope state per
        // entry would cost more than it tells the reader.
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        // The level filter is applied by the logging pipeline before Log is called, so honoring every level
        // here keeps the buffer consistent with what the platform sinks received.
        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            ArgumentNullException.ThrowIfNull(formatter);

            if (!IsEnabled(logLevel))
            {
                return;
            }

            // A throwing formatter must never take the process down over a log line.
            string message;
            try
            {
                message = formatter(state, exception);
            }
            catch (Exception formatterException)
            {
                message = $"<log message could not be formatted: {formatterException.Message}>";
            }

            _buffer.Add(new ServiceLogEntry
            {
                ObservedAt = DateTimeOffset.UtcNow,
                Level = logLevel,
                Category = _category,
                Message = message,
                Exception = exception?.ToString() ?? string.Empty,
            });
        }
    }
}
