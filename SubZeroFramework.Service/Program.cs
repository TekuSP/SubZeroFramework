using FrameworkDotnet;
using FrameworkDotnet.Interfaces;
using Hardware.Info;

using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Logging.Configuration;
using Microsoft.Extensions.Logging.EventLog;
using Microsoft.Extensions.Options;

using SubZeroFramework.Service.Models;
using SubZeroFramework.Service.Services;
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
        builder.Services.AddSingleton<HardwareInfoNoiseFilteringLogger>(x =>
            new HardwareInfoNoiseFilteringLogger(x.GetRequiredService<ILogger<HardwareInfo>>()));
        builder.Services.AddSingleton<IHardwareInfoLogNoiseBuffer>(x => x.GetRequiredService<HardwareInfoNoiseFilteringLogger>());
        builder.Services.AddSingleton<IHardwareInfo, HardwareInfo>(x =>
            new HardwareInfo(logger: x.GetRequiredService<HardwareInfoNoiseFilteringLogger>()));
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

        try
        {
            await app.RunAsync().ConfigureAwait(false);
            return 0;
        }
        catch (Exception exception)
        {
            // A crashed host must exit NON-ZERO so the SCM/systemd restart-on-failure recovery engages
            // (a clean exit 0 reads as a normal stop and is never restarted). Restore fans first —
            // StopTelemetryLoops is idempotent with the ProcessExit hook, so double handling is safe.
            app.Logger.LogCritical(exception, "SubZeroFramework service host crashed.");
            app.Services.GetRequiredService<FrameworkShutdownCoordinator>().StopTelemetryLoops("Program.Main host crash");
            return FrameworkFatalExitHandler.FatalExitCode;
        }
        finally
        {
            app.Logger.LogInformation("SubZeroFramework service has stopped.");
        }
    }
}
