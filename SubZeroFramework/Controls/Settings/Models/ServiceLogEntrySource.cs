namespace SubZeroFramework.Controls.Settings.Models;

/// <summary>
/// Which process produced a log entry shown on the logs page.
/// </summary>
/// <remarks>
/// The two halves fail independently and in different ways — the service can lose the EC while the app is
/// fine, and the app can lose the service connection while the service is healthy — so a log line is only
/// useful if you can tell which side it came from.
/// </remarks>
public enum ServiceLogEntrySource
{
    /// <summary>The background service, fetched over gRPC.</summary>
    Service,

    /// <summary>This desktop app, read from its own in-process buffer.</summary>
    App,
}
