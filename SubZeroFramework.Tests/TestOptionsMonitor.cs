using Microsoft.Extensions.Options;

namespace SubZeroFramework.Tests;

public sealed class TestOptionsMonitor<TOptions> : IOptionsMonitor<TOptions>
    where TOptions : class
{
    private readonly TOptions _currentValue;

    public TestOptionsMonitor(TOptions currentValue)
    {
        ArgumentNullException.ThrowIfNull(currentValue);
        _currentValue = currentValue;
    }

    public TOptions CurrentValue => _currentValue;

    public TOptions Get(string? name) => _currentValue;

    public IDisposable OnChange(Action<TOptions, string?> listener)
    {
        ArgumentNullException.ThrowIfNull(listener);
        return EmptyDisposable.Instance;
    }

    private sealed class EmptyDisposable : IDisposable
    {
        public static readonly EmptyDisposable Instance = new();

        public void Dispose()
        {
        }
    }
}
