using FrameworkDotnet.Enums;

namespace SubZeroFramework.Models;

public sealed record FrameworkSystemStatus
{
    public DateTimeOffset ObservedAt { get; init; }

    public string ConnectionLibraryVersion { get; init; } = string.Empty;

    public string? ConnectionLibraryInformationalVersion { get; init; }

    public bool IsLibraryAvailable { get; init; }

    public bool? IsFrameworkDevice { get; init; }

    public string? DeviceModel { get; init; }

    public FrameworkPlatform? Platform { get; init; }

    public FrameworkPlatformFamily? PlatformFamily { get; init; }

    public ImmutableArray<FrameworkEcDriver> SupportedDrivers { get; init; } = [];

    public FrameworkEcDriver? ActiveDriver { get; init; }

    public string? EcBuildInfo { get; init; }

    public bool IsEcPollingEnabled { get; init; }

    public bool IsConnectionOpen { get; init; }

    public string? LastError { get; init; }
}