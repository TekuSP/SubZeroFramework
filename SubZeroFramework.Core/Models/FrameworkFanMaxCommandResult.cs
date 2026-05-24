namespace SubZeroFramework.Models;

/// <summary>
/// Represents the result of forcing a fan to maximum (100%) duty through the service boundary.
/// </summary>
public sealed record FrameworkFanMaxCommandResult
{
    /// <summary>
    /// Gets the zero-based fan index.
    /// </summary>
    public required int FanIndex { get; init; }

    /// <summary>
    /// Gets the applied duty cycle percent (always 100 on success).
    /// </summary>
    public required double AppliedDutyPercent { get; init; }
}
