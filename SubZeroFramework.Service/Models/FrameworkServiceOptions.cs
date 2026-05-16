namespace SubZeroFramework.Service.Models;

public sealed record FrameworkServiceOptions
{
    public TimeSpan PollingInterval { get; init; } = TimeSpan.FromMilliseconds(150);

    public TimeSpan HardwareInfoPollingInterval { get; init; } = TimeSpan.FromSeconds(1);

    public bool AllowFanControlCommands { get; init; }

    public FanControlStateOptions[] FanControlStates { get; init; } = [];
}
