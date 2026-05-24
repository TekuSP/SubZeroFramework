namespace SubZeroFramework.Models;

/// <summary>
/// Represents the result of applying a custom fan curve through the service boundary.
/// </summary>
public sealed record FrameworkFanCustomCurveCommandResult
{
    public required int FanIndex { get; init; }

    public required bool Succeeded { get; init; }

    public required string Message { get; init; }
}
