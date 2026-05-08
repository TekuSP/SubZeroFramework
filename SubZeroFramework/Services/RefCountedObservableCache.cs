using System.Reactive.Linq;

namespace SubZeroFramework.Services;

internal sealed class RefCountedObservableCache<TKey, TValue>
    where TKey : notnull
{
    private readonly Lock _syncLock = new();
    private readonly Dictionary<TKey, Entry> _entries = [];

    public IObservable<TValue> GetOrAdd(TKey key, Func<IObservable<TValue>> createObservable)
    {
        ArgumentNullException.ThrowIfNull(createObservable);

        lock (_syncLock)
        {
            if (_entries.TryGetValue(key, out var existingEntry))
            {
                existingEntry.ReferenceCount += 1;
                return existingEntry.Observable;
            }

            IObservable<TValue>? sharedObservable = null;
            sharedObservable = Observable.Create<TValue>(observer =>
            {
                var subscription = createObservable().Subscribe(observer);
                return () =>
                {
                    subscription.Dispose();
                    Release(key);
                };
            })
            .Replay(1)
            .RefCount();

            _entries[key] = new Entry(sharedObservable, 1);
            return sharedObservable;
        }
    }

    private void Release(TKey key)
    {
        lock (_syncLock)
        {
            if (!_entries.TryGetValue(key, out var entry))
            {
                return;
            }

            entry.ReferenceCount -= 1;
            if (entry.ReferenceCount <= 0)
            {
                _entries.Remove(key);
            }
        }
    }

    private sealed class Entry(IObservable<TValue> observable, int referenceCount)
    {
        public IObservable<TValue> Observable { get; } = observable;

        public int ReferenceCount { get; set; } = referenceCount;
    }
}
