namespace SubZeroFramework.Models;

/// <summary>
/// Represents the result of setting a fan duty cycle through the service boundary.
/// </summary>
public sealed record FrameworkFanDutyCommandResult
{
    /// <summary>
    /// Gets the zero-based fan index.
    /// </summary>
    public required int FanIndex { get; init; }

    /// <summary>
    /// Gets the applied duty cycle percent.
    /// </summary>
    public required double AppliedDutyPercent { get; init; }
}
