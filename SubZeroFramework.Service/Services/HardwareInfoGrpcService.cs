using Grpc.Core;

using SubZeroFramework.GrpcContracts;
using SubZeroFramework.Models;
using SubZeroFramework.Services;

namespace SubZeroFramework.Service.Services;

public sealed class HardwareInfoGrpcService : HardwareInfoService.HardwareInfoServiceBase
{
    private readonly IFrameworkDataProvider _frameworkDataProvider;
    private readonly ILogger<HardwareInfoGrpcService> _logger;

    private readonly IHostApplicationLifetime _applicationLifetime;

    public HardwareInfoGrpcService(
        IFrameworkDataProvider frameworkDataProvider,
        IHostApplicationLifetime applicationLifetime,
        ILogger<HardwareInfoGrpcService> logger)
    {
        _frameworkDataProvider = frameworkDataProvider;
        _applicationLifetime = applicationLifetime;
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
        using var streamCancellation = context.LinkToShutdown(_applicationLifetime);
        var streamToken = streamCancellation.Token;
        var reader = ObservableChannelBridge.CreateBoundedReader(_frameworkDataProvider.HardwareInfoSnapshots, streamToken, _logger, "hardware info stream");

        try
        {
            while (await reader.WaitToReadAsync(streamToken).ConfigureAwait(false))
            {
                while (reader.TryRead(out var snapshot))
                {
                    _logger.LogDebug("Publishing hardware info stream snapshot. IsAvailable={IsAvailable}, LastErrorPresent={HasLastError}.", snapshot.IsAvailable, !string.IsNullOrEmpty(snapshot.LastError));
                    await responseStream.WriteAsync(HardwareInfoGrpcMapper.MapHardwareInfoSnapshot(snapshot), streamToken).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException) when (streamToken.IsCancellationRequested)
        {
            _logger.LogDebug("Stopping hardware info stream because the request was cancelled or the service is stopping.");
        }
    }

    public override async Task WatchHardwareInfoHistory(WatchHardwareInfoHistoryRequest request, IServerStreamWriter<HardwareInfoHistoryChangeBatchReply> responseStream, ServerCallContext context)
    {
        var requestedHistoryWindow = TimeSpan.FromSeconds(request.HistoryWindowSeconds);
        if (requestedHistoryWindow <= TimeSpan.Zero || requestedHistoryWindow > TelemetryHistoryLimits.MaximumHistoryWindow)
        {
            _logger.LogWarning("Rejected hardware info history request because the requested history window {HistoryWindowSeconds}s is outside the supported range.", request.HistoryWindowSeconds);
            throw new RpcException(new Status(StatusCode.InvalidArgument, "The hardware info history window must be between 1 second and 1 hour."));
        }

        _logger.LogInformation("Opening hardware info history stream with history window {HistoryWindowSeconds}s.", request.HistoryWindowSeconds);

        using var streamCancellation = context.LinkToShutdown(_applicationLifetime);
        await GrpcChangeSetWriter.WriteAsync(
            _frameworkDataProvider.ConnectHardwareInfoHistory(requestedHistoryWindow),
            responseStream,
            HardwareInfoGrpcMapper.MapHardwareInfoHistoryChange,
            HardwareInfoGrpcMapper.MapHardwareInfoHistoryBatch,
            streamCancellation.Token,
            _logger,
            "hardware info history stream").ConfigureAwait(false);
    }
}
