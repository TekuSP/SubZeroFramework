using Microsoft.Extensions.Options;

namespace SubZeroFramework.Tests;

/// <summary>
/// An <see cref="IOptionsMonitor{TOptions}"/> whose value can be replaced, raising change notifications.
/// </summary>
/// <remarks>
/// <see cref="OnChange"/> used to return a no-op disposable and never invoke the listener, which meant the
/// configuration-reload path could not be tested at all — and that is precisely where a real defect lived:
/// the service watches the file it writes, so every persisting command re-ran the configured overlay across
/// every fan. Anything depending on a reload must be exercisable here.
/// </remarks>
public sealed class TestOptionsMonitor<TOptions> : IOptionsMonitor<TOptions>
    where TOptions : class
{
    private readonly List<Action<TOptions, string?>> _listeners = [];

    public TestOptionsMonitor(TOptions currentValue)
    {
        ArgumentNullException.ThrowIfNull(currentValue);
        CurrentValue = currentValue;
    }

    public TOptions CurrentValue { get; private set; }

    public TOptions Get(string? name) => CurrentValue;

    /// <summary>Replaces the value and notifies listeners, the way a configuration reload does.</summary>
    public void Set(TOptions value)
    {
        ArgumentNullException.ThrowIfNull(value);
        CurrentValue = value;

        foreach (var listener in _listeners.ToArray())
        {
            listener(value, null);
        }
    }

    /// <summary>Re-raises the change notification without altering the value — a reload of identical content.</summary>
    public void RaiseChanged() => Set(CurrentValue);

    public IDisposable OnChange(Action<TOptions, string?> listener)
    {
        ArgumentNullException.ThrowIfNull(listener);
        _listeners.Add(listener);
        return new Subscription(() => _listeners.Remove(listener));
    }

    private sealed class Subscription(Action onDispose) : IDisposable
    {
        private Action? _onDispose = onDispose;

        public void Dispose()
        {
            var callback = _onDispose;
            _onDispose = null;
            callback?.Invoke();
        }
    }
}
