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

    public FrameworkTelemetryGrpcService(IFrameworkDataProvider frameworkDataProvider)
    {
        _frameworkDataProvider = frameworkDataProvider;
    }

    public override Task WatchTelemetryChannels(WatchTelemetryChannelsRequest request, IServerStreamWriter<TelemetryChannelChangeReply> responseStream, ServerCallContext context)
    {
        return GrpcChangeSetWriter.WriteAsync(
            _frameworkDataProvider.ConnectTelemetryChannels(),
            responseStream,
            change => change.Reason == ChangeReason.Remove
                ? null
                : TelemetryGrpcMapper.MapChannelChange(change),
            context.CancellationToken);
    }

    public override Task WatchCurrentTelemetryValues(WatchCurrentTelemetryValuesRequest request, IServerStreamWriter<CurrentTelemetryValueChangeReply> responseStream, ServerCallContext context)
    {
        return GrpcChangeSetWriter.WriteAsync(
            _frameworkDataProvider.ConnectCurrentTelemetryValues(),
            responseStream,
            change => change.Reason == ChangeReason.Remove
                ? null
                : TelemetryGrpcMapper.MapCurrentValueChange(change),
            context.CancellationToken);
    }

    public override Task WatchTelemetrySeries(WatchTelemetrySeriesRequest request, IServerStreamWriter<TelemetrySeriesPointChangeReply> responseStream, ServerCallContext context)
    {
        if (!Enum.TryParse<TelemetryArea>(request.Area, out var area)
            || !Enum.TryParse<TelemetryEntityKind>(request.EntityKind, out var entityKind)
            || !Enum.TryParse<TelemetryMetric>(request.Metric, out var metric))
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
            context.CancellationToken);
    }
}
