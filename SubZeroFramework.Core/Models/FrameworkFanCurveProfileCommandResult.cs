namespace SubZeroFramework.Models;

/// <summary>
/// Represents the result of a curve profile slot operation (save / set active / clear) through the service boundary.
/// </summary>
public sealed record FrameworkFanCurveProfileCommandResult
{
    public required int FanIndex { get; init; }

    public required int Slot { get; init; }

    public required bool Succeeded { get; init; }

    public required string Message { get; init; }
}
