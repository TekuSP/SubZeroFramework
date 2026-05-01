using FrameworkDotnet.Enums;

namespace SubZeroFramework.Models;

public sealed record FanCapabilityState
{
    public required int FanIndex { get; init; }

    public required string DisplayName { get; init; }

    public FrameworkFanFeaturesState Features { get; init; }

    public bool SupportsFanControl { get; init; }

    public bool SupportsThermalReporting { get; init; }

    public DateTimeOffset ObservedAt { get; init; }

    public bool IsAvailable { get; init; }

    public string FeaturesDisplay => Features.ToString();
}