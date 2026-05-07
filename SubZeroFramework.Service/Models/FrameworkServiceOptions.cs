namespace SubZeroFramework.Service.Models;

public sealed record FrameworkServiceOptions
{
    public TimeSpan PollingInterval { get; init; } = TimeSpan.FromSeconds(2);
}
