namespace SubZeroFramework.Models;

public sealed record FrameworkServiceConfigurationApplyRequest
{
    /// <summary>PRIMARY tier — see <see cref="PollingTiers.Primary"/>.</summary>
    public required TimeSpan PollingInterval { get; init; }

    /// <summary>SECONDARY tier — see <see cref="PollingTiers.Secondary"/>.</summary>
    public required TimeSpan SecondaryPollingInterval { get; init; }

    /// <summary>TERTIARY tier — see <see cref="PollingTiers.Tertiary"/>.</summary>
    public required TimeSpan HardwareInfoPollingInterval { get; init; }

    /// <summary>
    /// How long each tier's samples are kept for history.
    /// </summary>
    /// <remarks>
    /// Paired with the intervals rather than given a section of its own, because the two only mean anything
    /// together: an interval decides how much data a tier produces and the retention decides how much of it
    /// is held, and the memory cost is their product.
    /// </remarks>
    /// <remarks>
    /// Deliberately NOT required, defaulting to zero — which the manager reads as "leave this tier's
    /// retention alone". A caller changing only the intervals should not have to restate retention, and
    /// forcing it would mean every such caller carries a value it does not care about and might get wrong.
    /// </remarks>
    public TimeSpan PrimaryRetention { get; init; }

    public TimeSpan SecondaryRetention { get; init; }

    public TimeSpan TertiaryRetention { get; init; }

    public required bool AllowFanControlCommands { get; init; }
}
