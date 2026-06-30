namespace SubZeroFramework.Services;

/// <summary>Result of a battery charge-limit get/set command through the service boundary.</summary>
public sealed record FrameworkChargeLimitsResult
{
    public required bool IsAvailable { get; init; }

    public required bool Succeeded { get; init; }

    public string Message { get; init; } = string.Empty;

    public int MinimumPercent { get; init; }

    public int MaximumPercent { get; init; }
}
