using DynamicData;

using Grpc.Core;

namespace SubZeroFramework.Service.Services;

internal static class GrpcChangeSetWriter
{
    public static async Task WriteAsync<TObject, TKey, TReply>(
        IObservable<IChangeSet<TObject, TKey>> source,
        IServerStreamWriter<TReply> responseStream,
        Func<Change<TObject, TKey>, TReply?> mapChange,
        CancellationToken cancellationToken)
        where TObject : notnull
        where TKey : notnull
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(responseStream);
        ArgumentNullException.ThrowIfNull(mapChange);

        var reader = ObservableChannelBridge.CreateBoundedReader(source, cancellationToken);

        while (await reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
        {
            while (reader.TryRead(out var changeSet))
            {
                foreach (var change in changeSet)
                {
                    var reply = mapChange(change);
                    if (reply is null)
                    {
                        continue;
                    }

                    await responseStream.WriteAsync(reply, cancellationToken).ConfigureAwait(false);
                }
            }
        }
    }
}
