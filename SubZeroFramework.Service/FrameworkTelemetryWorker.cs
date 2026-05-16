using Microsoft.Extensions.Options;

using SubZeroFramework.Service.Models;
using SubZeroFramework.Services;

namespace SubZeroFramework.Service;

public sealed class FrameworkTelemetryWorker : BackgroundService
{
    private readonly IFrameworkDataProvider _frameworkDataProvider;
    private readonly FrameworkServiceOptions _options;
    private readonly ILogger<FrameworkTelemetryWorker> _logger;
    private readonly IHostApplicationLifetime _applicationLifetime;

    public FrameworkTelemetryWorker(
        IFrameworkDataProvider frameworkDataProvider,
        IOptions<FrameworkServiceOptions> options,
        ILogger<FrameworkTelemetryWorker> logger,
        IHostApplicationLifetime applicationLifetime)
    {
        _frameworkDataProvider = frameworkDataProvider;
        _options = options.Value;
        _logger = logger;
        _applicationLifetime = applicationLifetime;
    }

    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!_frameworkDataProvider.SetPolling(_options.PollingInterval))
        {
            _logger.LogWarning("Unable to configure the Framework polling interval.");
        }

        if (!_frameworkDataProvider.SetHardwareInfoPolling(_options.HardwareInfoPollingInterval))
        {
            _logger.LogWarning("Unable to configure the HardwareInfo polling interval.");
        }

        var status = await _frameworkDataProvider.RefreshAsync(cancellationToken).ConfigureAwait(false);
        if (status.RequiresElevation && OperatingSystem.IsLinux())
        {
            _logger.LogCritical("Framework EC access requires the Linux service process to run as root. Configure the systemd unit to run with root privileges.");
        }

        if (!_frameworkDataProvider.StartPolling())
        {
            _logger.LogWarning("Unable to start the Framework polling loop.");
        }

        if (!_frameworkDataProvider.StartHardwareInfoPolling())
        {
            _logger.LogWarning("Unable to start the HardwareInfo polling loop.");
        }

        await base.StartAsync(cancellationToken).ConfigureAwait(false);
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Framework telemetry service is active.");
        return Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken);
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Stopping Framework telemetry service.");

        try
        {
            _frameworkDataProvider.StopPolling();
            _frameworkDataProvider.StopHardwareInfoPolling();
        }
        catch (ObjectDisposedException)
        {
        }

        await base.StopAsync(cancellationToken).ConfigureAwait(false);
    }
}
