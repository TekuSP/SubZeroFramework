using System.Collections.Immutable;

namespace SubZeroFramework.Models;

/// <summary>
/// The service's retained log, as fetched by the app.
/// </summary>
public sealed record FrameworkServiceLogSnapshot
{
    public ImmutableArray<ServiceLogEntry> Entries { get; init; } = [];

    /// <summary>
    /// Entries the service dropped because its buffer is bounded. Non-zero means this is the most recent
    /// slice, NOT everything since start — the UI says so rather than implying a complete history.
    /// </summary>
    public long DroppedCount { get; init; }

    /// <summary>How many entries the service retains at most.</summary>
    public int BufferCapacity { get; init; }

    public bool IsTruncated => DroppedCount > 0;
}
