using System.Reactive.Disposables;
using System.Reactive.Linq;

using DynamicData;

using SubZeroFramework.Services.Units;

namespace SubZeroFramework.Services;

/// <summary>
/// Watches the temperature telemetry stream and raises a Windows notification when a sensor crosses the
/// critical band, honoring the client-only "Thermal alerts" opt-in from Settings → Startup &amp; alerts.
/// Per-sensor hysteresis + cooldown keep a sensor hovering at the threshold from spamming notifications.
/// </summary>
/// <remarks>
/// DISABLED for the MVP (2026-07-19, user decision): the monitor is not started and the Settings toggle is
/// inert. Windows toast delivery via <c>AppNotificationManager</c> is silently dropped for this
/// self-contained unpackaged app — <c>Register()</c> and <c>Show()</c> both succeed, but the shell never
/// accepts the notification (no Action Center entry, and Windows never creates the per-app entry under
/// <c>HKCU\...\CurrentVersion\Notifications\Settings</c> that first delivery produces). Linux has no
/// implementation at all. Future direction when this is revived:
///   - Linux: post straight to the session bus — <c>org.freedesktop.Notifications</c> (the <c>Notify</c>
///     method) is a tiny D-Bus call that every desktop environment implements; no toolkit dependency.
///   - Windows: either resolve the WinAppSDK self-contained delivery, or fall back to legacy
///     <c>Windows.UI.Notifications.ToastNotificationManager.CreateToastNotifier(aumid)</c> with our own
///     AppUserModelId registration (a working prototype exists in this file's git history, 2026-07-18).
/// </remarks>
public sealed class ThermalAlertMonitor : IDisposable
{
    // Mirrors the critical band of the thermal page (ThermalSensorModel tints values >= 85 °C critical);
    // re-arm only after the sensor cools below the lower bound.
    private const double CriticalThresholdCelsius = 85d;
    private const double RearmThresholdCelsius = 80d;
    private static readonly TimeSpan AlertCooldown = TimeSpan.FromMinutes(10);

    private readonly ILocalClientSettingsStore _clientSettings;
    private readonly IUnitFormattingService _unitFormattingService;
    private readonly ILogger<ThermalAlertMonitor> _logger;
    private readonly ITemperatureTelemetryClient _temperatureTelemetryClient;
    private readonly Dictionary<int, DateTimeOffset> _lastAlertAt = [];
    private readonly HashSet<int> _latchedSensors = [];
    private readonly CompositeDisposable _subscriptions = [];
    private bool _started;

    public ThermalAlertMonitor(
        ITemperatureTelemetryClient temperatureTelemetryClient,
        ILocalClientSettingsStore clientSettings,
        IUnitFormattingService unitFormattingService,
        ILogger<ThermalAlertMonitor> logger)
    {
        ArgumentNullException.ThrowIfNull(temperatureTelemetryClient);
        ArgumentNullException.ThrowIfNull(clientSettings);
        ArgumentNullException.ThrowIfNull(unitFormattingService);
        ArgumentNullException.ThrowIfNull(logger);

        _temperatureTelemetryClient = temperatureTelemetryClient;
        _clientSettings = clientSettings;
        _unitFormattingService = unitFormattingService;
        _logger = logger;
    }

    public void Start()
    {
        if (_started)
        {
            return;
        }

        _started = true;
        TryRegisterNotificationChannel();

        _subscriptions.Add(_temperatureTelemetryClient
            .WatchTemperatures()
            .QueryWhenChanged(query => query.Items.ToArray())
            .Sample(TimeSpan.FromSeconds(5))
            .Subscribe(EvaluateSensors));
    }

    public void Dispose()
    {
        _subscriptions.Dispose();
    }

    private void EvaluateSensors(TemperatureTelemetrySnapshot[] sensors)
    {
        foreach (var sensor in sensors)
        {
            if (sensor.TemperatureCelsius is not double celsius)
            {
                continue;
            }

            if (celsius < RearmThresholdCelsius)
            {
                _latchedSensors.Remove(sensor.SensorIndex);
                continue;
            }

            if (celsius < CriticalThresholdCelsius
                || !_clientSettings.ThermalAlertsEnabled
                || _latchedSensors.Contains(sensor.SensorIndex))
            {
                continue;
            }

            var now = DateTimeOffset.UtcNow;
            if (_lastAlertAt.TryGetValue(sensor.SensorIndex, out var lastAlert) && now - lastAlert < AlertCooldown)
            {
                continue;
            }

            _lastAlertAt[sensor.SensorIndex] = now;
            _latchedSensors.Add(sensor.SensorIndex);
            RaiseAlert(sensor.DisplayName, celsius);
        }
    }

    private void RaiseAlert(string sensorName, double celsius)
    {
        var reading = _unitFormattingService.FormatTemperature(celsius);
        _logger.LogWarning("Thermal alert: sensor {SensorName} reached {Reading} (critical).", sensorName, reading);

        // Notification delivery is best-effort; the warning above already reached the log.
        TryShowToast("Thermal alert", $"{sensorName} reached {reading} — the sensor crossed the critical band.");
    }

    private bool TryShowToast(string title, string body)
    {
#if WINDOWS10_0_26100_0_OR_GREATER
        try
        {
            if (!Microsoft.Windows.AppNotifications.AppNotificationManager.IsSupported())
            {
                return false;
            }

            var notification = new Microsoft.Windows.AppNotifications.Builder.AppNotificationBuilder()
                .AddText(title)
                .AddText(body)
                .BuildNotification();

            Microsoft.Windows.AppNotifications.AppNotificationManager.Default.Show(notification);
            return true;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Failed to show the {Title} notification.", title);
            return false;
        }
#else
        // Desktop (Skia) target: log-only for MVP.
        return false;
#endif
    }

    private void TryRegisterNotificationChannel()
    {
#if WINDOWS10_0_26100_0_OR_GREATER
        try
        {
            if (Microsoft.Windows.AppNotifications.AppNotificationManager.IsSupported())
            {
                // Required once per process for unpackaged apps before any Show call.
                Microsoft.Windows.AppNotifications.AppNotificationManager.Default.Register();
            }
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "App notification registration failed; thermal alerts will only reach the log.");
        }
#endif
    }
}
