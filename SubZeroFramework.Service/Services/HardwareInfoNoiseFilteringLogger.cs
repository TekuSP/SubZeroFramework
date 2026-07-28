using System.Collections.Concurrent;

using Hardware.Info;

using SubZeroFramework.Services;

namespace SubZeroFramework.Service.Services;

internal sealed class HardwareInfoNoiseFilteringLogger : ILogger<HardwareInfo>, IHardwareInfoLogNoiseBuffer
{
    private const string SuppressedExceptionFullName = "WmiLight.InvalidClassException";

    private static readonly AsyncLocal<CaptureContext?> CurrentCapture = new();

    private readonly ILogger<HardwareInfo> _inner;

    public HardwareInfoNoiseFilteringLogger(ILogger<HardwareInfo> inner)
    {
        _inner = inner;
    }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull
        => _inner.BeginScope(state);

    public bool IsEnabled(LogLevel logLevel) => _inner.IsEnabled(logLevel);

    private const string ProcessOutputFailurePrefix = "Failed to read process output: ";

    // External tools Hardware.Info tried to spawn that are not installed. Each is reported ONCE with an
    // actionable hint instead of a stack trace per poll: on Linux, Hardware.Info shells out (lshw for
    // memory/storage, etc.) every refresh, and a missing binary produced two full warning+stacktrace
    // journal entries per cycle, tens of thousands of lines a day (first field report: issue #51).
    private readonly ConcurrentDictionary<string, bool> _reportedMissingTools = new(StringComparer.Ordinal);

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        // ENOENT / ERROR_FILE_NOT_FOUND from spawning an external prober = the tool is not installed.
        // That is a fact about the machine, not a fault worth repeating: say it once, usefully.
        if (exception is System.ComponentModel.Win32Exception { NativeErrorCode: 2 })
        {
            var message = formatter(state, exception);
            if (message.StartsWith(ProcessOutputFailurePrefix, StringComparison.Ordinal))
            {
                var rest = message[ProcessOutputFailurePrefix.Length..];
                var separator = rest.IndexOf(' ');
                var command = separator > 0 ? rest[..separator] : rest;

                if (_reportedMissingTools.TryAdd(command, true))
                {
                    _inner.Log(
                        LogLevel.Warning,
                        eventId,
                        $"The '{command}' tool is not installed, so the hardware details it provides will be missing. Install it with your package manager to enable them. Further '{command}' failures are suppressed.",
                        null,
                        static (formatted, _) => formatted);
                }

                return;
            }
        }

        var capture = CurrentCapture.Value;
        if (capture is not null && IsSuppressible(exception))
        {
            capture.Buffer(new BufferedEntry(logLevel, eventId, formatter(state, exception), exception));
            return;
        }

        _inner.Log(logLevel, eventId, state, exception, formatter);
    }

    public IHardwareInfoNoiseCapture BeginCapture()
    {
        var context = new CaptureContext(this);
        CurrentCapture.Value = context;
        return context;
    }

    private static bool IsSuppressible(Exception? exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (string.Equals(current.GetType().FullName, SuppressedExceptionFullName, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private void FlushBuffered(ConcurrentQueue<BufferedEntry> entries)
    {
        while (entries.TryDequeue(out var entry))
        {
            _inner.Log(entry.Level, entry.EventId, entry.Message, entry.Exception, static (state, _) => state);
        }
    }

    private sealed class CaptureContext : IHardwareInfoNoiseCapture
    {
        private readonly HardwareInfoNoiseFilteringLogger _owner;
        private readonly ConcurrentQueue<BufferedEntry> _buffered = new();
        private int _dataPresent;
        private int _disposed;

        public CaptureContext(HardwareInfoNoiseFilteringLogger owner)
        {
            _owner = owner;
        }

        public void Buffer(BufferedEntry entry) => _buffered.Enqueue(entry);

        public void SetDataPresent(bool dataPresent)
        {
            Interlocked.Exchange(ref _dataPresent, dataPresent ? 1 : 0);
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            if (ReferenceEquals(CurrentCapture.Value, this))
            {
                CurrentCapture.Value = null;
            }

            if (Volatile.Read(ref _dataPresent) == 0)
            {
                _owner.FlushBuffered(_buffered);
            }
        }
    }

    private readonly record struct BufferedEntry(LogLevel Level, EventId EventId, string Message, Exception? Exception);
}
