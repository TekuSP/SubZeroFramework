namespace SubZeroFramework.Services;

public static class GrpcTransportDefaults
{
    public static readonly TimeSpan StreamReconnectDelay = TimeSpan.FromSeconds(2);
    public static readonly TimeSpan ChannelKeepAlivePingDelay = TimeSpan.FromSeconds(60);
    public static readonly TimeSpan ChannelKeepAlivePingTimeout = TimeSpan.FromSeconds(30);
    public static readonly TimeSpan UnaryRequestTimeout = TimeSpan.FromSeconds(10);
}
