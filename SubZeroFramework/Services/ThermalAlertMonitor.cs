using System.Reactive.Disposables;
using System.Reactive.Linq;

using DynamicData;

using SubZeroFramework.Services.Units;

namespace SubZeroFramework.Services;

/// <summary>
/// Watches the temperature telemetry stream and raises a desktop notification when the HOTTEST sensor
/// reaches the user-configured warning temperature, honoring the client-only "Thermal alerts" opt-in and
/// threshold from Settings → Startup &amp; alerts. Hysteresis + cooldown keep a reading hovering at the
/// line from spamming notifications. Delivery goes through <see cref="IDesktopNotificationService"/>.
/// </summary>
public sealed class ThermalAlertMonitor : IDisposable
{
    /// <summary>Default warning temperature — matches the thermal page's critical band tint (≥ 85 °C).</summary>
    public const double DefaultThresholdCelsius = 85d;

    /// <summary>User-configurable warning-temperature bounds (canonical Celsius; the Settings slider spans these).</summary>
    public const double MinimumThresholdCelsius = 30d;
    public const double MaximumThresholdCelsius = 130d;

    // Re-arm only after the hottest sensor cools this far below the threshold, so a reading hovering at
    // the line cannot fire again immediately.
    private const double RearmHysteresisCelsius = 5d;
    private static readonly TimeSpan AlertCooldown = TimeSpan.FromMinutes(10);

    private readonly ILocalClientSettingsStore _clientSettings;
    private readonly IUnitFormattingService _unitFormattingService;
    private readonly IDesktopNotificationService _notificationService;
    private readonly ILogger<ThermalAlertMonitor> _logger;
    private readonly ITemperatureTelemetryClient _temperatureTelemetryClient;
    private readonly CompositeDisposable _subscriptions = [];
    private DateTimeOffset _lastAlertAt = DateTimeOffset.MinValue;
    private bool _latched;
    private bool _started;

    public ThermalAlertMonitor(
        ITemperatureTelemetryClient temperatureTelemetryClient,
        ILocalClientSettingsStore clientSettings,
        IUnitFormattingService unitFormattingService,
        IDesktopNotificationService notificationService,
        ILogger<ThermalAlertMonitor> logger)
    {
        ArgumentNullException.ThrowIfNull(temperatureTelemetryClient);
        ArgumentNullException.ThrowIfNull(clientSettings);
        ArgumentNullException.ThrowIfNull(unitFormattingService);
        ArgumentNullException.ThrowIfNull(notificationService);
        ArgumentNullException.ThrowIfNull(logger);

        _temperatureTelemetryClient = temperatureTelemetryClient;
        _clientSettings = clientSettings;
        _unitFormattingService = unitFormattingService;
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

    /// <summary>
    /// Sends a test notification through the exact same path a real thermal alert uses, so the user can
    /// confirm notifications actually reach the screen. Returns false when the platform has no delivery
    /// mechanism or the send failed.
    /// </summary>
    public Task<bool> TrySendTestNotificationAsync()
    {
        _logger.LogInformation("Sending a test thermal-alert notification.");
        return _notificationService.TryShowAsync(
            "Test alert",
            $"Notifications are working. A real alert fires when the hottest sensor reaches {_unitFormattingService.FormatTemperature(ConfiguredThresholdCelsius)}.");
    }

    private void EvaluateSensors(TemperatureTelemetrySnapshot[] sensors)
    {
        // The alert always tracks the HOTTEST sensor against the user-configured warning temperature.
        TemperatureTelemetrySnapshot? hottest = null;
        var hottestCelsius = double.MinValue;
        foreach (var sensor in sensors)
        {
            if (sensor.TemperatureCelsius is double celsius && celsius > hottestCelsius)
            {
                hottestCelsius = celsius;
                hottest = sensor;
            }
        }

        if (hottest is null)
        {
            return;
        }

        var threshold = ConfiguredThresholdCelsius;
        if (hottestCelsius < threshold - RearmHysteresisCelsius)
        {
            _latched = false;
            return;
        }

        if (hottestCelsius < threshold || !_clientSettings.ThermalAlertsEnabled || _latched)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        if (now - _lastAlertAt < AlertCooldown)
        {
            return;
        }

        _lastAlertAt = now;
        _latched = true;
        RaiseAlert(hottest, hottestCelsius);
    }

    // Clamped so a hand-edited settings file cannot push the alert outside the supported band.
    private double ConfiguredThresholdCelsius
        => Math.Clamp(_clientSettings.ThermalAlertThresholdCelsius, MinimumThresholdCelsius, MaximumThresholdCelsius);

    private void RaiseAlert(TemperatureTelemetrySnapshot sensor, double celsius)
    {
        var reading = _unitFormattingService.FormatTemperature(celsius);

        // Platform location (CPU, GPU VRAM, Memory, …) when the sensor's role is identified.
        var location = FrameworkSensorNameDisplay.ToLocation(sensor.SensorName);
        var sensorLabel = location is null ? sensor.DisplayName : $"{sensor.DisplayName} ({location})";

        _logger.LogWarning("Thermal alert: sensor {SensorLabel} reached {Reading} (warning temperature).", sensorLabel, reading);

        // Notification delivery is best-effort; the warning above already reached the log and failures
        // are logged inside the notification service.
        _ = _notificationService.TryShowAsync(
            "Thermal alert",
            $"{sensorLabel} reached {reading} — the hottest sensor crossed your warning temperature {_unitFormattingService.FormatTemperature(ConfiguredThresholdCelsius)}.");
    }
}
