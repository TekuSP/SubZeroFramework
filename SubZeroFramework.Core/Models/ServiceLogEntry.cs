using Microsoft.Extensions.Logging;

namespace SubZeroFramework.Models;

/// <summary>
/// One line of the background service's log, as surfaced to clients over the service boundary.
/// </summary>
/// <remarks>
/// The service keeps these in a bounded in-memory buffer rather than reading back the Windows Event Log or
/// journald: one implementation for both platforms, no extra permissions, and "since the service started" is
/// exactly what a buffer holds. The platform sinks still receive everything for post-mortem use.
/// </remarks>
public sealed record ServiceLogEntry
{
    public required DateTimeOffset ObservedAt { get; init; }

    public required LogLevel Level { get; init; }

    /// <summary>Logger category, e.g. the type that logged it.</summary>
    public required string Category { get; init; }

    public required string Message { get; init; }

    /// <summary>Exception detail when one was logged, otherwise empty.</summary>
    public string Exception { get; init; } = string.Empty;
}
