using System.Threading.Channels;

using System.Reactive.Disposables;

namespace SubZeroFramework.Service.Services;

internal static class ObservableChannelBridge
{
    public static ChannelReader<T> CreateBoundedReader<T>(IObservable<T> source, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);

        var channel = Channel.CreateBounded<T>(new BoundedChannelOptions(32)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = true,
            AllowSynchronousContinuations = false,
        });

        var subscription = new SingleAssignmentDisposable();
        var completionState = 0;

        void Complete(Exception? exception = null)
        {
            if (Interlocked.Exchange(ref completionState, 1) != 0)
            {
                return;
            }

            subscription.Dispose();
            channel.Writer.TryComplete(exception);
        }

        subscription.Disposable = source.Subscribe(
            value =>
            {
                if (!channel.Writer.TryWrite(value))
                {
                    Complete(new ReactiveBackpressureExceededException("The telemetry stream consumer fell behind the producer buffer."));
                }
            },
            exception =>
            {
                Complete(exception);
            },
            () =>
            {
                Complete();
            });

        cancellationToken.Register(() =>
        {
            Complete();
        });

        return channel.Reader;
    }
}
