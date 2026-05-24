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

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
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
