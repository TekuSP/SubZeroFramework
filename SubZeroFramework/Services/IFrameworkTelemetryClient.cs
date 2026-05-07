using DynamicData;

namespace SubZeroFramework.Services;

/// <summary>
/// Provides local gRPC access to normalized telemetry metadata, current values, and retained time-series history.
/// </summary>
public interface IFrameworkTelemetryClient
{
    /// <summary>
    /// Watches the available telemetry channels.
    /// </summary>
    IObservable<IChangeSet<TelemetryChannel, TelemetryChannelId>> WatchTelemetryChannels();

    /// <summary>
    /// Watches the latest current telemetry values.
    /// </summary>
    IObservable<IChangeSet<CurrentTelemetryValue, TelemetryChannelId>> WatchCurrentTelemetryValues();

    /// <summary>
    /// Watches a retained telemetry series for the requested channel and history window.
    /// </summary>
    /// <param name="channelId">The logical telemetry channel identifier.</param>
    /// <param name="historyWindow">The requested history window.</param>
    IObservable<IChangeSet<TelemetryPoint, long>> WatchTelemetrySeries(TelemetryChannelId channelId, TimeSpan historyWindow);
}
