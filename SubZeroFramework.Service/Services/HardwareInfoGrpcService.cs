using Grpc.Core;

using SubZeroFramework.GrpcContracts;
using SubZeroFramework.Models;
using SubZeroFramework.Services;

namespace SubZeroFramework.Service.Services;

public sealed class HardwareInfoGrpcService : HardwareInfoService.HardwareInfoServiceBase
{
    private readonly IFrameworkDataProvider _frameworkDataProvider;

    public HardwareInfoGrpcService(IFrameworkDataProvider frameworkDataProvider)
    {
        _frameworkDataProvider = frameworkDataProvider;
    }

    public override Task<HardwareInfoReply> GetHardwareInfo(GetHardwareInfoRequest request, ServerCallContext context)
    {
        var snapshot = _frameworkDataProvider.GetLatestHardwareInfoSnapshot();
        return Task.FromResult(HardwareInfoGrpcMapper.MapHardwareInfoSnapshot(snapshot));
    }

    public override async Task WatchHardwareInfo(WatchHardwareInfoRequest request, IServerStreamWriter<HardwareInfoReply> responseStream, ServerCallContext context)
    {
        var reader = ObservableChannelBridge.CreateBoundedReader(_frameworkDataProvider.HardwareInfoSnapshots, context.CancellationToken);

        while (await reader.WaitToReadAsync(context.CancellationToken).ConfigureAwait(false))
        {
            while (reader.TryRead(out var snapshot))
            {
                await responseStream.WriteAsync(HardwareInfoGrpcMapper.MapHardwareInfoSnapshot(snapshot), context.CancellationToken).ConfigureAwait(false);
            }
        }
    }
}
