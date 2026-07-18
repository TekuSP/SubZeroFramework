namespace SubZeroFramework.Models;

/// <summary>
/// Represents the result of setting (or clearing) a fan's CPU usage modifier through the service boundary.
/// </summary>
public sealed record FrameworkFanUsageModifierCommandResult
{
    public required int FanIndex { get; init; }

    public required bool Succeeded { get; init; }

    public required string Message { get; init; }
}
