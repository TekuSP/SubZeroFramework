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

    public bool IsGrpcActive { get; init; }

    public DateTimeOffset LastTelemetryObservedAt { get; init; }

    public bool RequiresElevation { get; init; }

    public string? LastError { get; init; }

    public bool IsFanControlEnabled { get; init; }

    public bool HasCallerIdentityValidation { get; init; }

    public string? FanControlAuthorizationMessage { get; init; }

    /// <summary>
    /// What the embedded controller reports about its own health, or null where nothing could be read.
    /// </summary>
    /// <remarks>
    /// Carried on the status rather than its own stream because it changes on the same cadence and for the
    /// same reasons: a controller that cannot answer these commands is usually a controller the rest of this
    /// record is already describing as unavailable.
    /// </remarks>
    public EcDiagnosticsSnapshot? EcDiagnostics { get; init; }
}
