namespace SubZeroFramework.Service.Models;

public sealed record FrameworkServiceOptions
{
    public TimeSpan PollingInterval { get; init; } = TimeSpan.FromMilliseconds(150);

    public bool AllowFanControlCommands { get; init; }
}
