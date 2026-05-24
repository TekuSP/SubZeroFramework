using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Subjects;

namespace SubZeroFramework.Service.Services;

/// <summary>
/// Serializes asynchronous work items through a reactive pipeline (<see cref="Subject{T}"/> + <see cref="Observable"/>.<c>Concat</c>),
/// replacing classic <see cref="SemaphoreSlim"/>(1, 1) gates around <c>await</c>-heavy critical sections.
/// Enqueued work runs strictly in arrival order; callers receive a <see cref="Task"/> that completes
/// when their work item finishes (or faults/cancels).
/// </summary>
public sealed class ReactiveRequestQueue : IDisposable
{
    private readonly ISubject<Func<Task>> _requests;
    private readonly CompositeDisposable _subscriptions = new();
    private int _disposed;

    public ReactiveRequestQueue()
    {
        _requests = Subject.Synchronize(new Subject<Func<Task>>());
        _subscriptions.Add(_requests
            .Select(work => Observable.FromAsync(work))
            .Concat()
            .Subscribe(static _ => { }, static _ => { }));
    }

    public Task<T> EnqueueAsync<T>(Func<CancellationToken, Task<T>> work, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(work);

        if (Volatile.Read(ref _disposed) != 0)
        {
            return Task.FromException<T>(new ObjectDisposedException(nameof(ReactiveRequestQueue)));
        }

        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var registration = cancellationToken.CanBeCanceled
            ? cancellationToken.Register(static state =>
                {
                    var source = (TaskCompletionSource<T>)state!;
                    source.TrySetCanceled();
                }, tcs)
            : default;

        var envelope = async () =>
        {
            try
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    tcs.TrySetCanceled(cancellationToken);
                    return;
                }

                var result = await work(cancellationToken).ConfigureAwait(false);
                tcs.TrySetResult(result);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                tcs.TrySetCanceled(cancellationToken);
            }
            catch (Exception ex)
            {
                tcs.TrySetException(ex);
            }
            finally
            {
                registration.Dispose();
            }
        };

        try
        {
            _requests.OnNext(envelope);
        }
        catch (ObjectDisposedException)
        {
            tcs.TrySetException(new ObjectDisposedException(nameof(ReactiveRequestQueue)));
        }

        return tcs.Task;
    }

    public Task EnqueueAsync(Func<CancellationToken, Task> work, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(work);

        return EnqueueAsync(async ct =>
        {
            await work(ct).ConfigureAwait(false);
            return true;
        }, cancellationToken);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _requests.OnCompleted();
        _subscriptions.Dispose();
    }
}
