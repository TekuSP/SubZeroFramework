using FrameworkDotnet;
using FrameworkDotnet.Interfaces;
using Hardware.Info;

using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Logging.Configuration;
using Microsoft.Extensions.Logging.EventLog;
using Microsoft.Extensions.Options;

using SubZeroFramework.Models;
using SubZeroFramework.Service.Models;
using SubZeroFramework.Service.Services;
using SubZeroFramework.Services.Compute;
using SubZeroFramework.Services.Linux;
using SubZeroFramework.Service.Services.Hosting;
using SubZeroFramework.Services;

namespace SubZeroFramework.Service;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        var managementExitCode = await FrameworkServiceManagementCli.TryExecuteAsync(args).ConfigureAwait(false);
        if (managementExitCode.HasValue)
        {
            return managementExitCode.Value;
        }

        var builder = WebApplication.CreateBuilder(args);
        var socketPath = FrameworkGrpcSocketPath.GetPath();
        var persistentConfigurationPath = FrameworkServiceConfigurationPaths.GetPersistentConfigurationPath();

        builder.Configuration.AddJsonFile(persistentConfigurationPath, optional: true, reloadOnChange: true);

        builder.Services.AddWindowsService(options =>
        {
            options.ServiceName = "SubZeroFrameworkService";
        });
        builder.Services.AddSystemd();

        builder.Services.Configure<HostOptions>(options =>
        {
            // Fan restore-to-auto on stop is a handful of EC writes (sub-second), but leave generous
            // headroom for a contended EC. Matches the systemd unit's TimeoutStopSec=90; on Windows the
            // service lifetime requests the same additional stop time from the SCM (default is only 30 s).
            options.ShutdownTimeout = TimeSpan.FromSeconds(90);
        });

        if (OperatingSystem.IsWindows())
        {
            LoggerProviderOptions.RegisterProviderOptions<EventLogSettings, EventLogLoggerProvider>(builder.Services);
        }

        builder.WebHost.ConfigureKestrel(serverOptions =>
        {
            FrameworkGrpcSocketSecurity.PrepareServerSocketPath(socketPath);

            serverOptions.ListenUnixSocket(socketPath, listenOptions =>
            {
                listenOptions.Protocols = HttpProtocols.Http2;
            });
        });

        builder.Services
            .AddOptions<FrameworkServiceOptions>()
            .Bind(builder.Configuration.GetSection("FrameworkService"));

        builder.Services.AddGrpc();

        // Mirrors the service's own log into a bounded in-memory buffer so the app can show it (Settings →
        // Service logs) without reading back the Event Log or journald. Added ALONGSIDE the platform sinks,
        // never instead of them. Registered as a singleton first so the provider and the gRPC handler share
        // the one buffer.
        builder.Services.AddSingleton<InMemoryServiceLogBuffer>();
        builder.Services.AddSingleton<ILoggerProvider>(x => new InMemoryServiceLogProvider(x.GetRequiredService<InMemoryServiceLogBuffer>()));
        builder.Services.AddSingleton<HardwareInfoNoiseFilteringLogger>(x =>
            new HardwareInfoNoiseFilteringLogger(x.GetRequiredService<ILogger<HardwareInfo>>()));
        builder.Services.AddSingleton<IHardwareInfoLogNoiseBuffer>(x => x.GetRequiredService<HardwareInfoNoiseFilteringLogger>());
        builder.Services.AddSingleton<IHardwareInfo, HardwareInfo>(x =>
            new HardwareInfo(logger: x.GetRequiredService<HardwareInfoNoiseFilteringLogger>()));
        // GPU/NPU utilization. Optional by design: a TFM with no reader registers the null-object pair, the
        // provider publishes no compute channels, and the UI simply shows no devices. Compile-time (#if)
        // rather than a runtime OS check so the Linux build carries neither the readers nor their interop —
        // the Windows publish profiles build the windows TFM, the Linux ones build net10.0.
#if WINDOWS10_0_26100_0_OR_GREATER
        builder.Services.AddSingleton<IComputeDeviceIdentityResolver, WindowsComputeDeviceIdentityResolver>();
        builder.Services.AddSingleton<IComputeUtilizationReader, WindowsPdhComputeUtilizationReader>();
        builder.Services.AddSingleton<IGraphicsInventoryReader>(UnavailableGraphicsInventoryReader.Instance);
#else
        // Linux has no single counter set covering every vendor the way Windows' GPU Engine does, so each
        // source is its own reader and a composite merges them: a Framework 16 with the graphics module
        // fitted runs two at once. Each is independently optional — the composite drops one that fails and
        // keeps publishing the rest. Unlike the Windows readers these are gated at REGISTRATION rather than
        // by #if, because they are ordinary file I/O over an injectable root (which is what makes them
        // testable off Linux) and net10.0 is shared with the desktop app head.
        builder.Services.AddSingleton<IComputeDeviceIdentityResolver>(UnavailableComputeDeviceIdentityResolver.Instance);

        if (OperatingSystem.IsLinux())
        {
            builder.Services.AddSingleton<IComputeUtilizationReader>(x => new CompositeComputeUtilizationReader(
                [
                    new LinuxAmdGpuUtilizationReader(x.GetRequiredService<ILogger<LinuxAmdGpuUtilizationReader>>()),
                    new LinuxNvmlGpuUtilizationReader(x.GetRequiredService<ILogger<LinuxNvmlGpuUtilizationReader>>()),
                    new LinuxIntelGpuUtilizationReader(x.GetRequiredService<ILogger<LinuxIntelGpuUtilizationReader>>()),
                ],
                x.GetRequiredService<ILogger<CompositeComputeUtilizationReader>>()));

            // Replaces Hardware.Info's xrandr-based enumeration, which cannot work without a display server.
            builder.Services.AddSingleton<IGraphicsInventoryReader, LinuxDrmGraphicsInventoryReader>();
        }
        else
        {
            builder.Services.AddSingleton<IComputeUtilizationReader>(UnavailableComputeUtilizationReader.Instance);
            builder.Services.AddSingleton<IGraphicsInventoryReader>(UnavailableGraphicsInventoryReader.Instance);
        }
#endif

        builder.Services.AddSingleton<IFrameworkSystem, FrameworkSystem>();
        builder.Services.AddSingleton<FrameworkFanControlSafetyTracker>();
        builder.Services.AddSingleton<IFrameworkDataProvider, FrameworkDataProvider>();
        builder.Services.AddSingleton<FrameworkShutdownCoordinator>();
        builder.Services.AddSingleton<FrameworkFatalExitHandler>();
        builder.Services.AddSingleton<FrameworkFanControlStateStore>();
        builder.Services.AddSingleton<FanPreviewWatchdog>();
        builder.Services.AddSingleton<FrameworkFanControlAuthorizationService>();
        builder.Services.AddSingleton<FrameworkServiceConfigurationStore>();
        builder.Services.AddSingleton<FrameworkServiceConfigurationManager>();
        builder.Services.AddHostedService(static services => services.GetRequiredService<FrameworkShutdownCoordinator>());
        builder.Services.AddHostedService<FrameworkTelemetryWorker>();
        // Registered after the telemetry worker so it stops first (LIFO) on shutdown, ceasing EC writes
        // before the restore-to-auto path runs. Actuates stored custom curves against live temperatures.
        builder.Services.AddHostedService<FrameworkFanCurveControlWorker>();

        var app = builder.Build();
        var serviceOptions = app.Services.GetRequiredService<IOptionsMonitor<FrameworkServiceOptions>>().CurrentValue;

        app.Logger.LogInformation("Starting SubZeroFramework service on socket {SocketPath}. FanControlCommandsEnabled={FanControlCommandsEnabled}.", socketPath, serviceOptions.AllowFanControlCommands);
        app.MapGrpcService<FrameworkStatusGrpcService>();
        app.MapGrpcService<FrameworkServiceConfigurationGrpcService>();
        app.MapGrpcService<FrameworkTelemetryGrpcService>();
        app.MapGrpcService<HardwareInfoGrpcService>();
        app.MapGrpcService<FrameworkFanControlGrpcService>();
        app.Logger.LogInformation("Mapped gRPC services for status, service configuration, telemetry, hardware info, and fan control.");

        // Kestrel creates the socket file during bind, inheriting this process's umask — under systemd
        // that lands at 0755 root:root, and connect(2) needs WRITE permission, so the unprivileged app
        // could never reach its own service. ApplicationStarted fires after the listener is bound, which
        // is the earliest point the file exists to be chmod'd.
        app.Lifetime.ApplicationStarted.Register(() =>
        {
            FrameworkGrpcSocketSecurity.AllowLocalClientsToConnect(socketPath);

            if (OperatingSystem.IsLinux())
            {
                app.Logger.LogInformation(
                    "Local gRPC socket {SocketPath} is open to unprivileged local clients; the containing directory stays restricted.",
                    socketPath);
            }
        });

        // Resolved BEFORE the host runs, and deliberately not inside the catch below: by the time RunAsync
        // throws, the host has already disposed its service provider, so resolving anything from it there
        // throws ObjectDisposedException. That exception would replace the crash handler entirely — the fans
        // would never be restored, the real cause would never be logged, and the process would die as an
        // unhandled exception instead of exiting with FatalExitCode, which is precisely the signal
        // systemd/SCM restart-on-failure keys off. The coordinator is a singleton the host constructs anyway
        // (it is also registered as a hosted service), so holding it here costs nothing.
        var shutdownCoordinator = app.Services.GetRequiredService<FrameworkShutdownCoordinator>();

        try
        {
            await app.RunAsync().ConfigureAwait(false);
            return 0;
        }
        catch (Exception exception)
        {
            // A crashed host must exit NON-ZERO so the SCM/systemd restart-on-failure recovery engages
            // (a clean exit 0 reads as a normal stop and is never restarted). Restore fans first —
            // StopTelemetryLoops is idempotent with the ProcessExit hook, so double handling is safe, and it
            // already tolerates a provider that disposal has beaten it to.
            app.Logger.LogCritical(exception, "SubZeroFramework service host crashed.");
            shutdownCoordinator.StopTelemetryLoops("Program.Main host crash");
            return FrameworkFatalExitHandler.FatalExitCode;
        }
        finally
        {
            app.Logger.LogInformation("SubZeroFramework service has stopped.");
        }
    }
}
