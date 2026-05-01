using DynamicData;

using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Reactive.Subjects;

namespace SubZeroFramework.Services;

internal sealed class RetainedSnapshotStream<T> : IObservable<T>, IDisposable
    where T : notnull
{
    private readonly TimeSpan _retentionWindow;
    private readonly IScheduler _scheduler;
    private readonly ReplaySubject<T> _latest = new(1);
    private readonly SourceCache<HistoricalRecord<T>, long> _history = new(record => record.SampleId);
    private readonly IDisposable _historyExpirationSubscription;
    private long _nextSampleId;
    private bool _disposed;

    public RetainedSnapshotStream(TimeSpan retentionWindow, IScheduler? scheduler = null)
    {
        _retentionWindow = retentionWindow;
        _scheduler = scheduler ?? Scheduler.Default;
        _historyExpirationSubscription = _history
            .ExpireAfter(_ => _retentionWindow, scheduler: _scheduler)
            .Subscribe();
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

        if (historyWindow <= TimeSpan.Zero || historyWindow > _retentionWindow)
        {
            throw new ArgumentOutOfRangeException(nameof(historyWindow), $"History window must be between {TimeSpan.Zero} and {_retentionWindow}.");
        }

        return _history
            .Connect()
            .ExpireAfter(record => GetRemainingLifetime(record.ObservedAt, historyWindow), scheduler: _scheduler);
    }

    public IDisposable Subscribe(IObserver<T> observer)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        return _latest.Subscribe(observer);
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
        _historyExpirationSubscription.Dispose();
        _latest.Dispose();
        _history.Dispose();
    }

    private TimeSpan? GetRemainingLifetime(DateTimeOffset observedAt, TimeSpan historyWindow)
    {
        var remainingLifetime = (observedAt + historyWindow) - _scheduler.Now;
        return remainingLifetime > TimeSpan.Zero ? remainingLifetime : TimeSpan.Zero;
    }
}