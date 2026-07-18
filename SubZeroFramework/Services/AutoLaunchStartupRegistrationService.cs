using AutoLaunch;

namespace SubZeroFramework.Services;

/// <summary>
/// "Start with system boot" registration for the client app: a per-user login-time launch entry pointing
/// at the current executable. Client-only — the background service has its own autorun handled by the
/// service manager (SCM / systemd).
/// </summary>
public interface IStartupRegistrationService
{
    /// <summary>Whether launch-at-sign-in registration is available on this platform.</summary>
    bool IsSupported { get; }

    bool IsEnabled();

    /// <summary>Registers or removes the launch-at-sign-in entry. Returns false when the write failed.</summary>
    bool TrySetEnabled(bool enabled);
}

/// <summary>
/// Cross-platform implementation backed by the AutoLaunch library: HKCU Run key on Windows, freedesktop
/// autostart entry (~/.config/autostart) on Linux, LaunchAgent on macOS — the same mechanisms the earlier
/// hand-rolled per-platform services used, behind one API.
/// </summary>
public sealed class AutoLaunchStartupRegistrationService : IStartupRegistrationService
{
    // Matches the value name the previous hand-rolled Windows implementation wrote to the HKCU Run key,
    // so registrations made before the library swap keep reading as enabled.
    private const string AppName = "SubZeroFramework";

    public bool IsSupported =>
        (OperatingSystem.IsWindows() || OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
        && !string.IsNullOrEmpty(Environment.ProcessPath);

    public bool IsEnabled()
    {
        if (!IsSupported)
        {
            return false;
        }

        var (success, enabled) = CreateBuilder().BuildSafe().TryGetStatus();
        return success && enabled;
    }

    public bool TrySetEnabled(bool enabled)
    {
        if (!IsSupported)
        {
            return false;
        }

        var launcher = CreateBuilder().BuildSafe();
        return enabled ? launcher.TryEnable() : launcher.TryDisable();
    }

    // Built per call: construction is pure configuration, and the safe-mode launcher reports failure by
    // return value, so a broken environment degrades to the settings row's failure message, not a crash.
    private static AutoLaunchBuilder CreateBuilder()
        => new AutoLaunchBuilder()
            .Automatic()
            .SetAppName(AppName)
            .SetAppPath(Environment.ProcessPath!)
            .SetWorkScope(WorkScope.CurrentUser)
            .SetExtraConfigIf(OperatingSystem.IsLinux(), "X-GNOME-Autostart-enabled=true");
}
