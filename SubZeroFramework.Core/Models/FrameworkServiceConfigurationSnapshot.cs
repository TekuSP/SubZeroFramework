namespace SubZeroFramework.Models;

public sealed record FrameworkServiceConfigurationSnapshot
{
    public required TimeSpan PollingInterval { get; init; }

    public required TimeSpan HardwareInfoPollingInterval { get; init; }

    public required bool AllowFanControlCommands { get; init; }

    public required string PersistentConfigurationPath { get; init; }
}
