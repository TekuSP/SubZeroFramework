namespace SubZeroFramework.Models;

/// <summary>
/// Represents the result of setting a fan RPM target through the service boundary.
/// </summary>
public sealed record FrameworkFanRpmCommandResult
{
    /// <summary>
    /// Gets the zero-based fan index.
    /// </summary>
    public required int FanIndex { get; init; }

    /// <summary>
    /// Gets the applied fan speed in RPM.
    /// </summary>
    public required int AppliedSpeedRpm { get; init; }
}
