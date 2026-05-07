using Grpc.Core;

using SubZeroFramework.GrpcContracts;
using SubZeroFramework.Models;
using SubZeroFramework.Services;

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
            RequiresElevation = status.RequiresElevation,
            LastError = status.LastError ?? string.Empty,
        };

        reply.SupportedDrivers.AddRange(status.SupportedDrivers.Select(driver => driver.ToString()));
        return reply;
    }
}
