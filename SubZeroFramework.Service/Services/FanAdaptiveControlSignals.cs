using System.Collections.Concurrent;

namespace SubZeroFramework.Service.Services;

/// <summary>
/// A one-way channel from the gRPC surface to the fan curve worker, for actions that touch the ADAPTIVE
/// CONTROLLER'S live state rather than any stored configuration.
/// </summary>
/// <remarks>
/// <para>
/// The controllers — and therefore the integrator and the throttle latch — live inside the worker's serialized
/// evaluation, deliberately: they are not thread-safe, and making them so would hide the fact that two callers
/// stepping the same integrator is a bug. A gRPC handler is on a different thread, so it cannot reach in.
/// </para>
/// <para>
/// So a request is left here and the worker drains it on its next tick. That also gives the right semantics
/// for free: "release the latch" is a request about a control loop that may not even be running, and the
/// worker is the only component that knows.
/// </para>
/// </remarks>
public sealed class FanAdaptiveControlSignals
{
    // A set rather than a queue: releasing a latch twice is the same as releasing it once, so duplicate
    // requests between two ticks must collapse rather than queue up.
    private readonly ConcurrentDictionary<int, byte> _pendingThrottleLatchReleases = new();
    private readonly ConcurrentDictionary<int, byte> _pendingControllerResets = new();

    /// <summary>Asks the worker to clear the throttle latch on a fan at its next evaluation.</summary>
    /// <param name="fanIndex">The fan.</param>
    public void RequestThrottleLatchRelease(int fanIndex) => _pendingThrottleLatchReleases[fanIndex] = 0;

    /// <summary>
    /// Takes a pending latch-release request for a fan, if there is one.
    /// </summary>
    /// <param name="fanIndex">The fan.</param>
    /// <returns>True when a request was pending and has now been consumed.</returns>
    public bool TryConsumeThrottleLatchRelease(int fanIndex) => _pendingThrottleLatchReleases.TryRemove(fanIndex, out _);

    /// <summary>
    /// Asks the worker to discard a fan's running controller, so its estimator restarts empty.
    /// </summary>
    /// <remarks>
    /// Clearing the persisted state is not enough on its own: the live controller holds the fit in memory and
    /// would simply re-publish it on the next accepted sample, undoing the forget within half a minute.
    /// </remarks>
    /// <param name="fanIndex">The fan.</param>
    public void RequestControllerReset(int fanIndex) => _pendingControllerResets[fanIndex] = 0;

    /// <summary>Takes a pending controller-reset request for a fan, if there is one.</summary>
    /// <param name="fanIndex">The fan.</param>
    /// <returns>True when a request was pending and has now been consumed.</returns>
    public bool TryConsumeControllerReset(int fanIndex) => _pendingControllerResets.TryRemove(fanIndex, out _);

    /// <summary>Drops every pending request, for a factory reset or a shutdown.</summary>
    public void Clear()
    {
        _pendingThrottleLatchReleases.Clear();
        _pendingControllerResets.Clear();
    }
}
