using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;

namespace SubZeroFramework.Service;

internal static class FrameworkServiceManagementCli
{
    private const string CommandSwitch = "--service-management";
    private const string WindowsServiceName = "SubZeroFrameworkService";
    private const string LinuxUnitName = "subzeroframework.service";
    private const string LinuxInstalledWorkingDirectory = "/usr/local/lib/subzeroframework";
    private const string LinuxInstalledExecutablePath = "/usr/local/bin/SubZeroFramework.Service";
    private const string LinuxInstalledUnitPath = "/etc/systemd/system/subzeroframework.service";

    public static async Task<int?> TryExecuteAsync(string[] args)
    {
        if (args.Length < 2 || !string.Equals(args[0], CommandSwitch, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var operation = NormalizeOperation(args[1]);

        try
        {
            EnsureManagementPrivileges(operation);
            await ExecuteAsync(operation).ConfigureAwait(false);
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception.Message);
            return 1;
        }
    }

    private static string NormalizeOperation(string operation)
        => operation.ToLowerInvariant() switch
        {
            "shutdown" => "shutdown",
            "restart" => "restart",
            "enable-autorun" => "enable-autorun",
            "disable-autorun" => "disable-autorun",
            "install" => "install",
            "update" => "update",
            "uninstall" => "uninstall",
            _ => throw new InvalidOperationException($"Unsupported service management operation '{operation}'."),
        };

    private static Task ExecuteAsync(string operation)
        => operation switch
        {
            "shutdown" => ShutdownAsync(),
            "restart" => RestartAsync(),
            "enable-autorun" => EnableAutorunAsync(),
            "disable-autorun" => DisableAutorunAsync(),
            "install" => InstallAsync(),
            "update" => UpdateAsync(),
            "uninstall" => UninstallAsync(),
            _ => throw new InvalidOperationException($"Unsupported service management operation '{operation}'."),
        };

    private static void EnsureManagementPrivileges(string operation)
    {
        if (OperatingSystem.IsWindows() && !IsRunningAsAdministrator())
        {
            throw new InvalidOperationException($"Service management operation '{operation}' requires administrator privileges on Windows. Re-run the packaged service executable as administrator so the client can complete this request.");
        }

        if (OperatingSystem.IsLinux() && !IsRunningAsRoot())
        {
            throw new InvalidOperationException($"Service management operation '{operation}' requires root privileges on Linux. Re-run the packaged service executable with root privileges so the client can complete this request.");
        }
    }

    private static bool IsRunningAsAdministrator()
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        using var identity = WindowsIdentity.GetCurrent();
        if (identity is null)
        {
            return false;
        }

        var principal = new WindowsPrincipal(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }

    private static bool IsRunningAsRoot()
    {
        if (!OperatingSystem.IsLinux())
        {
            return false;
        }

        return GetEffectiveUserId() == 0;
    }

    [DllImport("libc", EntryPoint = "geteuid")]
    private static extern uint GetEffectiveUserId();

    private static async Task ShutdownAsync()
    {
        if (OperatingSystem.IsWindows())
        {
            await RunProcessAsync("net.exe", ["stop", WindowsServiceName], allowFailure: true).ConfigureAwait(false);
            return;
        }

        if (OperatingSystem.IsLinux())
        {
            await RunShellScriptAsync($"systemctl stop {QuotePosixArgument(LinuxUnitName)}", allowFailure: true).ConfigureAwait(false);
            return;
        }

        throw CreateUnsupportedPlatformException();
    }

    private static async Task RestartAsync()
    {
        if (OperatingSystem.IsWindows())
        {
            await RunProcessAsync("net.exe", ["stop", WindowsServiceName], allowFailure: true).ConfigureAwait(false);
            await RunProcessAsync("net.exe", ["start", WindowsServiceName]).ConfigureAwait(false);
            return;
        }

        if (OperatingSystem.IsLinux())
        {
            await RunShellScriptAsync($"systemctl restart {QuotePosixArgument(LinuxUnitName)}").ConfigureAwait(false);
            return;
        }

        throw CreateUnsupportedPlatformException();
    }

    private static async Task EnableAutorunAsync()
    {
        if (OperatingSystem.IsWindows())
        {
            await RunProcessAsync("sc.exe", ["config", WindowsServiceName, "start=", "auto"]).ConfigureAwait(false);
            return;
        }

        if (OperatingSystem.IsLinux())
        {
            await RunShellScriptAsync($"systemctl enable {QuotePosixArgument(LinuxUnitName)}").ConfigureAwait(false);
            return;
        }

        throw CreateUnsupportedPlatformException();
    }

    private static async Task DisableAutorunAsync()
    {
        if (OperatingSystem.IsWindows())
        {
            await RunProcessAsync("sc.exe", ["config", WindowsServiceName, "start=", "disabled"]).ConfigureAwait(false);
            return;
        }

        if (OperatingSystem.IsLinux())
        {
            await RunShellScriptAsync($"systemctl disable {QuotePosixArgument(LinuxUnitName)}", allowFailure: true).ConfigureAwait(false);
            return;
        }

        throw CreateUnsupportedPlatformException();
    }

    private static async Task InstallAsync()
    {
        if (OperatingSystem.IsWindows())
        {
            await RunProcessAsync("sc.exe", ["create", WindowsServiceName, "binPath=", SourceExecutablePath, "start=", "demand"]).ConfigureAwait(false);
            await RunProcessAsync("sc.exe", ["failure", WindowsServiceName, "reset=", "0", "actions=", "restart/5000/restart/5000/restart/5000"]).ConfigureAwait(false);
            await RunProcessAsync("net.exe", ["start", WindowsServiceName]).ConfigureAwait(false);
            return;
        }

        if (OperatingSystem.IsLinux())
        {
            var script = string.Join(" && ",
            [
                $"install -d -m 755 {QuotePosixArgument(LinuxInstalledWorkingDirectory)}",
                $"rm -rf {QuotePosixArgument(LinuxInstalledWorkingDirectory)}/*",
                $"cp -R {QuotePosixArgument(Path.Combine(SourceDirectory, "."))} {QuotePosixArgument(LinuxInstalledWorkingDirectory)}",
                $"install -m 755 {QuotePosixArgument(Path.Combine(LinuxInstalledWorkingDirectory, "SubZeroFramework.Service"))} {QuotePosixArgument(LinuxInstalledExecutablePath)}",
                $"install -m 644 {QuotePosixArgument(ResolveLinuxUnitSourcePath())} {QuotePosixArgument(LinuxInstalledUnitPath)}",
                "systemctl daemon-reload",
                $"systemctl start {QuotePosixArgument(LinuxUnitName)}",
            ]);

            await RunShellScriptAsync(script).ConfigureAwait(false);
            return;
        }

        throw CreateUnsupportedPlatformException();
    }

    private static async Task UpdateAsync()
    {
        if (OperatingSystem.IsWindows())
        {
            await RunProcessAsync("net.exe", ["stop", WindowsServiceName], allowFailure: true).ConfigureAwait(false);
            await RunProcessAsync("sc.exe", ["config", WindowsServiceName, "binPath=", SourceExecutablePath]).ConfigureAwait(false);
            await RunProcessAsync("sc.exe", ["failure", WindowsServiceName, "reset=", "0", "actions=", "restart/5000/restart/5000/restart/5000"]).ConfigureAwait(false);
            await RunProcessAsync("net.exe", ["start", WindowsServiceName]).ConfigureAwait(false);
            return;
        }

        if (OperatingSystem.IsLinux())
        {
            var script = string.Join(" && ",
            [
                $"systemctl stop {QuotePosixArgument(LinuxUnitName)} || true",
                $"install -d -m 755 {QuotePosixArgument(LinuxInstalledWorkingDirectory)}",
                $"rm -rf {QuotePosixArgument(LinuxInstalledWorkingDirectory)}/*",
                $"cp -R {QuotePosixArgument(Path.Combine(SourceDirectory, "."))} {QuotePosixArgument(LinuxInstalledWorkingDirectory)}",
                $"install -m 755 {QuotePosixArgument(Path.Combine(LinuxInstalledWorkingDirectory, "SubZeroFramework.Service"))} {QuotePosixArgument(LinuxInstalledExecutablePath)}",
                $"install -m 644 {QuotePosixArgument(ResolveLinuxUnitSourcePath())} {QuotePosixArgument(LinuxInstalledUnitPath)}",
                "systemctl daemon-reload",
                $"systemctl start {QuotePosixArgument(LinuxUnitName)}",
            ]);

            await RunShellScriptAsync(script).ConfigureAwait(false);
            return;
        }

        throw CreateUnsupportedPlatformException();
    }

    private static async Task UninstallAsync()
    {
        if (OperatingSystem.IsWindows())
        {
            await RunProcessAsync("net.exe", ["stop", WindowsServiceName], allowFailure: true).ConfigureAwait(false);
            await RunProcessAsync("sc.exe", ["delete", WindowsServiceName], allowFailure: true).ConfigureAwait(false);
            return;
        }

        if (OperatingSystem.IsLinux())
        {
            var script = string.Join(" ; ",
            [
                $"systemctl stop {QuotePosixArgument(LinuxUnitName)} || true",
                $"systemctl disable {QuotePosixArgument(LinuxUnitName)} || true",
                $"rm -f {QuotePosixArgument(LinuxInstalledUnitPath)}",
                $"rm -f {QuotePosixArgument(LinuxInstalledExecutablePath)}",
                $"rm -rf {QuotePosixArgument(LinuxInstalledWorkingDirectory)}",
                "systemctl daemon-reload",
            ]);

            await RunShellScriptAsync(script).ConfigureAwait(false);
            return;
        }

        throw CreateUnsupportedPlatformException();
    }

    private static async Task RunShellScriptAsync(string command, bool allowFailure = false)
    {
        await RunProcessAsync("/bin/sh", ["-c", command], allowFailure).ConfigureAwait(false);
    }

    private static async Task RunProcessAsync(string fileName, IReadOnlyList<string> arguments, bool allowFailure = false)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = fileName,
                UseShellExecute = false,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
            }
        };

        foreach (var argument in arguments)
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        process.Start();

        var standardOutputTask = process.StandardOutput.ReadToEndAsync();
        var standardErrorTask = process.StandardError.ReadToEndAsync();

        await process.WaitForExitAsync().ConfigureAwait(false);

        var standardOutput = await standardOutputTask.ConfigureAwait(false);
        var standardError = await standardErrorTask.ConfigureAwait(false);

        if (process.ExitCode == 0 || allowFailure)
        {
            return;
        }

        var details = string.IsNullOrWhiteSpace(standardError)
            ? string.IsNullOrWhiteSpace(standardOutput)
                ? $"{fileName} exited with code {process.ExitCode}."
                : standardOutput.Trim()
            : standardError.Trim();

        throw new InvalidOperationException(details);
    }

    private static string ResolveLinuxUnitSourcePath()
    {
        var candidatePaths = new[]
        {
            Path.Combine(SourceDirectory, LinuxUnitName),
            Path.Combine(SourceDirectory, "subzeroframework.service"),
        };

        return candidatePaths.FirstOrDefault(File.Exists)
            ?? throw new InvalidOperationException($"Unable to locate {LinuxUnitName} next to the published service package.");
    }

    private static string SourceExecutablePath
        => Environment.ProcessPath
            ?? throw new InvalidOperationException("Unable to determine the current service executable path.");

    private static string SourceDirectory
        => Path.GetDirectoryName(SourceExecutablePath)
            ?? throw new InvalidOperationException("Unable to determine the current service package directory.");

    private static string QuotePosixArgument(string value)
        => $"'{value.Replace("'", "'\\''")}'";

    private static PlatformNotSupportedException CreateUnsupportedPlatformException()
        => new("Service management commands are only implemented for Windows services and Linux systemd.");
}
