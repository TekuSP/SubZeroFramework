using Grpc.Core;

using SubZeroFramework.GrpcContracts;
using SubZeroFramework.Models;
using SubZeroFramework.Services;

namespace SubZeroFramework.Service.Services;

public sealed class FrameworkTelemetryGrpcService : FrameworkTelemetryService.FrameworkTelemetryServiceBase
{
    private readonly IFrameworkDataProvider _frameworkDataProvider;
    private readonly FrameworkFanControlStateStore _fanControlStateStore;
    private readonly ILogger<FrameworkTelemetryGrpcService> _logger;

    public FrameworkTelemetryGrpcService(IFrameworkDataProvider frameworkDataProvider, FrameworkFanControlStateStore fanControlStateStore, ILogger<FrameworkTelemetryGrpcService> logger)
    {
        _frameworkDataProvider = frameworkDataProvider;
        _fanControlStateStore = fanControlStateStore;
        _logger = logger;
    }

    public override Task WatchTelemetryChannels(WatchTelemetryChannelsRequest request, IServerStreamWriter<TelemetryChannelChangeBatchReply> responseStream, ServerCallContext context)
    {
        _logger.LogInformation("Opening telemetry channel stream.");
        return GrpcChangeSetWriter.WriteAsync(
            _frameworkDataProvider.ConnectTelemetryChannels(),
            responseStream,
            TelemetryGrpcMapper.MapChannelChange,
            TelemetryGrpcMapper.MapChannelBatch,
            context.CancellationToken,
            _logger,
            "telemetry channel stream");
    }

    public override Task WatchFanCapabilities(WatchFanCapabilitiesRequest request, IServerStreamWriter<FanCapabilityChangeBatchReply> responseStream, ServerCallContext context)
    {
        _logger.LogInformation("Opening fan capability stream.");
        return GrpcChangeSetWriter.WriteAsync(
            _frameworkDataProvider.ConnectFanCapabilities(),
            responseStream,
            TelemetryGrpcMapper.MapFanCapabilityChange,
            TelemetryGrpcMapper.MapFanCapabilityBatch,
            context.CancellationToken,
            _logger,
            "fan capability stream");
    }

    public override Task WatchFanControlStates(WatchFanControlStatesRequest request, IServerStreamWriter<FanControlStateChangeBatchReply> responseStream, ServerCallContext context)
    {
        _logger.LogInformation("Opening fan control state stream.");
        return GrpcChangeSetWriter.WriteAsync(
            _fanControlStateStore.Connect(),
            responseStream,
            TelemetryGrpcMapper.MapFanControlStateChange,
            TelemetryGrpcMapper.MapFanControlStateBatch,
            context.CancellationToken,
            _logger,
            "fan control state stream");
    }

    public override async Task WatchPowerDelivery(WatchPowerDeliveryRequest request, IServerStreamWriter<PowerDeliveryReply> responseStream, ServerCallContext context)
    {
        try
        {
            _logger.LogInformation("Opening power delivery stream.");

            // The provider's snapshot stream replays the latest value on subscribe, so no separate initial write.
            var reader = ObservableChannelBridge.CreateBoundedReader(
                _frameworkDataProvider.PowerDeliverySnapshots, context.CancellationToken, _logger, "power delivery stream");

            while (await reader.WaitToReadAsync(context.CancellationToken).ConfigureAwait(false))
            {
                while (reader.TryRead(out var snapshot))
                {
                    await responseStream.WriteAsync(MapPowerDelivery(snapshot), context.CancellationToken).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException) when (context.CancellationToken.IsCancellationRequested)
        {
            _logger.LogDebug("Stopping power delivery stream because the request was cancelled.");
        }
    }

    private static PowerDeliveryReply MapPowerDelivery(PowerDeliverySnapshot snapshot)
    {
        var reply = new PowerDeliveryReply();
        foreach (var port in snapshot.Ports)
        {
            reply.Ports.Add(new PowerDeliveryPortState
            {
                SlotIndex = port.SlotIndex,
                IsPresent = port.IsPresent,
                IsActivePort = port.IsActivePort,
                HasPowerDeliveryContract = port.HasPowerDeliveryContract,
                CState = port.CState.ToString(),
                PowerRole = port.PowerRole.ToString(),
                DataRole = port.DataRole.ToString(),
                CcPolarity = port.CcPolarity.ToString(),
                VoltageVolts = port.VoltageVolts,
                CurrentAmperes = port.CurrentAmperes,
                IsVconnActive = port.IsVconnActive,
                IsEprActive = port.IsEprActive,
                IsEprSupported = port.IsEprSupported,
                AltModeFlags = port.AltModeFlags,
                CardType = port.CardType,
                DataLane = port.DataLane.ToString(),
                DisplayPortCapability = port.DisplayPortCapability.ToString(),
                CapabilitySupportsCharging = port.SupportsCharging,
                MaxChargeWatts = port.MaxChargeWatts,
                UsbAHighPower = port.UsbAHighPower,
                CapabilityDocumented = port.CapabilityDocumented,
                PortSource = port.PortSource,
                PortPosition = port.PortPosition,
                PortIsLeft = port.PortIsLeft,
            });
        }

        return reply;
    }

    public override Task WatchFanStates(WatchFanStatesRequest request, IServerStreamWriter<FanStateChangeBatchReply> responseStream, ServerCallContext context)
    {
        _logger.LogInformation("Opening fan state stream.");
        return GrpcChangeSetWriter.WriteAsync(
            _frameworkDataProvider.ConnectFanStates(),
            responseStream,
            TelemetryGrpcMapper.MapFanStateChange,
            TelemetryGrpcMapper.MapFanStateBatch,
            context.CancellationToken,
            _logger,
            "fan state stream");
    }

    public override Task WatchCurrentTelemetryValues(WatchCurrentTelemetryValuesRequest request, IServerStreamWriter<CurrentTelemetryValueChangeBatchReply> responseStream, ServerCallContext context)
    {
        _logger.LogInformation("Opening current telemetry value stream.");
        return GrpcChangeSetWriter.WriteAsync(
            _frameworkDataProvider.ConnectCurrentTelemetryValues(),
            responseStream,
            TelemetryGrpcMapper.MapCurrentValueChange,
            TelemetryGrpcMapper.MapCurrentValueBatch,
            context.CancellationToken,
            _logger,
            "current telemetry value stream");
    }

    public override Task WatchTelemetrySeries(WatchTelemetrySeriesRequest request, IServerStreamWriter<TelemetrySeriesPointChangeBatchReply> responseStream, ServerCallContext context)
    {
        if (!TelemetryGrpcMapper.TryParseTelemetryArea(request.Area, out var area)
            || !TelemetryGrpcMapper.TryParseTelemetryEntityKind(request.EntityKind, out var entityKind)
            || !TelemetryGrpcMapper.TryParseTelemetryMetric(request.Metric, out var metric))
        {
            _logger.LogWarning("Rejected telemetry series request because the requested channel was invalid. Area={Area}, EntityKind={EntityKind}, Metric={Metric}, Index={Index}.", request.Area, request.EntityKind, request.Metric, request.Index);
            throw new RpcException(new Status(StatusCode.InvalidArgument, "The requested telemetry channel is invalid."));
        }

        var requestedHistoryWindow = TimeSpan.FromSeconds(request.HistoryWindowSeconds);
        if (requestedHistoryWindow <= TimeSpan.Zero || requestedHistoryWindow > TelemetryHistoryLimits.MaximumHistoryWindow)
        {
            _logger.LogWarning("Rejected telemetry series request because the requested history window {HistoryWindowSeconds}s is outside the supported range.", request.HistoryWindowSeconds);
            throw new RpcException(new Status(StatusCode.InvalidArgument, "The telemetry history window must be between 1 second and 1 hour."));
        }

        var channelId = new TelemetryChannelId(area, entityKind, request.Index, metric);
        _logger.LogInformation("Opening telemetry series stream for {ChannelId} with history window {HistoryWindowSeconds}s.", channelId, request.HistoryWindowSeconds);
        return GrpcChangeSetWriter.WriteAsync(
            _frameworkDataProvider.ConnectTelemetrySeries(channelId, requestedHistoryWindow),
            responseStream,
            TelemetryGrpcMapper.MapTelemetryPointChange,
            TelemetryGrpcMapper.MapTelemetryPointBatch,
            context.CancellationToken,
            _logger,
            $"telemetry series stream for {channelId}");
    }
}
