using Grpc.Core;

using SubZeroFramework.GrpcContracts;
using SubZeroFramework.Models;
using SubZeroFramework.Services;

using System.Reactive.Linq;

namespace SubZeroFramework.Service.Services;

public sealed class FrameworkStatusGrpcService : FrameworkStatusService.FrameworkStatusServiceBase
{
    private readonly IFrameworkDataProvider _frameworkDataProvider;

    public FrameworkStatusGrpcService(IFrameworkDataProvider frameworkDataProvider)
    {
        _frameworkDataProvider = frameworkDataProvider;
    }

    public override async Task<FrameworkStatusReply> GetStatus(GetStatusRequest request, ServerCallContext context)
    {
        var status = await _frameworkDataProvider.RefreshAsync(context.CancellationToken).ConfigureAwait(false);
        return MapStatus(status);
    }

    public override async Task WatchStatus(WatchStatusRequest request, IServerStreamWriter<FrameworkStatusReply> responseStream, ServerCallContext context)
    {
        try
        {
            var initialStatus = await _frameworkDataProvider.RefreshAsync(context.CancellationToken).ConfigureAwait(false);
            await responseStream.WriteAsync(MapStatus(initialStatus), context.CancellationToken).ConfigureAwait(false);

            var statusStream = _frameworkDataProvider.SystemStatus
                .DistinctUntilChanged(status => new
                {
                    status.IsLibraryAvailable,
                    status.IsFrameworkDevice,
                    status.DeviceModel,
                    status.Platform,
                    status.PlatformFamily,
                    status.ActiveDriver,
                    status.EcBuildInfo,
                    status.IsEcPollingEnabled,
                    status.IsConnectionOpen,
                    status.RequiresElevation,
                    status.LastError,
                });

            var reader = ObservableChannelBridge.CreateBoundedReader(statusStream.Skip(1), context.CancellationToken);

            while (await reader.WaitToReadAsync(context.CancellationToken).ConfigureAwait(false))
            {
                while (reader.TryRead(out var status))
                {
                    await responseStream.WriteAsync(MapStatus(status), context.CancellationToken).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException) when (context.CancellationToken.IsCancellationRequested)
        {
        }
    }

    private static FrameworkStatusReply MapStatus(FrameworkSystemStatus status)
    {
        var reply = new FrameworkStatusReply
        {
            ObservedAtUnixTimeMilliseconds = status.ObservedAt.ToUnixTimeMilliseconds(),
            ConnectionLibraryVersion = status.ConnectionLibraryVersion,
            ConnectionLibraryInformationalVersion = status.ConnectionLibraryInformationalVersion ?? string.Empty,
            IsLibraryAvailable = status.IsLibraryAvailable,
            HasFrameworkDeviceValue = status.IsFrameworkDevice.HasValue,
            IsFrameworkDevice = status.IsFrameworkDevice ?? false,
            DeviceModel = status.DeviceModel ?? string.Empty,
            Platform = status.Platform?.ToString() ?? string.Empty,
            PlatformFamily = status.PlatformFamily?.ToString() ?? string.Empty,
            ActiveDriver = status.ActiveDriver?.ToString() ?? string.Empty,
            EcBuildInfo = status.EcBuildInfo ?? string.Empty,
            IsEcPollingEnabled = status.IsEcPollingEnabled,
            IsConnectionOpen = status.IsConnectionOpen,
            IsGrpcActive = status.IsGrpcActive,
            RequiresElevation = status.RequiresElevation,
            LastError = status.LastError ?? string.Empty,
        };

        reply.SupportedDrivers.AddRange(status.SupportedDrivers.Select(driver => driver.ToString()));
        return reply;
    }
}
