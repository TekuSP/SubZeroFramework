namespace SubZeroFramework.Models;

public sealed record FrameworkServiceConfigurationUpdateRequest
{
    public required TimeSpan PollingInterval { get; init; }

    public required TimeSpan HardwareInfoPollingInterval { get; init; }

    public required bool AllowFanControlCommands { get; init; }
}
