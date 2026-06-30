namespace SubZeroFramework.Models;

/// <summary>Battery charge floor/ceiling thresholds (percent) reported by — or written to — the EC.</summary>
public sealed record ChargeLimitsState
{
    public required int MinimumPercent { get; init; }

    public required int MaximumPercent { get; init; }
}
