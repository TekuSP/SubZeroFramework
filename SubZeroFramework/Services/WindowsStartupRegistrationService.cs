using Microsoft.Win32;

namespace SubZeroFramework.Services;

/// <summary>
/// "Start with Windows" registration for the client app: a per-user Run-key entry pointing at the current
/// executable. Client-only — the background service has its own autorun handled by the service manager.
/// </summary>
public interface IStartupRegistrationService
{
    /// <summary>Whether launch-at-sign-in registration is available on this platform.</summary>
    bool IsSupported { get; }

    bool IsEnabled();

    /// <summary>Registers or removes the launch-at-sign-in entry. Returns false when the write failed.</summary>
    bool TrySetEnabled(bool enabled);
}

public sealed class WindowsStartupRegistrationService : IStartupRegistrationService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValueName = "SubZeroFramework";

    public bool IsSupported => OperatingSystem.IsWindows() && !string.IsNullOrEmpty(Environment.ProcessPath);

    public bool IsEnabled()
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        try
        {
            using var runKey = Registry.CurrentUser.OpenSubKey(RunKeyPath);
            return runKey?.GetValue(RunValueName) is string;
        }
        catch (Exception exception) when (exception is System.Security.SecurityException or IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    public bool TrySetEnabled(bool enabled)
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        try
        {
            using var runKey = Registry.CurrentUser.CreateSubKey(RunKeyPath);

            if (enabled)
            {
                if (Environment.ProcessPath is not string executablePath || string.IsNullOrEmpty(executablePath))
                {
                    return false;
                }

                runKey.SetValue(RunValueName, $"\"{executablePath}\"");
            }
            else
            {
                runKey.DeleteValue(RunValueName, throwOnMissingValue: false);
            }

            return true;
        }
        catch (Exception exception) when (exception is System.Security.SecurityException or IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
