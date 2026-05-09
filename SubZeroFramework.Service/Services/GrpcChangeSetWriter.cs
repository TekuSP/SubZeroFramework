using DynamicData;

using Grpc.Core;

namespace SubZeroFramework.Service.Services;

internal static class GrpcChangeSetWriter
{
    public static async Task WriteAsync<TObject, TKey, TReply, TBatchReply>(
        IObservable<IChangeSet<TObject, TKey>> source,
        IServerStreamWriter<TBatchReply> responseStream,
        Func<Change<TObject, TKey>, TReply> mapChange,
        Func<IReadOnlyList<TReply>, TBatchReply> mapBatch,
        CancellationToken cancellationToken)
        where TObject : notnull
        where TKey : notnull
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(responseStream);
        ArgumentNullException.ThrowIfNull(mapChange);
        ArgumentNullException.ThrowIfNull(mapBatch);

        try
        {
            var reader = ObservableChannelBridge.CreateBoundedReader(source, cancellationToken);

                while (await reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
            {
                while (reader.TryRead(out var changeSet))
                {
                    if (changeSet.Count == 0)
                    {
                        continue;
                    }

                    var replies = new List<TReply>(changeSet.Count);
                    foreach (var change in changeSet)
                    {
                        replies.Add(mapChange(change));
                    }

                    await responseStream.WriteAsync(mapBatch(replies), cancellationToken).ConfigureAwait(false);
                }
            }
        }
        catch (ReactiveBackpressureExceededException exception)
        {
            throw new RpcException(new Status(StatusCode.ResourceExhausted, exception.Message));
        }
    }
}
