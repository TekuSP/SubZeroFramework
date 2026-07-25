namespace SubZeroFramework.Models;

/// <summary>
/// Represents the result of resetting all fan control state to factory defaults through the service boundary.
/// A partial reset is reported rather than thrown: the fans that were reset stay reset either way, so the
/// counters describe exactly how far it got.
/// </summary>
public sealed record FrameworkFanControlResetCommandResult
{
    /// <summary>True only when every fan was restored AND the persisted settings were deleted.</summary>
    public required bool Succeeded { get; init; }

    public required string Message { get; init; }

    /// <summary>Fans returned to the controller's automatic mode.</summary>
    public required int FansRestored { get; init; }

    /// <summary>Fans whose controller restore failed; their stored settings were cleared regardless.</summary>
    public required int FansFailed { get; init; }

    /// <summary>Saved per-fan entries deleted, including any for fans the hardware no longer reports.</summary>
    public required int PersistedEntriesCleared { get; init; }
}
