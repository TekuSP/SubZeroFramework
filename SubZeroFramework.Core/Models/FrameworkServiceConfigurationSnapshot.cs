namespace SubZeroFramework.Models;

public sealed record FrameworkServiceConfigurationSnapshot
{
    /// <summary>PRIMARY tier — see <see cref="PollingTiers.Primary"/>.</summary>
    public required TimeSpan PollingInterval { get; init; }

    /// <summary>SECONDARY tier — see <see cref="PollingTiers.Secondary"/>.</summary>
    public required TimeSpan SecondaryPollingInterval { get; init; }

    /// <summary>TERTIARY tier — see <see cref="PollingTiers.Tertiary"/>.</summary>
    public required TimeSpan HardwareInfoPollingInterval { get; init; }

    /// <summary>How long each tier's samples are kept for history. See the apply request for why they pair.</summary>
    public required TimeSpan PrimaryRetention { get; init; }

    public required TimeSpan SecondaryRetention { get; init; }

    public required TimeSpan TertiaryRetention { get; init; }

    public required bool AllowFanControlCommands { get; init; }

    public required string PersistentConfigurationPath { get; init; }
}
