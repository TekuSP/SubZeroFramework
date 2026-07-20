using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.Versioning;
using System.Security.Principal;

using Microsoft.Extensions.Options;

using SubZeroFramework.Models;

namespace SubZeroFramework.Services;

public sealed class LocalFrameworkServiceControlClient : IFrameworkServiceControlClient
{
    private const string WindowsServiceManagerName = "Windows Service Control Manager";
    private const string LinuxServiceManagerName = "systemd";
    private const string ServiceManagementSwitch = "--service-management";

    private readonly FrameworkServiceControlOptions _options;
    private readonly ILogger<LocalFrameworkServiceControlClient> _logger;

    public LocalFrameworkServiceControlClient(IOptions<FrameworkServiceControlOptions> options, ILogger<LocalFrameworkServiceControlClient> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _options = options.Value;
        _logger = logger;
    }

    public FrameworkServiceControlInfo GetInfo()
    {
        if (OperatingSystem.IsWindows())
        {
            var executablePath = ResolveWindowsServiceExecutablePath();
            var packagedHelperAvailable = executablePath is not null;
            var isElevated = IsRunningAsAdministrator();
            var isInstalled = IsWindowsServiceInstalled();
            var isAutorunEnabled = isInstalled ? QueryWindowsAutorunEnabled() : null;

            return new FrameworkServiceControlInfo
            {
                IsSupported = true,
                IsInstalled = isInstalled,
                CanUninstall = isInstalled,
                CanInstall = packagedHelperAvailable && !isInstalled,
                CanUpdate = packagedHelperAvailable && isInstalled,
                PackagedHelperAvailable = packagedHelperAvailable,
                IsElevatedSession = isElevated,
                IsAutorunEnabled = isAutorunEnabled,
                PlatformServiceManager = WindowsServiceManagerName,
                ServiceIdentity = _options.WindowsServiceName,
                InstallSourceSummary = executablePath is null
                    ? "Service package unavailable. Place a packaged service executable under service-package/windows or configure ServiceControl:WindowsServiceExecutablePath."
                    : $"Install source executable: {executablePath}",
                InstallReadinessMessage = executablePath is null
                    ? "Windows install and update stay disabled until the packaged service executable is discoverable."
                    : isInstalled
                        ? isElevated
                            ? "Windows update is ready. This client session already has administrator privileges, so the packaged service executable can refresh the SCM entry directly."
                            : "Windows update is ready, but this client session is not elevated. The packaged service executable will request a UAC administrator prompt before it can refresh the SCM entry."
                        : isElevated
                            ? "Windows install is ready. This client session already has administrator privileges, so the packaged service executable can register the SCM entry directly."
                            : "Windows install is ready, but this client session is not elevated. The packaged service executable will request a UAC administrator prompt before it can register the SCM entry.",
                PrivilegePromptMessage = isElevated
                    ? "This client session already has administrator privileges. Windows service lifecycle actions can launch the packaged service helper directly without another UAC prompt."
                    : "This client session does not currently have administrator privileges. Windows service lifecycle actions will show a UAC administrator prompt before the packaged service helper can continue.",
            };
        }

        if (OperatingSystem.IsLinux())
        {
            var executablePath = ResolveLinuxServiceExecutablePath();
            var canInstall = executablePath is not null;
            var isRoot = ClientLinuxPrivilegeDetector.IsRunningAsRoot();
            var isInstalled = IsLinuxServiceInstalled();
            var isAutorunEnabled = isInstalled ? QueryLinuxAutorunEnabled() : null;

            return new FrameworkServiceControlInfo
            {
                IsSupported = true,
                IsInstalled = isInstalled,
                CanUninstall = isInstalled,
                CanInstall = canInstall && !isInstalled,
                CanUpdate = canInstall && isInstalled,
                PackagedHelperAvailable = canInstall,
                IsElevatedSession = isRoot,
                IsAutorunEnabled = isAutorunEnabled,
                PlatformServiceManager = LinuxServiceManagerName,
                ServiceIdentity = _options.LinuxUnitName,
                InstallSourceSummary = canInstall
                    ? $"Install source executable: {executablePath}"
                    : "Service package unavailable. Place a packaged service executable under service-package/linux or configure ServiceControl:LinuxServiceExecutablePath.",
                InstallReadinessMessage = canInstall
                    ? isInstalled
                        ? isRoot
                            ? $"Linux update is ready. This client session is already running as root, so the packaged service executable can refresh {_options.LinuxInstalledWorkingDirectory} and {_options.LinuxInstalledUnitPath} directly."
                            : $"Linux update is ready, but this client session is not running as root. The packaged service executable will request pkexec or root authentication before it can refresh {_options.LinuxInstalledWorkingDirectory} and {_options.LinuxInstalledUnitPath}."
                        : isRoot
                            ? $"Linux install is ready. This client session is already running as root, so the packaged service executable can copy itself into {_options.LinuxInstalledWorkingDirectory} and register {_options.LinuxInstalledUnitPath} directly."
                            : $"Linux install is ready, but this client session is not running as root. The packaged service executable will request pkexec or root authentication before it can copy itself into {_options.LinuxInstalledWorkingDirectory} and register {_options.LinuxInstalledUnitPath}."
                    : "Linux install and update stay disabled until the packaged service executable is discoverable.",
                PrivilegePromptMessage = isRoot
                    ? "This client session is already running as root. Linux service lifecycle actions can launch the packaged service helper directly without another privilege prompt."
                    : "This client session is not running as root. Linux service lifecycle actions may show a pkexec or root authentication prompt before the packaged service helper can continue.",
            };
        }

        return new FrameworkServiceControlInfo
        {
            IsSupported = false,
            IsInstalled = false,
            CanUninstall = false,
            CanInstall = false,
            CanUpdate = false,
            PackagedHelperAvailable = false,
            IsElevatedSession = false,
            IsAutorunEnabled = null,
            PlatformServiceManager = "Unsupported platform",
            ServiceIdentity = "Unsupported platform",
            InstallSourceSummary = "Service lifecycle requests are only implemented for Windows services and Linux systemd.",
            InstallReadinessMessage = "No service lifecycle manager is available on this platform.",
            PrivilegePromptMessage = "Unsupported platform.",
        };
    }

    public Task<FrameworkServiceCommandResult> ShutdownAsync(CancellationToken cancellationToken = default)
        => ExecuteOperationAsync(
            "Shutdown service",
            CreateShutdownCommands,
            "Requested service shutdown.",
            cancellationToken);

    public Task<FrameworkServiceCommandResult> RestartAsync(CancellationToken cancellationToken = default)
        => ExecuteOperationAsync(
            "Restart service",
            CreateRestartCommands,
            "Requested service restart.",
            cancellationToken);

    public Task<FrameworkServiceCommandResult> EnableAutorunAsync(CancellationToken cancellationToken = default)
        => ExecuteOperationAsync(
            "Enable service autorun",
            CreateEnableAutorunCommands,
            "Requested automatic service startup.",
            cancellationToken);

    public Task<FrameworkServiceCommandResult> DisableAutorunAsync(CancellationToken cancellationToken = default)
        => ExecuteOperationAsync(
            "Disable service autorun",
            CreateDisableAutorunCommands,
            "Requested automatic service startup disablement.",
            cancellationToken);

    public Task<FrameworkServiceCommandResult> InstallAsync(CancellationToken cancellationToken = default)
        => ExecuteOperationAsync(
            "Install service",
            CreateInstallCommands,
            "Requested service installation.",
            cancellationToken);

    public Task<FrameworkServiceCommandResult> UpdateAsync(CancellationToken cancellationToken = default)
        => ExecuteOperationAsync(
            "Update service",
            CreateUpdateCommands,
            "Requested service update.",
            cancellationToken);

    public Task<FrameworkServiceCommandResult> UninstallAsync(CancellationToken cancellationToken = default)
        => ExecuteOperationAsync(
            "Uninstall service",
            CreateUninstallCommands,
            "Requested service uninstallation.",
            cancellationToken);

    public async Task<FrameworkServiceCommandResult> ReinstallAsync(CancellationToken cancellationToken = default)
    {
        var uninstallResult = await UninstallAsync(cancellationToken).ConfigureAwait(false);
        if (!uninstallResult.Succeeded)
        {
            return uninstallResult with
            {
                OperationName = "Reinstall service",
                Message = $"Reinstall stopped during the uninstall step. {uninstallResult.Message}",
            };
        }

        var installResult = await InstallAsync(cancellationToken).ConfigureAwait(false);
        return new FrameworkServiceCommandResult
        {
            OperationName = "Reinstall service",
            Succeeded = installResult.Succeeded,
            Kind = installResult.Kind,
            Message = installResult.Succeeded
                ? "Requested service reinstall."
                : $"Reinstall stopped during the install step. {installResult.Message}",
        };
    }

    private async Task<FrameworkServiceCommandResult> ExecuteOperationAsync(string operationName, Func<IReadOnlyList<ProcessStartInfo>> commandFactory, string successMessage, CancellationToken cancellationToken)
    {
        try
        {
            var startInfos = commandFactory();
            foreach (var startInfo in startInfos)
            {
                await RunProcessAsync(startInfo, operationName, cancellationToken).ConfigureAwait(false);
            }

            _logger.LogInformation("Completed client-side service operation {OperationName}.", operationName);

            return new FrameworkServiceCommandResult
            {
                OperationName = operationName,
                Succeeded = true,
                Kind = FrameworkServiceCommandResultKind.Success,
                Message = successMessage,
            };
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Cancelled client-side service operation {OperationName}.", operationName);

            return new FrameworkServiceCommandResult
            {
                OperationName = operationName,
                Succeeded = false,
                Kind = FrameworkServiceCommandResultKind.Warning,
                Message = $"{operationName} was cancelled.",
            };
        }
        catch (Win32Exception exception) when (IsWindowsElevationCancelled(exception))
        {
            _logger.LogWarning(exception, "Client-side service operation {OperationName} was cancelled at the elevation prompt.", operationName);

            return new FrameworkServiceCommandResult
            {
                OperationName = operationName,
                Succeeded = false,
                Kind = FrameworkServiceCommandResultKind.Warning,
                Message = "Administrator approval was cancelled. Re-run the action and approve the UAC prompt if you still want to continue.",
            };
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Client-side service operation {OperationName} failed.", operationName);

            if (TryCreateWarningMessage(exception, out var warningMessage))
            {
                return new FrameworkServiceCommandResult
                {
                    OperationName = operationName,
                    Succeeded = false,
                    Kind = FrameworkServiceCommandResultKind.Warning,
                    Message = warningMessage,
                };
            }

            return new FrameworkServiceCommandResult
            {
                OperationName = operationName,
                Succeeded = false,
                Kind = FrameworkServiceCommandResultKind.Error,
                Message = exception.Message,
            };
        }
    }

    private async Task RunProcessAsync(ProcessStartInfo startInfo, string operationName, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Launching {OperationName} via {CommandLine}.", operationName, FormatCommandLine(startInfo));

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Unable to start the process for {operationName}.");

        if (!startInfo.UseShellExecute)
        {
            var standardOutputTask = process.StandardOutput.ReadToEndAsync();
            var standardErrorTask = process.StandardError.ReadToEndAsync();

            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

            var standardOutput = await standardOutputTask.ConfigureAwait(false);
            var standardError = await standardErrorTask.ConfigureAwait(false);

            if (process.ExitCode != 0)
            {
                var details = string.IsNullOrWhiteSpace(standardError)
                    ? string.IsNullOrWhiteSpace(standardOutput)
                        ? $"{operationName} failed with exit code {process.ExitCode}."
                        : standardOutput.Trim()
                    : standardError.Trim();

                throw new InvalidOperationException($"{operationName} failed with exit code {process.ExitCode}. {details}");
            }

            return;
        }

        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"{operationName} failed with exit code {process.ExitCode}.");
        }
    }

    private IReadOnlyList<ProcessStartInfo> CreateShutdownCommands()
    {
        var serviceManagementProcess = TryCreateServiceManagementProcess("shutdown");
        if (serviceManagementProcess is not null)
        {
            return [serviceManagementProcess];
        }

        if (OperatingSystem.IsWindows())
        {
            return [CreateWindowsElevatedProcess("net.exe", $"stop {QuoteWindowsArgument(_options.WindowsServiceName)}")];
        }

        if (OperatingSystem.IsLinux())
        {
            return [CreateLinuxPrivilegedProcess("systemctl", "stop", _options.LinuxUnitName)];
        }

        throw CreateUnsupportedPlatformException();
    }

    private IReadOnlyList<ProcessStartInfo> CreateRestartCommands()
    {
        var serviceManagementProcess = TryCreateServiceManagementProcess("restart");
        if (serviceManagementProcess is not null)
        {
            return [serviceManagementProcess];
        }

        if (OperatingSystem.IsWindows())
        {
            return [CreateWindowsElevatedCmdProcess($"net.exe stop {QuoteWindowsArgument(_options.WindowsServiceName)} & net.exe start {QuoteWindowsArgument(_options.WindowsServiceName)}")];
        }

        if (OperatingSystem.IsLinux())
        {
            return [CreateLinuxPrivilegedProcess("systemctl", "restart", _options.LinuxUnitName)];
        }

        throw CreateUnsupportedPlatformException();
    }

    private IReadOnlyList<ProcessStartInfo> CreateEnableAutorunCommands()
    {
        var serviceManagementProcess = TryCreateServiceManagementProcess("enable-autorun");
        if (serviceManagementProcess is not null)
        {
            return [serviceManagementProcess];
        }

        if (OperatingSystem.IsWindows())
        {
            return [CreateWindowsElevatedProcess("sc.exe", $"config {QuoteWindowsArgument(_options.WindowsServiceName)} start= auto")];
        }

        if (OperatingSystem.IsLinux())
        {
            return [CreateLinuxPrivilegedProcess("systemctl", "enable", _options.LinuxUnitName)];
        }

        throw CreateUnsupportedPlatformException();
    }

    private IReadOnlyList<ProcessStartInfo> CreateDisableAutorunCommands()
    {
        var serviceManagementProcess = TryCreateServiceManagementProcess("disable-autorun");
        if (serviceManagementProcess is not null)
        {
            return [serviceManagementProcess];
        }

        if (OperatingSystem.IsWindows())
        {
            return [CreateWindowsElevatedProcess("sc.exe", $"config {QuoteWindowsArgument(_options.WindowsServiceName)} start= disabled")];
        }

        if (OperatingSystem.IsLinux())
        {
            return [CreateLinuxPrivilegedProcess("systemctl", "disable", _options.LinuxUnitName)];
        }

        throw CreateUnsupportedPlatformException();
    }

    private IReadOnlyList<ProcessStartInfo> CreateInstallCommands()
    {
        return [CreateRequiredServiceManagementProcess("install")];
    }

    private IReadOnlyList<ProcessStartInfo> CreateUpdateCommands()
    {
        return [CreateRequiredServiceManagementProcess("update")];
    }

    private IReadOnlyList<ProcessStartInfo> CreateUninstallCommands()
    {
        var serviceManagementProcess = TryCreateServiceManagementProcess("uninstall");
        if (serviceManagementProcess is not null)
        {
            return [serviceManagementProcess];
        }

        if (OperatingSystem.IsWindows())
        {
            return [CreateWindowsElevatedCmdProcess($"net.exe stop {QuoteWindowsArgument(_options.WindowsServiceName)} & sc.exe delete {QuoteWindowsArgument(_options.WindowsServiceName)}")];
        }

        if (OperatingSystem.IsLinux())
        {
            var script = string.Join(" ; ",
            [
                $"systemctl stop {QuotePosixArgument(_options.LinuxUnitName)} || true",
                $"systemctl disable {QuotePosixArgument(_options.LinuxUnitName)} || true",
                $"rm -f {QuotePosixArgument(_options.LinuxInstalledUnitPath)}",
                $"rm -f {QuotePosixArgument(_options.LinuxInstalledExecutablePath)}",
                $"rm -rf {QuotePosixArgument(_options.LinuxInstalledWorkingDirectory)}",
                "systemctl daemon-reload",
            ]);

            return [CreateLinuxPrivilegedShellProcess(script)];
        }

        throw CreateUnsupportedPlatformException();
    }

    private ProcessStartInfo CreateRequiredServiceManagementProcess(string operation)
        => TryCreateServiceManagementProcess(operation)
            ?? throw new InvalidOperationException("Unable to locate a packaged service executable. Install and update require the published service package to be present next to the app or configured explicitly through ServiceControl options.");

    private ProcessStartInfo? TryCreateServiceManagementProcess(string operation)
    {
        var managementExecutablePath = ResolveServiceManagementExecutablePath();
        if (managementExecutablePath is null)
        {
            return null;
        }

        if (OperatingSystem.IsWindows())
        {
            return new ProcessStartInfo
            {
                FileName = managementExecutablePath,
                Arguments = $"{ServiceManagementSwitch} {operation}",
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden,
            };
        }

        if (OperatingSystem.IsLinux())
        {
            if (ClientLinuxPrivilegeDetector.IsRunningAsRoot())
            {
                return CreateDirectProcess(managementExecutablePath, ServiceManagementSwitch, operation);
            }

            return CreateDirectProcess("pkexec", managementExecutablePath, ServiceManagementSwitch, operation);
        }

        return null;
    }

    private string? ResolveServiceManagementExecutablePath()
    {
        if (OperatingSystem.IsWindows())
        {
            return ResolveWindowsServiceExecutablePath();
        }

        if (OperatingSystem.IsLinux())
        {
            return ResolveLinuxServiceExecutablePath();
        }

        return null;
    }

    private string? ResolveWindowsServiceExecutablePath()
        => ResolveExistingFile(
            _options.WindowsServiceExecutablePath,
            Path.Combine(AppContext.BaseDirectory, "service-package", "windows", "SubZeroFramework.Service.exe"),
            Path.Combine(AppContext.BaseDirectory, "SubZeroFramework.Service.exe"),
            Path.Combine(AppContext.BaseDirectory, "SubZeroFramework.Service", "SubZeroFramework.Service.exe"),
#if DEBUG
            // Dev-checkout fallback: the sibling service project's build output, so the whole lifecycle
            // (install → autorun → update → uninstall) is exercisable from an F5 build. Debug-only so an
            // installed Release app can never bind the SCM entry to a stray repo path.
            FindDevServiceExecutable("SubZeroFramework.Service.exe"),
#endif
            null);

    private string? ResolveLinuxServiceExecutablePath()
        => ResolveExistingFile(
            _options.LinuxServiceExecutablePath,
            _options.LinuxServicePublishDirectory is null ? null : Path.Combine(_options.LinuxServicePublishDirectory, "SubZeroFramework.Service"),
            Path.Combine(AppContext.BaseDirectory, "service-package", "linux", "SubZeroFramework.Service"),
            Path.Combine(AppContext.BaseDirectory, "SubZeroFramework.Service", "SubZeroFramework.Service"),
            Path.Combine(AppContext.BaseDirectory, "publish", "SubZeroFramework.Service", "SubZeroFramework.Service"),
#if DEBUG
            // Dev-checkout fallback (see the Windows twin above).
            FindDevServiceExecutable("SubZeroFramework.Service"),
#endif
            null);

#if DEBUG
    // Walks up from the app's output directory to find the sibling SubZeroFramework.Service project and
    // returns its newest matching build output. The app's output nests differently per platform/RID
    // (e.g. bin\ARM64\Debug\net10.0-windows10.0.26100\win-arm64), so a fixed relative "..\..\.." path is
    // brittle — searching for the SubZeroFramework.Service\bin marker at each parent is robust to all of them.
    private static string? FindDevServiceExecutable(string executableFileName)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        for (var depth = 0; depth < 8 && directory is not null; depth++, directory = directory.Parent)
        {
            var serviceBin = Path.Combine(directory.FullName, "SubZeroFramework.Service", "bin");
            if (!Directory.Exists(serviceBin))
            {
                continue;
            }

            try
            {
                return Directory
                    .EnumerateFiles(serviceBin, "*", SearchOption.AllDirectories)
                    .Where(path => string.Equals(Path.GetFileName(path), executableFileName, StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(File.GetLastWriteTimeUtc)
                    .FirstOrDefault();
            }
            catch (Exception)
            {
                return null;
            }
        }

        return null;
    }
#endif

    private string? ResolveLinuxServicePublishDirectory()
        => ResolveExistingDirectory(
            _options.LinuxServicePublishDirectory,
            Path.Combine(AppContext.BaseDirectory, "SubZeroFramework.Service"),
            Path.Combine(AppContext.BaseDirectory, "publish", "SubZeroFramework.Service"));

    private string? ResolveLinuxUnitSourcePath()
        => ResolveExistingFile(
            _options.LinuxUnitSourcePath,
            Path.Combine(AppContext.BaseDirectory, "subzeroframework.service"),
            Path.Combine(AppContext.BaseDirectory, "SubZeroFramework.Service", "subzeroframework.service"));

    private bool IsWindowsServiceInstalled()
    {
        try
        {
            return RunProbeProcess(CreateDirectProcess("sc.exe", "query", _options.WindowsServiceName)) == 0;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Unable to determine whether Windows service {ServiceName} is installed.", _options.WindowsServiceName);
            return false;
        }
    }

    private bool IsLinuxServiceInstalled()
    {
        try
        {
            return File.Exists(_options.LinuxInstalledUnitPath)
                || File.Exists(_options.LinuxInstalledExecutablePath);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Unable to determine whether Linux service unit {UnitName} is installed.", _options.LinuxUnitName);
            return false;
        }
    }

    private bool? QueryWindowsAutorunEnabled()
    {
        try
        {
            var probe = RunProbeProcessDetailed(CreateDirectProcess("sc.exe", "qc", _options.WindowsServiceName));
            if (probe.ExitCode != 0)
            {
                return null;
            }

            return FrameworkServiceAutorunStateParser.ParseWindowsScQcOutput(probe.StandardOutput);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Unable to determine whether Windows service {ServiceName} autorun is enabled.", _options.WindowsServiceName);
            return null;
        }
    }

    private bool? QueryLinuxAutorunEnabled()
    {
        try
        {
            var probe = RunProbeProcessDetailed(CreateDirectProcess("systemctl", "is-enabled", _options.LinuxUnitName));
            return FrameworkServiceAutorunStateParser.ParseLinuxSystemctlIsEnabledOutput(probe.StandardOutput, probe.ExitCode);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Unable to determine whether Linux service unit {UnitName} autorun is enabled.", _options.LinuxUnitName);
            return null;
        }
    }

    private static string? ResolveExistingFile(params string?[] candidates)
        => candidates
            .Where(candidate => !string.IsNullOrWhiteSpace(candidate))
            .Select(candidate => Path.GetFullPath(candidate!))
            .FirstOrDefault(File.Exists);

    private static string? ResolveExistingDirectory(params string?[] candidates)
        => candidates
            .Where(candidate => !string.IsNullOrWhiteSpace(candidate))
            .Select(candidate => Path.GetFullPath(candidate!))
            .FirstOrDefault(Directory.Exists);

    private static ProcessStartInfo CreateWindowsElevatedProcess(string fileName, string arguments)
        => new()
        {
            FileName = fileName,
            Arguments = arguments,
            UseShellExecute = true,
            Verb = "runas",
            WindowStyle = ProcessWindowStyle.Hidden,
        };

    private static ProcessStartInfo CreateWindowsElevatedCmdProcess(string command)
        => CreateWindowsElevatedProcess("cmd.exe", $"/c {command}");

    private static ProcessStartInfo CreateLinuxPrivilegedProcess(string fileName, params string[] arguments)
    {
        if (ClientLinuxPrivilegeDetector.IsRunningAsRoot())
        {
            return CreateDirectProcess(fileName, arguments);
        }

        return CreateDirectProcess("pkexec", [fileName, .. arguments]);
    }

    private static ProcessStartInfo CreateLinuxPrivilegedShellProcess(string command)
    {
        if (ClientLinuxPrivilegeDetector.IsRunningAsRoot())
        {
            return CreateDirectProcess("/bin/sh", "-c", command);
        }

        return CreateDirectProcess("pkexec", "/bin/sh", "-c", command);
    }

    private static ProcessStartInfo CreateDirectProcess(string fileName, params string[] arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            UseShellExecute = false,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        return startInfo;
    }

    private static int RunProbeProcess(ProcessStartInfo startInfo)
    {
        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Unable to start {startInfo.FileName} while probing local service state.");

        var standardOutputTask = process.StandardOutput.ReadToEndAsync();
        var standardErrorTask = process.StandardError.ReadToEndAsync();

        process.WaitForExit();

        _ = standardOutputTask.GetAwaiter().GetResult();
        _ = standardErrorTask.GetAwaiter().GetResult();

        return process.ExitCode;
    }

    private static (int ExitCode, string StandardOutput, string StandardError) RunProbeProcessDetailed(ProcessStartInfo startInfo)
    {
        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Unable to start {startInfo.FileName} while probing local service state.");

        var standardOutputTask = process.StandardOutput.ReadToEndAsync();
        var standardErrorTask = process.StandardError.ReadToEndAsync();

        process.WaitForExit();

        return (
            process.ExitCode,
            standardOutputTask.GetAwaiter().GetResult(),
            standardErrorTask.GetAwaiter().GetResult());
    }

    private static string QuoteWindowsArgument(string value)
        => $"\"{value.Replace("\"", "\\\"")}\"";

    private static string QuotePosixArgument(string value)
        => $"'{value.Replace("'", "'\\''")}'";

    private static bool IsWindowsElevationCancelled(Win32Exception exception)
        => exception.NativeErrorCode == 1223;

    [SupportedOSPlatform("windows")]
    private static bool IsRunningAsAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        if (identity is null)
        {
            return false;
        }

        var principal = new WindowsPrincipal(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }

    private static bool TryCreateWarningMessage(Exception exception, out string warningMessage)
    {
        if (exception.Message.Contains("pkexec", StringComparison.OrdinalIgnoreCase)
            && (exception.Message.Contains("not authorized", StringComparison.OrdinalIgnoreCase)
                || exception.Message.Contains("authentication", StringComparison.OrdinalIgnoreCase)
                || exception.Message.Contains("cancel", StringComparison.OrdinalIgnoreCase)
                || exception.Message.Contains("dismiss", StringComparison.OrdinalIgnoreCase)))
        {
            warningMessage = "Root authentication was cancelled or denied. Re-run the action and approve the pkexec prompt if you still want to continue.";
            return true;
        }

        if (exception.Message.Contains("Unable to locate a packaged service executable", StringComparison.OrdinalIgnoreCase))
        {
            warningMessage = exception.Message;
            return true;
        }

        warningMessage = string.Empty;
        return false;
    }

    private static string FormatCommandLine(ProcessStartInfo startInfo)
    {
        if (startInfo.ArgumentList.Count > 0)
        {
            return string.Join(" ", [startInfo.FileName, .. startInfo.ArgumentList]);
        }

        return string.IsNullOrWhiteSpace(startInfo.Arguments)
            ? startInfo.FileName
            : $"{startInfo.FileName} {startInfo.Arguments}";
    }

    private static PlatformNotSupportedException CreateUnsupportedPlatformException()
        => new("Service lifecycle actions are only implemented for Windows services and Linux systemd.");
}