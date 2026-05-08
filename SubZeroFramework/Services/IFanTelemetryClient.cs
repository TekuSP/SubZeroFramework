using DynamicData;

using SubZeroFramework.Models;

namespace SubZeroFramework.Services;

/// <summary>
/// Provides UI-friendly fan telemetry projections on top of the normalized framework telemetry client.
/// </summary>
public interface IFanTelemetryClient
{
    /// <summary>
    /// Watches the current fan telemetry set.
    /// </summary>
    IObservable<IChangeSet<FanTelemetrySnapshot, int>> WatchFans();

    /// <summary>
    /// Watches retained fan speed history for the specified fan index and history window.
    /// </summary>
    IObservable<IChangeSet<FanTelemetrySeriesPoint, long>> WatchFanHistory(int fanIndex, TimeSpan historyWindow);
}
