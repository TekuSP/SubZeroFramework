namespace SubZeroFramework.Models;

public sealed record FrameworkServiceConfigurationUpdateResult
{
    public required bool Succeeded { get; init; }

    public required string Message { get; init; }

    public required FrameworkServiceConfigurationSnapshot Configuration { get; init; }
}
