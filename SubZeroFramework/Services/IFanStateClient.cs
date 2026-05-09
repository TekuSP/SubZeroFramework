using DynamicData;

namespace SubZeroFramework.Services;

/// <summary>
/// Provides UI-friendly runtime fan-state projections on top of the gRPC telemetry boundary.
/// </summary>
public interface IFanStateClient
{
    /// <summary>
    /// Watches the current runtime fan state set.
    /// </summary>
    IObservable<IChangeSet<FanStateSnapshot, int>> WatchFanStates();
}