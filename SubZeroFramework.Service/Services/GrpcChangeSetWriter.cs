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
        CancellationToken cancellationToken,
        ILogger? logger = null,
        string? streamName = null)
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

                    logger?.LogDebug("Publishing {ChangeCount} change(s) to {StreamName}.", replies.Count, streamName ?? typeof(TBatchReply).Name);
                    await responseStream.WriteAsync(mapBatch(replies), cancellationToken).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            logger?.LogDebug("Stopping publish loop for {StreamName} because the request was cancelled.", streamName ?? typeof(TBatchReply).Name);
        }
        catch (ReactiveBackpressureExceededException exception)
        {
            logger?.LogWarning(exception, "Backpressure exceeded while publishing {StreamName}.", streamName ?? typeof(TBatchReply).Name);
            throw new RpcException(new Status(StatusCode.ResourceExhausted, exception.Message));
        }
    }
}
