using DynamicData;

using Grpc.Core;

using SubZeroFramework.GrpcContracts;
using SubZeroFramework.Models;
using SubZeroFramework.Services;

namespace SubZeroFramework.Service.Services;

public sealed class FrameworkTelemetryGrpcService : FrameworkTelemetryService.FrameworkTelemetryServiceBase
{
    private static readonly TimeSpan MaximumHistoryWindow = TimeSpan.FromHours(1);

    private readonly IFrameworkDataProvider _frameworkDataProvider;
    private readonly FrameworkFanControlStateStore _fanControlStateStore;

    public FrameworkTelemetryGrpcService(IFrameworkDataProvider frameworkDataProvider, FrameworkFanControlStateStore fanControlStateStore)
    {
        _frameworkDataProvider = frameworkDataProvider;
        _fanControlStateStore = fanControlStateStore;
    }

    public override Task WatchTelemetryChannels(WatchTelemetryChannelsRequest request, IServerStreamWriter<TelemetryChannelChangeBatchReply> responseStream, ServerCallContext context)
    {
        return GrpcChangeSetWriter.WriteAsync(
            _frameworkDataProvider.ConnectTelemetryChannels(),
            responseStream,
            TelemetryGrpcMapper.MapChannelChange,
            TelemetryGrpcMapper.MapChannelBatch,
            context.CancellationToken);
    }

    public override Task WatchFanCapabilities(WatchFanCapabilitiesRequest request, IServerStreamWriter<FanCapabilityChangeBatchReply> responseStream, ServerCallContext context)
    {
        return GrpcChangeSetWriter.WriteAsync(
            _frameworkDataProvider.ConnectFanCapabilities(),
            responseStream,
            TelemetryGrpcMapper.MapFanCapabilityChange,
            TelemetryGrpcMapper.MapFanCapabilityBatch,
            context.CancellationToken);
    }

    public override Task WatchFanControlStates(WatchFanControlStatesRequest request, IServerStreamWriter<FanControlStateChangeBatchReply> responseStream, ServerCallContext context)
    {
        return GrpcChangeSetWriter.WriteAsync(
            _fanControlStateStore.Connect(),
            responseStream,
            TelemetryGrpcMapper.MapFanControlStateChange,
            TelemetryGrpcMapper.MapFanControlStateBatch,
            context.CancellationToken);
    }

    public override Task WatchFanStates(WatchFanStatesRequest request, IServerStreamWriter<FanStateChangeBatchReply> responseStream, ServerCallContext context)
    {
        return GrpcChangeSetWriter.WriteAsync(
            _frameworkDataProvider.ConnectFanStates(),
            responseStream,
            TelemetryGrpcMapper.MapFanStateChange,
            TelemetryGrpcMapper.MapFanStateBatch,
            context.CancellationToken);
    }

    public override Task WatchCurrentTelemetryValues(WatchCurrentTelemetryValuesRequest request, IServerStreamWriter<CurrentTelemetryValueChangeBatchReply> responseStream, ServerCallContext context)
    {
        return GrpcChangeSetWriter.WriteAsync(
            _frameworkDataProvider.ConnectCurrentTelemetryValues(),
            responseStream,
            TelemetryGrpcMapper.MapCurrentValueChange,
            TelemetryGrpcMapper.MapCurrentValueBatch,
            context.CancellationToken);
    }

    public override Task WatchTelemetrySeries(WatchTelemetrySeriesRequest request, IServerStreamWriter<TelemetrySeriesPointChangeBatchReply> responseStream, ServerCallContext context)
    {
        if (!TelemetryGrpcMapper.TryParseTelemetryArea(request.Area, out var area)
            || !TelemetryGrpcMapper.TryParseTelemetryEntityKind(request.EntityKind, out var entityKind)
            || !TelemetryGrpcMapper.TryParseTelemetryMetric(request.Metric, out var metric))
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "The requested telemetry channel is invalid."));
        }

        var requestedHistoryWindow = TimeSpan.FromSeconds(request.HistoryWindowSeconds);
        if (requestedHistoryWindow <= TimeSpan.Zero || requestedHistoryWindow > MaximumHistoryWindow)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "The telemetry history window must be between 1 second and 1 hour."));
        }

        var channelId = new TelemetryChannelId(area, entityKind, request.Index, metric);
        return GrpcChangeSetWriter.WriteAsync(
            _frameworkDataProvider.ConnectTelemetrySeries(channelId, requestedHistoryWindow),
            responseStream,
            TelemetryGrpcMapper.MapTelemetryPointChange,
            TelemetryGrpcMapper.MapTelemetryPointBatch,
            context.CancellationToken);
    }
}
