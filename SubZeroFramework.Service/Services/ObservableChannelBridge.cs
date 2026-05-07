using System.Threading.Channels;

namespace SubZeroFramework.Service.Services;

internal static class ObservableChannelBridge
{
    public static ChannelReader<T> CreateBoundedReader<T>(IObservable<T> source, CancellationToken cancellationToken)
    {
        var channel = Channel.CreateBounded<T>(new BoundedChannelOptions(1)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false,
        });

        var subscription = source.Subscribe(
            value =>
            {
                channel.Writer.TryWrite(value);
            },
            exception =>
            {
                channel.Writer.TryComplete(exception);
            },
            () =>
            {
                channel.Writer.TryComplete();
            });

        cancellationToken.Register(() =>
        {
            subscription.Dispose();
            channel.Writer.TryComplete();
        });

        return channel.Reader;
    }
}
