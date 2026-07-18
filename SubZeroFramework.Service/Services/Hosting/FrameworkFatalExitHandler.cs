namespace SubZeroFramework.Service.Services.Hosting;

/// <summary>
/// Terminates the service process with a non-zero exit code after restoring fans to automatic EC control.
/// Needed because since .NET 6 an unhandled <see cref="BackgroundService"/> exception stops the host
/// CLEANLY (exit 0): the Windows SCM and systemd treat that as a normal stop, so the restart-on-failure
/// recovery configured by <c>--service-management install</c> never engages. Terminating the process
/// abruptly with a non-zero code is the documented way to make recovery fire; the fan restore runs first
/// (and again, idempotently, from the ProcessExit hook) so fans are never left on a stale override.
/// </summary>
public sealed class FrameworkFatalExitHandler
{
    public const int FatalExitCode = 1;

    private readonly FrameworkShutdownCoordinator _shutdownCoordinator;
    private readonly IHostApplicationLifetime _applicationLifetime;
    private readonly ILogger<FrameworkFatalExitHandler> _logger;
    private readonly Action<int> _terminateProcess;

    public FrameworkFatalExitHandler(
        FrameworkShutdownCoordinator shutdownCoordinator,
        IHostApplicationLifetime applicationLifetime,
        ILogger<FrameworkFatalExitHandler> logger)
        : this(shutdownCoordinator, applicationLifetime, logger, static exitCode => Environment.Exit(exitCode))
    {
    }

    // Test seam: lets tests observe the terminate call instead of killing the test process.
    internal FrameworkFatalExitHandler(
        FrameworkShutdownCoordinator shutdownCoordinator,
        IHostApplicationLifetime applicationLifetime,
        ILogger<FrameworkFatalExitHandler> logger,
        Action<int> terminateProcess)
    {
        _shutdownCoordinator = shutdownCoordinator;
        _applicationLifetime = applicationLifetime;
        _logger = logger;
        _terminateProcess = terminateProcess;
    }

    /// <summary>
    /// Handles a fault that leaves the service unable to do its job (e.g. a faulted telemetry stream that
    /// permanently stops curve actuation while an EC override may still be applied). During normal shutdown
    /// the fault is only logged — the host is already stopping, and a fatal exit there would turn every
    /// clean stop into an SCM "failure" that pointlessly restarts the service.
    /// </summary>
    public void HandleFatalFault(Exception exception, string context)
    {
        if (_applicationLifetime.ApplicationStopping.IsCancellationRequested)
        {
            _logger.LogDebug(exception, "Ignoring fault in {Context} because the host is already stopping.", context);
            return;
        }

        _logger.LogCritical(
            exception,
            "Fatal fault in {Context}. Restoring fans to automatic control and terminating with exit code {ExitCode} so the configured service recovery restarts the process.",
            context,
            FatalExitCode);

        try
        {
            _shutdownCoordinator.StopTelemetryLoops(context);
        }
        catch (Exception restoreException)
        {
            _logger.LogError(restoreException, "Fan restore failed during fatal-exit handling for {Context}. Exiting anyway.", context);
        }

        _terminateProcess(FatalExitCode);
    }
}
