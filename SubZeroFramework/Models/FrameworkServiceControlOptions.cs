namespace SubZeroFramework.Models;

public sealed record FrameworkServiceControlOptions
{
    public string WindowsServiceName { get; init; } = "SubZeroFrameworkService";

    public string? WindowsServiceExecutablePath { get; init; }

    public string LinuxUnitName { get; init; } = "subzeroframework.service";

    public string? LinuxServiceExecutablePath { get; init; }

    public string? LinuxServicePublishDirectory { get; init; }

    public string? LinuxUnitSourcePath { get; init; }

    public string LinuxInstalledWorkingDirectory { get; init; } = "/usr/local/lib/subzeroframework";

    public string LinuxInstalledExecutablePath { get; init; } = "/usr/local/bin/SubZeroFramework.Service";

    public string LinuxInstalledUnitPath { get; init; } = "/etc/systemd/system/subzeroframework.service";
}