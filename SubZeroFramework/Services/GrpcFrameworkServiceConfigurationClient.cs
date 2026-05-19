using System.Reactive.Linq;

using Grpc.Core;

using SubZeroFramework.GrpcContracts;

namespace SubZeroFramework.Services;

public sealed class GrpcFrameworkServiceConfigurationClient : IFrameworkServiceConfigurationClient, IDisposable
{
    private readonly FrameworkGrpcChannelFactory _channelFactory;
    private readonly FrameworkServiceConfigurationService.FrameworkServiceConfigurationServiceClient _client;
    private readonly IObservable<FrameworkServiceConfigurationSnapshot> _sharedConfigurationStream;

    public GrpcFrameworkServiceConfigurationClient(FrameworkGrpcChannelFactory channelFactory)
    {
        ArgumentNullException.ThrowIfNull(channelFactory);

        _channelFactory = channelFactory;
        _client = new FrameworkServiceConfigurationService.FrameworkServiceConfigurationServiceClient(_channelFactory.Channel);
        _sharedConfigurationStream = _channelFactory.ShareLatest(CreateConfigurationStream());
    }

    public async Task<FrameworkServiceConfigurationSnapshot> GetConfigurationAsync(CancellationToken cancellationToken = default)
    {
        using var timeoutSource = _channelFactory.CreateTimeoutCancellationSource(cancellationToken);
        var reply = await _client.GetServiceConfigurationAsync(new GetServiceConfigurationRequest(), cancellationToken: timeoutSource.Token).ResponseAsync.ConfigureAwait(false);
        return MapConfiguration(reply);
    }

    public IObservable<FrameworkServiceConfigurationSnapshot> WatchConfiguration()
        => _sharedConfigurationStream;

    public async Task<FrameworkServiceConfigurationUpdateResult> UpdateConfigurationAsync(FrameworkServiceConfigurationUpdateRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        using var timeoutSource = _channelFactory.CreateTimeoutCancellationSource(cancellationToken);
        var reply = await _client.UpdateServiceConfigurationAsync(
            new UpdateServiceConfigurationRequest
            {
                PollingIntervalMilliseconds = checked((long)Math.Round(request.PollingInterval.TotalMilliseconds, MidpointRounding.AwayFromZero)),
                HardwareInfoPollingIntervalMilliseconds = checked((long)Math.Round(request.HardwareInfoPollingInterval.TotalMilliseconds, MidpointRounding.AwayFromZero)),
                AllowFanControlCommands = request.AllowFanControlCommands,
            },
            cancellationToken: timeoutSource.Token).ResponseAsync.ConfigureAwait(false);

        return new FrameworkServiceConfigurationUpdateResult
        {
            Succeeded = reply.Succeeded,
            Message = reply.Message,
            Configuration = MapConfiguration(reply.Configuration),
        };
    }

    public void Dispose()
    {
    }

    private IObservable<FrameworkServiceConfigurationSnapshot> CreateConfigurationStream()
    {
        return Observable.Create<FrameworkServiceConfigurationSnapshot>(observer =>
        {
            var cancellationSource = new CancellationTokenSource();

            _ = Task.Run(async () =>
            {
                while (!cancellationSource.IsCancellationRequested)
                {
                    AsyncServerStreamingCall<FrameworkServiceConfigurationReply>? call = null;

                    try
                    {
                        call = _client.WatchServiceConfiguration(new WatchServiceConfigurationRequest(), cancellationToken: cancellationSource.Token);

                        while (await call.ResponseStream.MoveNext(cancellationSource.Token).ConfigureAwait(false))
                        {
                            observer.OnNext(MapConfiguration(call.ResponseStream.Current));
                        }
                    }
                    catch (OperationCanceledException) when (cancellationSource.IsCancellationRequested)
                    {
                        break;
                    }
                    catch (RpcException) when (!cancellationSource.IsCancellationRequested)
                    {
                    }
                    catch (Exception) when (!cancellationSource.IsCancellationRequested)
                    {
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

    private static FrameworkServiceConfigurationSnapshot MapConfiguration(FrameworkServiceConfigurationReply reply)
    {
        return new FrameworkServiceConfigurationSnapshot
        {
            PollingInterval = TimeSpan.FromMilliseconds(reply.PollingIntervalMilliseconds),
            HardwareInfoPollingInterval = TimeSpan.FromMilliseconds(reply.HardwareInfoPollingIntervalMilliseconds),
            AllowFanControlCommands = reply.AllowFanControlCommands,
            PersistentConfigurationPath = reply.PersistentConfigurationPath,
        };
    }
}
