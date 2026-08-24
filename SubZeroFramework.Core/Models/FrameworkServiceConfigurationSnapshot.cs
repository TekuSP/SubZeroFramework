namespace SubZeroFramework.Models;

public sealed record FrameworkServiceConfigurationSnapshot
{
    /// <summary>PRIMARY tier — see <see cref="PollingTiers.Primary"/>.</summary>
    public required TimeSpan PollingInterval { get; init; }

    /// <summary>SECONDARY tier — see <see cref="PollingTiers.Secondary"/>.</summary>
    public required TimeSpan SecondaryPollingInterval { get; init; }

    /// <summary>TERTIARY tier — see <see cref="PollingTiers.Tertiary"/>.</summary>
    public required TimeSpan HardwareInfoPollingInterval { get; init; }

    public required bool AllowFanControlCommands { get; init; }

    public required string PersistentConfigurationPath { get; init; }
}
