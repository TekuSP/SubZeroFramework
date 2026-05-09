using FrameworkDotnet.Enums;

namespace SubZeroFramework.Models;

public sealed record FanStateSnapshot
{
    public required int FanIndex { get; init; }

    public required string DisplayName { get; init; }

    public required FrameworkFanState FanState { get; init; }

    public required DateTimeOffset ObservedAt { get; init; }

    public required bool IsAvailable { get; init; }
}