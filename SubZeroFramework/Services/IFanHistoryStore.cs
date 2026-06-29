using SubZeroFramework.Models;

namespace SubZeroFramework.Services;

/// <summary>
/// Owns the per-fan and per-sensor telemetry history subscriptions and their point caches for the Fan Control
/// page, so the view model is not the one juggling stream subscriptions. Consumers call the Ensure/Stop/Remove
/// methods to manage subscriptions, read the cached points, and subscribe to the change events to re-render.
/// </summary>
public interface IFanHistoryStore
{
    /// <summary>Raised (on the UI sync context) after a fan's speed history cache updates.</summary>
    event Action<int>? FanHistoryChanged;

    /// <summary>Raised (on the UI sync context) after a sensor's temperature history cache updates.</summary>
    event Action<int>? TemperatureHistoryChanged;

    /// <summary>All cached temperature history, keyed by sensor index.</summary>
    IReadOnlyDictionary<int, TelemetryPoint[]> TemperatureHistory { get; }

    /// <summary>The cached fan-speed history for a fan, or null if none.</summary>
    FanTelemetrySeriesPoint[]? GetFanHistory(int fanIndex);

    /// <summary>Starts watching a fan's speed history (no-op if already watching).</summary>
    void EnsureFanHistory(int fanIndex, TimeSpan range);

    /// <summary>Starts watching a sensor's temperature history (no-op if already watching).</summary>
    void EnsureTemperatureHistory(int sensorIndex, TimeSpan range);

    /// <summary>Stops watching a sensor and drops its cache.</summary>
    void StopTemperatureHistory(int sensorIndex);

    /// <summary>Stops watching a fan and drops its cache.</summary>
    void RemoveFanHistory(int fanIndex);
}
