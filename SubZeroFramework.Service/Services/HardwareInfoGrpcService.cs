using Grpc.Core;

using SubZeroFramework.GrpcContracts;
using SubZeroFramework.Models;
using SubZeroFramework.Services;

namespace SubZeroFramework.Service.Services;

public sealed class HardwareInfoGrpcService : HardwareInfoService.HardwareInfoServiceBase
{
    private static readonly TimeSpan MaximumHistoryWindow = TimeSpan.FromHours(1);

    private readonly IFrameworkDataProvider _frameworkDataProvider;
    private readonly ILogger<HardwareInfoGrpcService> _logger;

    public HardwareInfoGrpcService(IFrameworkDataProvider frameworkDataProvider, ILogger<HardwareInfoGrpcService> logger)
    {
        _frameworkDataProvider = frameworkDataProvider;
        _logger = logger;
    }

    public override Task<HardwareInfoReply> GetHardwareInfo(GetHardwareInfoRequest request, ServerCallContext context)
    {
        var snapshot = _frameworkDataProvider.GetLatestHardwareInfoSnapshot();
        _logger.LogDebug("Publishing GetHardwareInfo reply. IsAvailable={IsAvailable}, LastErrorPresent={HasLastError}.", snapshot.IsAvailable, !string.IsNullOrEmpty(snapshot.LastError));
        return Task.FromResult(HardwareInfoGrpcMapper.MapHardwareInfoSnapshot(snapshot));
    }

    public override async Task WatchHardwareInfo(WatchHardwareInfoRequest request, IServerStreamWriter<HardwareInfoReply> responseStream, ServerCallContext context)
    {
        _logger.LogInformation("Opening hardware info stream.");
        var reader = ObservableChannelBridge.CreateBoundedReader(_frameworkDataProvider.HardwareInfoSnapshots, context.CancellationToken, _logger, "hardware info stream");

        try
        {
            while (await reader.WaitToReadAsync(context.CancellationToken).ConfigureAwait(false))
            {
                while (reader.TryRead(out var snapshot))
                {
                    _logger.LogDebug("Publishing hardware info stream snapshot. IsAvailable={IsAvailable}, LastErrorPresent={HasLastError}.", snapshot.IsAvailable, !string.IsNullOrEmpty(snapshot.LastError));
                    await responseStream.WriteAsync(HardwareInfoGrpcMapper.MapHardwareInfoSnapshot(snapshot), context.CancellationToken).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException) when (context.CancellationToken.IsCancellationRequested)
        {
            _logger.LogDebug("Stopping hardware info stream because the request was cancelled.");
        }
    }

    public override Task WatchHardwareInfoHistory(WatchHardwareInfoHistoryRequest request, IServerStreamWriter<HardwareInfoHistoryChangeBatchReply> responseStream, ServerCallContext context)
    {
        var requestedHistoryWindow = TimeSpan.FromSeconds(request.HistoryWindowSeconds);
        if (requestedHistoryWindow <= TimeSpan.Zero || requestedHistoryWindow > MaximumHistoryWindow)
        {
            _logger.LogWarning("Rejected hardware info history request because the requested history window {HistoryWindowSeconds}s is outside the supported range.", request.HistoryWindowSeconds);
            throw new RpcException(new Status(StatusCode.InvalidArgument, "The hardware info history window must be between 1 second and 1 hour."));
        }

        _logger.LogInformation("Opening hardware info history stream with history window {HistoryWindowSeconds}s.", request.HistoryWindowSeconds);

        return GrpcChangeSetWriter.WriteAsync(
            _frameworkDataProvider.ConnectHardwareInfoHistory(requestedHistoryWindow),
            responseStream,
            HardwareInfoGrpcMapper.MapHardwareInfoHistoryChange,
            HardwareInfoGrpcMapper.MapHardwareInfoHistoryBatch,
            context.CancellationToken,
            _logger,
            "hardware info history stream");
    }
}
