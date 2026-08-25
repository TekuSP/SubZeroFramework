using System.Diagnostics;
using System.Reactive.Disposables;
using System.Reactive.Disposables.Fluent;

namespace SubZeroFramework.Service.Services;

/// <summary>
/// Holds the most recent snapshot from a stream, with the age of the reading, so a pull-style consumer can
/// tell a live value from a frozen one.
/// </summary>
/// <typeparam name="T">The snapshot type. Must be a reference type so the swap below is atomic.</typeparam>
/// <remarks>
/// <para>
/// The provider publishes snapshots rather than exposing them, which suits the workers that react to every
/// reading. A calibration run is the opposite shape: it steps through a script on its own clock and needs to
/// know the machine's state at each of ITS moments, not at each of the EC's.
/// </para>
/// <para>
/// <b>Age is the load-bearing part.</b> A cache that only stores the last value makes a dead stream
/// indistinguishable from a live one — the reading is still there, still plausible, and a consumer polling it
/// keeps acting on a number that stopped being true minutes ago. That matters most to the one caller that
/// deliberately heats the machine: for it, a frozen temperature silently disables both the safety ceiling and
/// the no-readings backstop at exactly the moment they are needed.
/// </para>
/// <para>
/// Faults and completion are recorded too, but neither can be relied on alone: the provider's stream ends
/// cleanly on shutdown and never faults on a failed EC read, so a stall produces no notification at all. Age
/// catches all three.
/// </para>
/// </remarks>
internal sealed class LatestSnapshotCache<T> : IDisposable
    where T : class
{
    private readonly CompositeDisposable _subscriptions = [];
    private readonly Stopwatch _sinceLastValue = Stopwatch.StartNew();

    /// <summary>
    /// Whole references, swapped atomically; never mutated in place.
    /// </summary>
    /// <remarks>
    /// Written from the telemetry thread and read from the consumer's. A reader that sees a snapshot one tick
    /// stale is fine; one that saw a half-written snapshot would read values that never coexisted.
    /// </remarks>
    private volatile T? _latest;

    private volatile Exception? _fault;
    private volatile bool _completed;

    public LatestSnapshotCache(IObservable<T> snapshots)
    {
        ArgumentNullException.ThrowIfNull(snapshots);

        snapshots
            .Subscribe(
                snapshot =>
                {
                    _latest = snapshot;
                    _sinceLastValue.Restart();
                },
                exception => _fault = exception,
                () => _completed = true)
            .DisposeWith(_subscriptions);
    }

    /// <summary>The most recent snapshot, or null if none has arrived yet.</summary>
    public T? Latest => _latest;

    /// <summary>How long ago the most recent snapshot arrived.</summary>
    public TimeSpan Age => _sinceLastValue.Elapsed;

    /// <summary>The error that ended the stream, if one did.</summary>
    public Exception? Fault => _fault;

    /// <summary>True once the stream ended normally — what the provider does on shutdown.</summary>
    public bool IsCompleted => _completed;

    /// <summary>
    /// True when this cache can no longer be trusted to describe the machine right now.
    /// </summary>
    /// <param name="maximumAge">How old a reading may be and still count as current.</param>
    /// <remarks>
    /// A value that has not arrived YET is not stale until the limit has passed — the clock starts at
    /// construction, so the first reading gets exactly the same grace as every later one. Treating "none yet"
    /// as stale outright would abort every consumer on its first poll, before the stream had a chance to
    /// deliver anything.
    /// </remarks>
    public bool IsStale(TimeSpan maximumAge)
        => _fault is not null || _completed || _sinceLastValue.Elapsed > maximumAge;

    public void Dispose() => _subscriptions.Dispose();
}
