using System.Net.Sockets;
using System.Reactive.Linq;

using Grpc.Net.Client;

namespace SubZeroFramework.Services;

public sealed class FrameworkGrpcChannelFactory : IDisposable
{
    private readonly GrpcChannel _channel;
    private readonly FrameworkGrpcEndpointValidationResult _endpointValidation;

    public FrameworkGrpcChannelFactory()
    {
        var socketPath = FrameworkGrpcSocketPath.GetPath();
        _endpointValidation = FrameworkGrpcSocketSecurity.ValidateEndpoint(socketPath);
        if (!_endpointValidation.IsValid)
        {
            throw new InvalidOperationException(_endpointValidation.Message);
        }

        var connectionFactory = new UnixDomainSocketsConnectionFactory(new UnixDomainSocketEndPoint(socketPath));
        var socketsHttpHandler = new SocketsHttpHandler
        {
            ConnectCallback = connectionFactory.ConnectAsync,
            PooledConnectionIdleTimeout = Timeout.InfiniteTimeSpan,
            KeepAlivePingDelay = GrpcTransportDefaults.ChannelKeepAlivePingDelay,
            KeepAlivePingTimeout = GrpcTransportDefaults.ChannelKeepAlivePingTimeout,
            EnableMultipleHttp2Connections = true,
        };

        _channel = GrpcChannel.ForAddress("http://localhost", new GrpcChannelOptions
        {
            HttpHandler = socketsHttpHandler,
        });
    }

    public GrpcChannel Channel => _channel;

    public FrameworkGrpcEndpointValidationResult EndpointValidation => _endpointValidation;

    public CancellationTokenSource CreateTimeoutCancellationSource(CancellationToken cancellationToken)
    {
        var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(GrpcTransportDefaults.UnaryRequestTimeout);
        return timeoutSource;
    }

    public IObservable<T> ShareLatest<T>(IObservable<T> source)
    {
        return source
            .Replay(1)
            .RefCount();
    }

    public void Dispose()
    {
        _channel.Dispose();
    }
}
