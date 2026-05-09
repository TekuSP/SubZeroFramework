using DynamicData;

namespace SubZeroFramework.Services;

/// <summary>
/// Provides UI-friendly fan capability projections on top of the gRPC telemetry boundary.
/// </summary>
public interface IFanCapabilityClient
{
    /// <summary>
    /// Watches the current fan capability set.
    /// </summary>
    IObservable<IChangeSet<FanCapabilityState, int>> WatchFanCapabilities();
}