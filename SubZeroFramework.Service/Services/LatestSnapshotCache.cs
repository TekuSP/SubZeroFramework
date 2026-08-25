using System.Reactive.Disposables;
using System.Reactive.Disposables.Fluent;

namespace SubZeroFramework.Service.Services;

/// <summary>
/// Holds the most recent snapshot from a stream so a pull-style consumer can read it on its own schedule.
/// </summary>
/// <typeparam name="T">The snapshot type. Must be a reference type so the swap below is atomic.</typeparam>
/// <remarks>
/// <para>
/// The provider publishes snapshots rather than exposing them, which suits the workers that react to every
/// reading. A calibration run is the opposite shape: it steps through a script on its own clock and needs to
/// know the machine's state at each of ITS moments, not at each of the EC's.
/// </para>
/// <para>
/// Exists as its own type mostly so subscription ownership is honest — the cache lives exactly as long as its
/// subscription, whereas the consumer that reads it may not.
/// </para>
/// </remarks>
internal sealed class LatestSnapshotCache<T> : IDisposable
    where T : class
{
    private readonly CompositeDisposable _subscriptions = [];

    /// <summary>
    /// Whole references, swapped atomically; never mutated in place.
    /// </summary>
    /// <remarks>
    /// Written from the telemetry thread and read from the consumer's. A reader that sees a snapshot one tick
    /// stale is fine; one that saw a half-written snapshot would read values that never coexisted.
    /// </remarks>
    private volatile T? _latest;

    private volatile Exception? _fault;

    public LatestSnapshotCache(IObservable<T> snapshots)
    {
        ArgumentNullException.ThrowIfNull(snapshots);

        snapshots
            .Subscribe(
                snapshot => _latest = snapshot,
                exception => _fault = exception)
            .DisposeWith(_subscriptions);
    }

    /// <summary>The most recent snapshot, or null if none has arrived yet.</summary>
    public T? Latest => _latest;

    /// <summary>
    /// The error that ended the stream, if one did.
    /// </summary>
    /// <remarks>
    /// Surfaced rather than swallowed because <see cref="Latest"/> cannot express the difference on its own:
    /// a dead stream leaves the last snapshot sitting there looking current, and a consumer polling it would
    /// read a value that stopped being true minutes ago.
    /// </remarks>
    public Exception? Fault => _fault;

    public void Dispose() => _subscriptions.Dispose();
}
