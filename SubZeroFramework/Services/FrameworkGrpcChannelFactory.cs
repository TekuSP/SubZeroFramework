using System.Net.Sockets;
using System.Reactive.Linq;

using Grpc.Net.Client;

using Microsoft.Extensions.Logging.Abstractions;

namespace SubZeroFramework.Services;

public sealed partial class FrameworkGrpcChannelFactory : IDisposable
{
    private readonly GrpcChannel _channel;
    private readonly FrameworkGrpcEndpointValidationResult _endpointValidation;
    private readonly ILogger<FrameworkGrpcChannelFactory> _logger;

    // The logger is optional so the existing parameterless construction in tests keeps working; the app
    // head resolves it through DI.
    public FrameworkGrpcChannelFactory(ILogger<FrameworkGrpcChannelFactory>? logger = null)
    {
        _logger = logger ?? NullLogger<FrameworkGrpcChannelFactory>.Instance;

        var socketPath = FrameworkGrpcSocketPath.GetPath();
        _endpointValidation = FrameworkGrpcSocketSecurity.ValidateEndpoint(socketPath);
        if (!_endpointValidation.IsValid)
        {
            // This is the failure that presents to the user as "the app shows nothing", so it must leave a
            // trail even though the caller also surfaces the message.
            LogEndpointRejected(socketPath, _endpointValidation.Message);
            throw new InvalidOperationException(_endpointValidation.Message);
        }

        LogEndpointAccepted(socketPath);

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

        LogChannelCreated(
            GrpcTransportDefaults.ChannelKeepAlivePingDelay,
            GrpcTransportDefaults.ChannelKeepAlivePingTimeout,
            GrpcTransportDefaults.UnaryRequestTimeout);
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
        LogChannelDisposed();
        _channel.Dispose();
    }

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "The gRPC endpoint {SocketPath} was rejected: {ValidationMessage}")]
    private partial void LogEndpointRejected(string socketPath, string validationMessage);

    [LoggerMessage(
        Level = LogLevel.Trace,
        Message = "The gRPC endpoint {SocketPath} passed validation.")]
    private partial void LogEndpointAccepted(string socketPath);

    [LoggerMessage(
        Level = LogLevel.Trace,
        Message = "Created the gRPC channel over the Unix domain socket. KeepAlivePingDelay={KeepAlivePingDelay}, KeepAlivePingTimeout={KeepAlivePingTimeout}, UnaryRequestTimeout={UnaryRequestTimeout}.")]
    private partial void LogChannelCreated(TimeSpan keepAlivePingDelay, TimeSpan keepAlivePingTimeout, TimeSpan unaryRequestTimeout);

    [LoggerMessage(
        Level = LogLevel.Trace,
        Message = "Disposing the gRPC channel.")]
    private partial void LogChannelDisposed();
}
