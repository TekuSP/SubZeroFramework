using System.Reactive.Linq;

using Grpc.Core;

using SubZeroFramework.GrpcContracts;

namespace SubZeroFramework.Services;

public sealed class GrpcFrameworkStatusClient : IFrameworkStatusClient, IDisposable
{
    private readonly FrameworkGrpcChannelFactory _channelFactory;
    private readonly FrameworkStatusService.FrameworkStatusServiceClient _client;
    private readonly IObservable<FrameworkSystemStatus> _sharedStatusStream;
    private DateTimeOffset? _lastObservedAt;

    public GrpcFrameworkStatusClient(FrameworkGrpcChannelFactory channelFactory)
    {
        ArgumentNullException.ThrowIfNull(channelFactory);

        _channelFactory = channelFactory;
        _client = new FrameworkStatusService.FrameworkStatusServiceClient(_channelFactory.Channel);
        _sharedStatusStream = _channelFactory.ShareLatest(CreateStatusStream());
    }

    public async Task<FrameworkSystemStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var timeoutSource = _channelFactory.CreateTimeoutCancellationSource(cancellationToken);
            var reply = await _client.GetStatusAsync(new GetStatusRequest(), cancellationToken: timeoutSource.Token).ResponseAsync.ConfigureAwait(false);
            var status = MapStatus(reply);
            _lastObservedAt = status.ObservedAt;
            return status;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (RpcException exception)
        {
            var unavailableStatus = CreateUnavailableStatus($"Unable to connect to SubZeroFramework.Service. {exception.Status.Detail}");
            _lastObservedAt = unavailableStatus.ObservedAt;
            return unavailableStatus;
        }
        catch (Exception exception)
        {
            var unavailableStatus = CreateUnavailableStatus($"Unable to connect to SubZeroFramework.Service. {exception.Message}");
            _lastObservedAt = unavailableStatus.ObservedAt;
            return unavailableStatus;
        }
    }

    public IObservable<FrameworkSystemStatus> WatchStatus()
        => _sharedStatusStream;

    public DateTimeOffset? LastObservedAt => _lastObservedAt;

    public FrameworkGrpcEndpointValidationResult EndpointValidation => _channelFactory.EndpointValidation;

    public void Dispose()
    {
    }

    private IObservable<FrameworkSystemStatus> CreateStatusStream()
    {
        return Observable.Create<FrameworkSystemStatus>(observer =>
        {
            var cancellationSource = new CancellationTokenSource();

            _ = Task.Run(async () =>
            {
                while (!cancellationSource.IsCancellationRequested)
                {
                    AsyncServerStreamingCall<FrameworkStatusReply>? call = null;

                    try
                    {
                        call = _client.WatchStatus(new WatchStatusRequest(), cancellationToken: cancellationSource.Token);

                        while (await call.ResponseStream.MoveNext(cancellationSource.Token).ConfigureAwait(false))
                        {
                            var status = MapStatus(call.ResponseStream.Current);
                            _lastObservedAt = status.ObservedAt;
                            observer.OnNext(status);
                        }

                        if (!cancellationSource.IsCancellationRequested)
                        {
                            var unavailableStatus = CreateUnavailableStatus("The background service status stream ended unexpectedly.");
                            _lastObservedAt = unavailableStatus.ObservedAt;
                            observer.OnNext(unavailableStatus);
                        }
                    }
                    catch (OperationCanceledException) when (cancellationSource.IsCancellationRequested)
                    {
                        break;
                    }
                    catch (RpcException exception) when (!cancellationSource.IsCancellationRequested)
                    {
                        var unavailableStatus = CreateUnavailableStatus($"Unable to connect to SubZeroFramework.Service. {exception.Status.Detail}");
                        _lastObservedAt = unavailableStatus.ObservedAt;
                        observer.OnNext(unavailableStatus);
                    }
                    catch (Exception exception) when (!cancellationSource.IsCancellationRequested)
                    {
                        var unavailableStatus = CreateUnavailableStatus($"Unable to connect to SubZeroFramework.Service. {exception.Message}");
                        _lastObservedAt = unavailableStatus.ObservedAt;
                        observer.OnNext(unavailableStatus);
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

    private static FrameworkSystemStatus MapStatus(FrameworkStatusReply reply)
    {
        return new FrameworkSystemStatus
        {
            ObservedAt = DateTimeOffset.FromUnixTimeMilliseconds(reply.ObservedAtUnixTimeMilliseconds),
            ConnectionLibraryVersion = reply.ConnectionLibraryVersion,
            ConnectionLibraryInformationalVersion = string.IsNullOrEmpty(reply.ConnectionLibraryInformationalVersion) ? null : reply.ConnectionLibraryInformationalVersion,
            IsLibraryAvailable = reply.IsLibraryAvailable,
            IsFrameworkDevice = reply.HasFrameworkDeviceValue ? reply.IsFrameworkDevice : null,
            DeviceModel = string.IsNullOrEmpty(reply.DeviceModel) ? null : reply.DeviceModel,
            Platform = Enum.TryParse<FrameworkDotnet.Enums.FrameworkPlatform>(reply.Platform, out var parsedPlatform) ? parsedPlatform : null,
            PlatformFamily = Enum.TryParse<FrameworkDotnet.Enums.FrameworkPlatformFamily>(reply.PlatformFamily, out var parsedPlatformFamily) ? parsedPlatformFamily : null,
            SupportedDrivers = reply.SupportedDrivers
                .Select(driver => Enum.TryParse<FrameworkDotnet.Enums.FrameworkEcDriver>(driver, out var parsedDriver)
                    ? parsedDriver
                    : FrameworkDotnet.Enums.FrameworkEcDriver.Unknown)
                .Where(driver => driver != FrameworkDotnet.Enums.FrameworkEcDriver.Unknown)
                .ToImmutableArray(),
            ActiveDriver = Enum.TryParse<FrameworkDotnet.Enums.FrameworkEcDriver>(reply.ActiveDriver, out var parsedActiveDriver) ? parsedActiveDriver : null,
            EcBuildInfo = string.IsNullOrEmpty(reply.EcBuildInfo) ? null : reply.EcBuildInfo,
            IsEcPollingEnabled = reply.IsEcPollingEnabled,
            IsConnectionOpen = reply.IsConnectionOpen,
            IsGrpcActive = true,
            LastTelemetryObservedAt = reply.LastTelemetryObservedAtUnixTimeMilliseconds <= 0
                ? DateTimeOffset.MinValue
                : DateTimeOffset.FromUnixTimeMilliseconds(reply.LastTelemetryObservedAtUnixTimeMilliseconds),
            RequiresElevation = reply.RequiresElevation,
            LastError = string.IsNullOrEmpty(reply.LastError) ? null : reply.LastError,
            IsFanControlEnabled = reply.IsFanControlEnabled,
            HasCallerIdentityValidation = reply.HasCallerIdentityValidation,
            FanControlAuthorizationMessage = string.IsNullOrEmpty(reply.FanControlAuthorizationMessage) ? null : reply.FanControlAuthorizationMessage,
        };
    }

    private static FrameworkSystemStatus CreateUnavailableStatus(string message)
    {
        return new FrameworkSystemStatus
        {
            ObservedAt = DateTimeOffset.UtcNow,
            ConnectionLibraryVersion = string.Empty,
            IsLibraryAvailable = false,
            IsFrameworkDevice = null,
            DeviceModel = null,
            Platform = null,
            PlatformFamily = null,
            SupportedDrivers = [],
            ActiveDriver = null,
            EcBuildInfo = null,
            IsEcPollingEnabled = false,
            IsConnectionOpen = false,
            IsGrpcActive = false,
            LastTelemetryObservedAt = DateTimeOffset.MinValue,
            RequiresElevation = false,
            LastError = message,
            IsFanControlEnabled = false,
            HasCallerIdentityValidation = false,
            FanControlAuthorizationMessage = null,
        };
    }
}
