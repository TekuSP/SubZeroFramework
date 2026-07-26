namespace SubZeroFramework.Models;

/// <summary>
/// Where the service helper lives and what it is called, for the in-app lifecycle actions.
/// </summary>
/// <remarks>
/// The path overrides below are BUILD-TIME ONLY and are not a post-install escape hatch. They are bound from
/// the <c>ServiceControl</c> section of <c>appsettings.json</c>, which ships as an embedded resource and is
/// never copied to the publish output — so an installed app has no file to edit and these cannot be changed
/// after the fact. They exist so a developer can point a local build at a custom layout.
///
/// The supported layouts are the ones the packaging produces: on Windows the MSI installs the helper where
/// the probe already looks, and on Linux the distro package (or the exact tarball layout in docs/INSTALL.md)
/// puts it beside the UI. Neither needs an override, and support guidance must not suggest one.
/// </remarks>
public sealed record FrameworkServiceControlOptions
{
    public string WindowsServiceName { get; init; } = "SubZeroFrameworkService";

    /// <summary>Build-time override only — see the type remarks.</summary>
    public string? WindowsServiceExecutablePath { get; init; }

    public string LinuxUnitName { get; init; } = "subzeroframework.service";

    /// <summary>Build-time override only — see the type remarks.</summary>
    public string? LinuxServiceExecutablePath { get; init; }

    /// <summary>Build-time override only — see the type remarks.</summary>
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