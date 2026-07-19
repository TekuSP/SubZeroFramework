using System.Reactive.Disposables;
using System.Reactive.Linq;

namespace SubZeroFramework.Services;

/// <summary>
/// Watches the service status stream and raises a status notification when the gRPC connection to the
/// background service is unexpectedly lost or comes back — covering crashes, SCM/systemd restarts, and
/// anything else that kills the service outside a user-initiated action. The first observed state is
/// treated as the baseline (no notification for the app simply starting up while the service is down).
/// Delivery honors the "Status notifications" opt-in via
/// <see cref="IDesktopNotificationService.TryShowStatusAsync"/>.
/// </summary>
public sealed class ServiceHealthNotifier : IDisposable
{
    // Debounces flapping during a restart: a state must hold this long before it is reported.
    private static readonly TimeSpan StateHoldTime = TimeSpan.FromSeconds(5);

    private readonly IFrameworkStatusClient _statusClient;
    private readonly IDesktopNotificationService _notificationService;
    private readonly ILogger<ServiceHealthNotifier> _logger;
    private readonly CompositeDisposable _subscriptions = [];
    private bool? _lastReportedReachable;
    private bool _started;

    public ServiceHealthNotifier(
        IFrameworkStatusClient statusClient,
        IDesktopNotificationService notificationService,
        ILogger<ServiceHealthNotifier> logger)
    {
        ArgumentNullException.ThrowIfNull(statusClient);
        ArgumentNullException.ThrowIfNull(notificationService);
        ArgumentNullException.ThrowIfNull(logger);

        _statusClient = statusClient;
        _notificationService = notificationService;
        _logger = logger;
    }

    public void Start()
    {
        if (_started)
        {
            return;
        }

        _started = true;

        _subscriptions.Add(_statusClient
            .WatchStatus()
            .Select(status => status.IsGrpcActive)
            .DistinctUntilChanged()
            .Throttle(StateHoldTime)
            .Subscribe(EvaluateReachability));
    }

    public void Dispose()
    {
        _subscriptions.Dispose();
    }

    private void EvaluateReachability(bool reachable)
    {
        if (_lastReportedReachable is not bool previous)
        {
            // Baseline: the state the app started in is not an event.
            _lastReportedReachable = reachable;
            return;
        }

        if (previous == reachable)
        {
            return;
        }

        _lastReportedReachable = reachable;
        _logger.LogInformation("Service reachability changed. Reachable={Reachable}.", reachable);

        _ = _notificationService.TryShowStatusAsync(
            reachable ? "Service reconnected" : "Service connection lost",
            reachable
                ? "The background service is reachable again."
                : "The background service stopped responding. Fan control and telemetry are paused until it returns.");
    }
}
