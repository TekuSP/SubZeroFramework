namespace SubZeroFramework.Services;

public sealed record FrameworkServiceControlInfo
{
    public required bool IsSupported { get; init; }

    public required bool IsInstalled { get; init; }

    public required bool CanUninstall { get; init; }

    public required bool CanInstall { get; init; }

    public required bool CanUpdate { get; init; }

    public required bool PackagedHelperAvailable { get; init; }

    public required bool IsElevatedSession { get; init; }

    public required bool? IsAutorunEnabled { get; init; }

    public required string PlatformServiceManager { get; init; }

    public required string ServiceIdentity { get; init; }

    public required string InstallSourceSummary { get; init; }

    public required string InstallReadinessMessage { get; init; }

    public required string PrivilegePromptMessage { get; init; }
}