using Grpc.Core;

using SubZeroFramework.GrpcContracts;
using SubZeroFramework.Models;
using SubZeroFramework.Services;

using System.Reactive.Linq;

namespace SubZeroFramework.Service.Services;

public sealed class FrameworkStatusGrpcService : FrameworkStatusService.FrameworkStatusServiceBase
{
    private readonly FrameworkFanControlAuthorizationService _authorizationService;
    private readonly IFrameworkDataProvider _frameworkDataProvider;
    private readonly ILogger<FrameworkStatusGrpcService> _logger;

    public FrameworkStatusGrpcService(IFrameworkDataProvider frameworkDataProvider, FrameworkFanControlAuthorizationService authorizationService, ILogger<FrameworkStatusGrpcService> logger)
    {
        _frameworkDataProvider = frameworkDataProvider;
        _authorizationService = authorizationService;
        _logger = logger;
    }

    public override async Task<FrameworkStatusReply> GetStatus(GetStatusRequest request, ServerCallContext context)
    {
        _logger.LogDebug("Received GetStatus request.");
        ApplyFanControlAuthorization();
        var status = await _frameworkDataProvider.RefreshAsync(context.CancellationToken).ConfigureAwait(false);
        _logger.LogDebug("Publishing GetStatus reply. IsConnectionOpen={IsConnectionOpen}, RequiresElevation={RequiresElevation}, LastErrorPresent={HasLastError}.", status.IsConnectionOpen, status.RequiresElevation, !string.IsNullOrEmpty(status.LastError));
        return MapStatus(status);
    }

    public override async Task WatchStatus(WatchStatusRequest request, IServerStreamWriter<FrameworkStatusReply> responseStream, ServerCallContext context)
    {
        try
        {
            _logger.LogInformation("Opening status stream.");
            ApplyFanControlAuthorization();
            var initialStatus = await _frameworkDataProvider.RefreshAsync(context.CancellationToken).ConfigureAwait(false);
            _logger.LogDebug("Publishing initial status stream reply. IsConnectionOpen={IsConnectionOpen}, RequiresElevation={RequiresElevation}, LastErrorPresent={HasLastError}.", initialStatus.IsConnectionOpen, initialStatus.RequiresElevation, !string.IsNullOrEmpty(initialStatus.LastError));
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
                    status.LastTelemetryObservedAt,
                    status.RequiresElevation,
                    status.LastError,
                });

            var reader = ObservableChannelBridge.CreateBoundedReader(statusStream.Skip(1), context.CancellationToken, _logger, "status stream");

            while (await reader.WaitToReadAsync(context.CancellationToken).ConfigureAwait(false))
            {
                while (reader.TryRead(out var status))
                {
                    _logger.LogDebug("Publishing status stream update. IsConnectionOpen={IsConnectionOpen}, RequiresElevation={RequiresElevation}, LastErrorPresent={HasLastError}.", status.IsConnectionOpen, status.RequiresElevation, !string.IsNullOrEmpty(status.LastError));
                    await responseStream.WriteAsync(MapStatus(status), context.CancellationToken).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException) when (context.CancellationToken.IsCancellationRequested)
        {
            _logger.LogDebug("Stopping status stream because the request was cancelled.");
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
            LastTelemetryObservedAtUnixTimeMilliseconds = status.LastTelemetryObservedAt.ToUnixTimeMilliseconds(),
            RequiresElevation = status.RequiresElevation,
            LastError = status.LastError ?? string.Empty,
            IsFanControlEnabled = status.IsFanControlEnabled,
            HasCallerIdentityValidation = status.HasCallerIdentityValidation,
            FanControlAuthorizationMessage = status.FanControlAuthorizationMessage ?? string.Empty,
        };

        reply.SupportedDrivers.AddRange(status.SupportedDrivers.Select(driver => driver.ToString()));
        return reply;
    }

    private void ApplyFanControlAuthorization()
    {
        _frameworkDataProvider.SetFanControlAuthorization(
            _authorizationService.IsFanControlEnabled,
            _authorizationService.HasCallerIdentityValidation,
            _authorizationService.GetAuthorizationMessage());
    }
}
