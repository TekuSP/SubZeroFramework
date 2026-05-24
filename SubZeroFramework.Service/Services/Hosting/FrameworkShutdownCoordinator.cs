using SubZeroFramework.Services;

namespace SubZeroFramework.Service.Services.Hosting;

public sealed class FrameworkShutdownCoordinator : IHostedService, IDisposable
{
    private readonly IFrameworkDataProvider _frameworkDataProvider;
    private readonly ILogger<FrameworkShutdownCoordinator> _logger;
    private int _shutdownRequested;
    private int _eventHooksRegistered;
    private bool _disposed;

    public FrameworkShutdownCoordinator(IFrameworkDataProvider frameworkDataProvider, ILogger<FrameworkShutdownCoordinator> logger)
    {
        _frameworkDataProvider = frameworkDataProvider;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        RegisterEventHooks();
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        UnregisterEventHooks();
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        UnregisterEventHooks();
        _disposed = true;
    }

    public void StopTelemetryLoops(string trigger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(trigger);

        if (Interlocked.Exchange(ref _shutdownRequested, 1) != 0)
        {
            _logger.LogDebug("Ignoring duplicate shutdown handling request from {Trigger} because telemetry shutdown has already been requested.", trigger);
            return;
        }

        _logger.LogInformation("{Trigger} requested telemetry shutdown so automatic fan control can be restored before exit.", trigger);

        try
        {
            var frameworkPollingStopped = _frameworkDataProvider.StopPolling();
            var hardwareInfoPollingStopped = _frameworkDataProvider.StopHardwareInfoPolling();

            _logger.LogInformation("Telemetry shutdown finished for {Trigger}. FrameworkPollingStopped={FrameworkPollingStopped}, HardwareInfoPollingStopped={HardwareInfoPollingStopped}.", trigger, frameworkPollingStopped, hardwareInfoPollingStopped);
        }
        catch (ObjectDisposedException)
        {
            _logger.LogDebug("Ignoring shutdown handling request from {Trigger} because the framework data provider has already been disposed.", trigger);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Telemetry shutdown failed while handling {Trigger}.", trigger);
        }
    }

    public void HandleProcessExit()
    {
        _logger.LogInformation("AppDomain.ProcessExit detected.");
        StopTelemetryLoops("AppDomain.ProcessExit");
    }

    public void HandleUnhandledException(object? exceptionObject, bool isTerminating)
    {
        if (exceptionObject is Exception exception)
        {
            _logger.LogCritical(exception, "AppDomain.UnhandledException detected. IsTerminating={IsTerminating}.", isTerminating);
        }
        else
        {
            _logger.LogCritical("AppDomain.UnhandledException detected with a non-Exception payload. PayloadType={PayloadType}, IsTerminating={IsTerminating}.", exceptionObject?.GetType().FullName ?? "null", isTerminating);
        }

        StopTelemetryLoops("AppDomain.UnhandledException");
    }

    public void HandleUnobservedTaskException(UnobservedTaskExceptionEventArgs eventArgs)
    {
        ArgumentNullException.ThrowIfNull(eventArgs);

        _logger.LogError(eventArgs.Exception, "TaskScheduler.UnobservedTaskException detected.");
        StopTelemetryLoops("TaskScheduler.UnobservedTaskException");
        eventArgs.SetObserved();
    }

    private void RegisterEventHooks()
    {
        if (Interlocked.Exchange(ref _eventHooksRegistered, 1) != 0)
        {
            return;
        }

        AppDomain.CurrentDomain.ProcessExit += OnProcessExit;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        _logger.LogInformation("Registered runtime shutdown hooks for ProcessExit, UnhandledException, and UnobservedTaskException.");
    }

    private void UnregisterEventHooks()
    {
        if (Interlocked.Exchange(ref _eventHooksRegistered, 0) == 0)
        {
            return;
        }

        AppDomain.CurrentDomain.ProcessExit -= OnProcessExit;
        AppDomain.CurrentDomain.UnhandledException -= OnUnhandledException;
        TaskScheduler.UnobservedTaskException -= OnUnobservedTaskException;

        _logger.LogDebug("Unregistered runtime shutdown hooks.");
    }

    private void OnProcessExit(object? sender, EventArgs eventArgs)
    {
        HandleProcessExit();
    }

    private void OnUnhandledException(object sender, UnhandledExceptionEventArgs eventArgs)
    {
        HandleUnhandledException(eventArgs.ExceptionObject, eventArgs.IsTerminating);
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs eventArgs)
    {
        HandleUnobservedTaskException(eventArgs);
    }
}