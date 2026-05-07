using System.Net.Sockets;
using System.Reactive.Linq;

using Grpc.Net.Client;

namespace SubZeroFramework.Services;

public sealed class FrameworkGrpcChannelFactory : IDisposable
{
    private readonly GrpcChannel _channel;

    public FrameworkGrpcChannelFactory()
    {
        var socketPath = FrameworkGrpcSocketPath.GetPath();
        FrameworkGrpcSocketSecurity.ValidateExpectedClientSocketPath(socketPath);
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
    }

    public GrpcChannel Channel => _channel;

    public CancellationTokenSource CreateTimeoutCancellationSource(CancellationToken cancellationToken)
    {
        var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(TimeSpan.FromSeconds(10));
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
