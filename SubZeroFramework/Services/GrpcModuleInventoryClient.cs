using FrameworkDotnet.Enums;

using Grpc.Core;

using SubZeroFramework.GrpcContracts;

namespace SubZeroFramework.Services;

/// <summary>
/// gRPC consumer of <c>WatchModuleInventory</c>. Mirrors <see cref="GrpcPowerDeliveryClient"/>: a single
/// reconnecting server-streaming call projected into a shared, replay-latest observable of the module inventory.
/// </summary>
public sealed class GrpcModuleInventoryClient : IModuleInventoryClient
{
    private readonly FrameworkGrpcChannelFactory _channelFactory;
    private readonly FrameworkTelemetryService.FrameworkTelemetryServiceClient _client;
    private readonly IObservable<ModuleInventoryStatus?> _sharedStream;

    public GrpcModuleInventoryClient(FrameworkGrpcChannelFactory channelFactory)
    {
        ArgumentNullException.ThrowIfNull(channelFactory);

        _channelFactory = channelFactory;
        _client = new FrameworkTelemetryService.FrameworkTelemetryServiceClient(_channelFactory.Channel);
        _sharedStream = _channelFactory.ShareLatest(CreateInventoryStream());
    }

    public IObservable<ModuleInventoryStatus?> WatchInventory() => _sharedStream;

    private IObservable<ModuleInventoryStatus?> CreateInventoryStream()
    {
        return System.Reactive.Linq.Observable.Create<ModuleInventoryStatus?>(observer =>
        {
            var cancellationSource = new CancellationTokenSource();

            _ = Task.Run(async () =>
            {
                while (!cancellationSource.IsCancellationRequested)
                {
                    AsyncServerStreamingCall<ModuleInventoryReply>? call = null;

                    try
                    {
                        call = _client.WatchModuleInventory(new WatchModuleInventoryRequest(), cancellationToken: cancellationSource.Token);

                        while (await call.ResponseStream.MoveNext(cancellationSource.Token).ConfigureAwait(false))
                        {
                            observer.OnNext(MapInventory(call.ResponseStream.Current));
                        }
                    }
                    catch (OperationCanceledException) when (cancellationSource.IsCancellationRequested)
                    {
                        break;
                    }
                    catch (RpcException) when (!cancellationSource.IsCancellationRequested)
                    {
                        observer.OnNext(null);
                    }
                    catch (Exception) when (!cancellationSource.IsCancellationRequested)
                    {
                        observer.OnNext(null);
                    }
                    finally
                    {
                        call?.Dispose();
                    }

                    try
                    {
                        await Task.Delay(GrpcTransportDefaults.StreamReconnectDelay, cancellationSource.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (cancellationSource.IsCancellationRequested)
                    {
                        break;
                    }
                }

                observer.OnCompleted();
            }, cancellationSource.Token);

            return () =>
            {
                cancellationSource.Cancel();
                cancellationSource.Dispose();
            };
        });
    }

    private static ModuleInventoryStatus MapInventory(ModuleInventoryReply reply) => new()
    {
        UsbCSlots = MapDescriptors(reply.UsbCSlots),
        InputDeckModules = MapDescriptors(reply.InputDeck),
        InternalModules = MapDescriptors(reply.InternalFixed),
        DetachedModules = MapDescriptors(reply.Detached),
        ExpansionBayModule = reply.HasExpansionBay && reply.ExpansionBay is not null
            ? MapDescriptor(reply.ExpansionBay)
            : null,
        ExpansionBayBoard = ParseEnum<FrameworkExpansionBayBoard>(reply.ExpansionBayBoard),
        ExpansionBayVendor = ParseEnum<FrameworkExpansionBayVendor>(reply.ExpansionBayVendor),
        ExpansionBaySerialNumber = reply.ExpansionBaySerial,
    };

    private static IReadOnlyList<ModuleDescriptorStatus> MapDescriptors(IReadOnlyList<ModuleDescriptor> descriptors)
    {
        var mapped = new List<ModuleDescriptorStatus>(descriptors.Count);
        foreach (var descriptor in descriptors)
        {
            mapped.Add(MapDescriptor(descriptor));
        }

        return mapped;
    }

    private static ModuleDescriptorStatus MapDescriptor(ModuleDescriptor descriptor) => new()
    {
        Identity = ParseEnum<FrameworkModuleIdentity>(descriptor.Identity),
        Bus = ParseEnum<FrameworkModuleBus>(descriptor.Bus),
        SlotKind = ParseEnum<FrameworkModuleSlotKind>(descriptor.SlotKind),
        Confidence = ParseEnum<FrameworkModuleConfidence>(descriptor.Confidence),
        IsPresent = descriptor.IsPresent,
        SlotIndex = descriptor.SlotIndex,
        Flags = (FrameworkModuleFlags)descriptor.Flags,
        VendorId = descriptor.VendorId,
        ProductId = descriptor.ProductId,
        BoardId = descriptor.BoardId,
        Position = ParseEnum<FrameworkInputModulePosition>(descriptor.Position),
        CardType = ParseEnum<FrameworkExpansionCardType>(descriptor.CardType),
        CardConfidence = ParseEnum<FrameworkModuleConfidence>(descriptor.CardConfidence),
    };

    // Unrecognised names (e.g. a newer service) fall back to the enum default (None/Unknown) instead of throwing.
    private static TEnum ParseEnum<TEnum>(string value) where TEnum : struct, Enum
        => Enum.TryParse<TEnum>(value, out var parsed) ? parsed : default;
}
