using DynamicData;

using System.Reactive.Concurrency;
using System.Reactive.Disposables;
using System.Reactive.Disposables.Fluent;
using System.Reactive.Linq;
using System.Reactive.Subjects;

namespace SubZeroFramework.Services;

internal sealed class RetainedSnapshotStream<T> : IObservable<T>, IDisposable
    where T : notnull
{
    /// <summary>
    /// Held as ticks so it can be swapped atomically while the publishing loop is running.
    /// </summary>
    /// <remarks>
    /// A <c>TimeSpan</c> field is not guaranteed to be read or written atomically — it wraps a 64-bit tick
    /// count — and this is written from the configuration path while a poll thread reads it.
    /// </remarks>
    private long _retentionWindowTicks;

    private readonly IScheduler _scheduler;
    private readonly CompositeDisposable _subscriptions = [];
    private readonly ReplaySubject<T> _latest = new(1);
    private readonly SourceCache<HistoricalRecord<T>, long> _history = new(record => record.SampleId);
    private long _nextSampleId;
    private bool _disposed;

    public RetainedSnapshotStream(TimeSpan retentionWindow, IScheduler? scheduler = null)
    {
        _retentionWindowTicks = retentionWindow.Ticks;
        _scheduler = scheduler ?? Scheduler.Default;

        // The lambda runs per record, so a later change to the window governs everything added after it.
        // Records already held keep the lifetime they were given — see the setter, which trims those.
        _history
            .ExpireAfter(_ => RetentionWindow, scheduler: _scheduler)
            .Subscribe()
            .DisposeWith(_subscriptions);
    }

    /// <summary>
    /// How long samples are kept. Settable, because it is a user setting rather than a build-time constant.
    /// </summary>
    /// <remarks>
    /// Shrinking it trims immediately rather than waiting for the existing records to age out on the lifetime
    /// they were admitted with. Otherwise reducing retention from an hour to five minutes would leave the
    /// previous hour in memory for another hour — the one outcome somebody lowering it is trying to avoid.
    /// </remarks>
    public TimeSpan RetentionWindow
    {
        get => TimeSpan.FromTicks(Interlocked.Read(ref _retentionWindowTicks));

        set
        {
            if (value <= TimeSpan.Zero || _disposed)
            {
                return;
            }

            var previous = TimeSpan.FromTicks(Interlocked.Exchange(ref _retentionWindowTicks, value.Ticks));

            if (value < previous)
            {
                TrimOlderThan(value);
            }
        }
    }

    private void TrimOlderThan(TimeSpan window)
    {
        var cutoff = DateTimeOffset.UtcNow - window;

        _history.Edit(updater =>
        {
            var expired = updater.Items
                .Where(record => record.ObservedAt < cutoff)
                .Select(record => record.SampleId)
                .ToArray();

            if (expired.Length > 0)
            {
                updater.RemoveKeys(expired);
            }
        });
    }

    public void Publish(T value, DateTimeOffset observedAt)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        _latest.OnNext(value);
        _history.AddOrUpdate(new HistoricalRecord<T>(
            SampleId: Interlocked.Increment(ref _nextSampleId),
            ObservedAt: observedAt,
            Value: value));
    }

    public IObservable<IChangeSet<HistoricalRecord<T>, long>> ConnectHistory(TimeSpan historyWindow)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var retention = RetentionWindow;

        // Clamped rather than thrown: a subscriber asking for an hour on a stream now retaining five minutes
        // has asked for something reasonable that the setting has since made impossible, and throwing would
        // take down a live chart because somebody lowered a number in Settings.
        if (historyWindow <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(historyWindow), $"History window must be greater than {TimeSpan.Zero}.");
        }

        if (historyWindow > retention)
        {
            historyWindow = retention;
        }

        return _history
            .Connect()
            .ExpireAfter(record => GetRemainingLifetime(record.ObservedAt, historyWindow), scheduler: _scheduler);
    }

    public IDisposable Subscribe(IObserver<T> observer)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var subscription = new CompositeDisposable();

        _latest
            .ObserveOn(_scheduler)
            .Subscribe(observer)
            .DisposeWith(subscription);

        return subscription;
    }

    public void Complete()
    {
        if (_disposed)
        {
            return;
        }

        _latest.OnCompleted();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _subscriptions.Dispose();
        _latest.Dispose();
        _history.Dispose();
    }

    private TimeSpan? GetRemainingLifetime(DateTimeOffset observedAt, TimeSpan historyWindow)
    {
        var remainingLifetime = (observedAt + historyWindow) - _scheduler.Now;
        return remainingLifetime > TimeSpan.Zero ? remainingLifetime : TimeSpan.Zero;
    }
}
