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

    // Where the .deb/.rpm/AUR packages install the service (see packaging/linux/build-linux-packages.sh).
    // These are DETECTION-ONLY paths: the in-app installer never writes here, because a package-managed
    // install belongs to the distro's package manager.
    //
    // Why this matters: without probing these, a working package-managed service reports "not installed",
    // the app offers to install one, and the in-app flow writes a unit into /etc/systemd/system/ — which
    // TAKES PRECEDENCE over /usr/lib/systemd/system/ in systemd's search order. That leaves a shadowing
    // unit pointing at /usr/local binaries which survives `apt remove`.

    public string LinuxPackagedUnitPath { get; init; } = "/usr/lib/systemd/system/subzeroframework.service";

    public string LinuxPackagedExecutablePath { get; init; } = "/usr/lib/subzeroframework/service/SubZeroFramework.Service";
}