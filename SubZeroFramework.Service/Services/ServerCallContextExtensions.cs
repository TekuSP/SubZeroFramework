using Grpc.Core;

using Microsoft.Extensions.Hosting;

namespace SubZeroFramework.Service.Services;

/// <summary>
/// Helpers for the long-lived server-streaming calls this service is built from.
/// </summary>
internal static class ServerCallContextExtensions
{
    /// <summary>
    /// A token that fires when the client goes away OR when the service starts shutting down.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every <c>Watch…</c> call here streams forever by design, and ASP.NET Core's graceful shutdown waits
    /// for in-flight requests to finish before it stops. A stream watching only
    /// <see cref="ServerCallContext.CancellationToken"/> never finishes on its own: that token fires when the
    /// client disconnects, or when Kestrel ABORTS the request — which it only does once the shutdown timeout
    /// has fully elapsed.
    /// </para>
    /// <para>
    /// The effect was a service that took its entire 90-second shutdown budget to stop whenever a client was
    /// connected. Windows Installer waits far less than that for a service to stop, so uninstalling put up
    /// "Installer is no longer responding" and needed retrying until the budget ran out. Linking the two
    /// tokens lets the streams unwind the moment shutdown is signalled, and the service stops in well under a
    /// second.
    /// </para>
    /// <para>
    /// The returned source must be disposed — hence <c>using</c> at every call site — or each completed call
    /// leaves a registration on the lifetime token that lives as long as the process.
    /// </para>
    /// </remarks>
    public static CancellationTokenSource LinkToShutdown(
        this ServerCallContext context,
        IHostApplicationLifetime applicationLifetime)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(applicationLifetime);

        return CancellationTokenSource.CreateLinkedTokenSource(
            context.CancellationToken,
            applicationLifetime.ApplicationStopping);
    }
}
