namespace SubZeroFramework.Models;

/// <summary>
/// Represents the result of restoring automatic fan control through the service boundary.
/// </summary>
public sealed record FrameworkRestoreAutoFanControlCommandResult
{
    /// <summary>
    /// Gets the zero-based fan index.
    /// </summary>
    public required int FanIndex { get; init; }
}
