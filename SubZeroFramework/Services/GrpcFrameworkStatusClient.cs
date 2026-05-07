using System.Net.Sockets;
using System.Reactive.Linq;

using Grpc.Core;
using Grpc.Net.Client;

using SubZeroFramework.GrpcContracts;

namespace SubZeroFramework.Services;

public sealed class GrpcFrameworkStatusClient : IFrameworkStatusClient, IDisposable
{
    private static readonly TimeSpan ReconnectDelay = TimeSpan.FromSeconds(2);

    private readonly GrpcChannel _channel;
    private readonly FrameworkStatusService.FrameworkStatusServiceClient _client;

    public GrpcFrameworkStatusClient()
    {
        var socketPath = OperatingSystem.IsWindows()
            ? Path.Combine(Path.GetTempPath(), "SubZeroFramework", "subzeroframework.grpc.sock")
            : Path.Combine("/run", "subzeroframework.grpc.sock");
        var connectionFactory = new UnixDomainSocketsConnectionFactory(new UnixDomainSocketEndPoint(socketPath));
        var socketsHttpHandler = new SocketsHttpHandler
        {
            ConnectCallback = connectionFactory.ConnectAsync,
            PooledConnectionIdleTimeout = Timeout.InfiniteTimeSpan,
            KeepAlivePingDelay = TimeSpan.FromSeconds(60),
            KeepAlivePingTimeout = TimeSpan.FromSeconds(30),
            EnableMultipleHttp2Connections = true,
        };

        _channel = GrpcChannel.ForAddress("http://localhost", new GrpcChannelOptions
        {
            HttpHandler = socketsHttpHandler,
        });
        _client = new FrameworkStatusService.FrameworkStatusServiceClient(_channel);
    }

    public async Task<FrameworkSystemStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        var reply = await _client.GetStatusAsync(new GetStatusRequest(), cancellationToken: cancellationToken).ResponseAsync.ConfigureAwait(false);
        return MapStatus(reply);
    }

    public IObservable<FrameworkSystemStatus> WatchStatus()
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
                            observer.OnNext(MapStatus(call.ResponseStream.Current));
                        }

                        if (!cancellationSource.IsCancellationRequested)
                        {
                            observer.OnNext(CreateUnavailableStatus("The background service status stream ended unexpectedly."));
                        }
                    }
                    catch (OperationCanceledException) when (cancellationSource.IsCancellationRequested)
                    {
                        break;
                    }
                    catch (RpcException exception) when (!cancellationSource.IsCancellationRequested)
                    {
                        observer.OnNext(CreateUnavailableStatus($"Unable to connect to SubZeroFramework.Service. {exception.Status.Detail}"));
                    }
                    catch (Exception exception) when (!cancellationSource.IsCancellationRequested)
                    {
                        observer.OnNext(CreateUnavailableStatus($"Unable to connect to SubZeroFramework.Service. {exception.Message}"));
                    }
                    finally
                    {
                        call?.Dispose();
                    }

                    try
                    {
                        await Task.Delay(ReconnectDelay, cancellationSource.Token).ConfigureAwait(false);
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

    public void Dispose()
    {
        _channel.Dispose();
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
            RequiresElevation = reply.RequiresElevation,
            LastError = string.IsNullOrEmpty(reply.LastError) ? null : reply.LastError,
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
            RequiresElevation = false,
            LastError = message,
        };
    }
}
