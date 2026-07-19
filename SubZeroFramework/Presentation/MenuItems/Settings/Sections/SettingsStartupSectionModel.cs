using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Microsoft.UI.Xaml;

using SubZeroFramework.Services;
using SubZeroFramework.Services.Units;

namespace SubZeroFramework.Presentation.MenuItems.Settings.Sections;

/// <summary>
/// ViewModel for the Startup &amp; alerts section. Navigation constructs it (ViewMap-registered) each time
/// the section shows; all state initializes synchronously in the constructor so the compiled bindings see
/// final values the moment the DataContext arrives, on the UI thread.
///
/// Edits are STAGED: toggles and the warning-temperature slider only mutate view-model state, and nothing
/// touches the settings store, the launch-at-sign-in registration, or the service until the user presses
/// Save (Cancel re-reads the actual state). This also makes the model immune to spurious slider writebacks:
/// a coerced value can never silently persist.
/// </summary>
public partial class SettingsStartupSectionModel : ObservableObject
{
    private readonly IFrameworkServiceControlClient _serviceControlClient;
    private readonly ILocalClientSettingsStore _clientSettings;
    private readonly IStartupRegistrationService _startupRegistration;
    private readonly ThermalAlertMonitor _thermalAlertMonitor;
    private readonly IUnitFormattingService _unitFormattingService;

    // Tolerance for the display↔canonical Celsius round trip: slider steps in every offered unit move the
    // canonical value by at least ~0.55 °C, so smaller deltas are conversion jitter, not edits.
    private const double ThresholdToleranceCelsius = 0.25d;

    private bool _suppressStagedCallbacks;

    // Guards the display-unit slider round trip (display value ↔ canonical Celsius) against feedback loops.
    private bool _suppressThresholdSync;

    // Staged canonical warning temperature; the slider surface renders and edits the user's display unit.
    private double _thresholdCelsius;

    // Last actual (persisted/applied) state — the baseline for dirty tracking and Cancel.
    private bool _savedStartWithSystemBoot;
    private bool? _savedAutorunEnabled;
    private bool _savedThermalAlertsEnabled;
    private bool _savedStatusNotificationsEnabled;
    private double _savedThresholdCelsius;

    public SettingsStartupSectionModel(
        IFrameworkServiceControlClient serviceControlClient,
        ILocalClientSettingsStore clientSettings,
        IStartupRegistrationService startupRegistration,
        ThermalAlertMonitor thermalAlertMonitor,
        IUnitFormattingService unitFormattingService)
    {
        ArgumentNullException.ThrowIfNull(serviceControlClient);
        ArgumentNullException.ThrowIfNull(clientSettings);
        ArgumentNullException.ThrowIfNull(startupRegistration);
        ArgumentNullException.ThrowIfNull(thermalAlertMonitor);
        ArgumentNullException.ThrowIfNull(unitFormattingService);

        _serviceControlClient = serviceControlClient;
        _clientSettings = clientSettings;
        _startupRegistration = startupRegistration;
        _thermalAlertMonitor = thermalAlertMonitor;
        _unitFormattingService = unitFormattingService;

        ReloadFromActualState();
    }

    [ObservableProperty]
    public partial bool StartWithSystemBoot { get; set; }

    [ObservableProperty]
    public partial bool StartWithSystemBootSupported { get; set; }

    [ObservableProperty]
    public partial bool ThermalAlertsEnabled { get; set; }

    /// <summary>
    /// The staged warning temperature in the user's chosen unit (Settings → Display units), e.g. "85 °C"
    /// or "185 °F". Recomputed (not a live getter) so a unit-preference change is picked up on refresh.
    /// </summary>
    [ObservableProperty]
    public partial string ThermalAlertThresholdDisplay { get; set; } = string.Empty;

    // ----- Unit-aware slider surface: the slider binds these so its whole scale (not just the value
    // label) renders in the user's chosen temperature unit; writes convert back to canonical Celsius. -----

    /// <summary>The staged warning temperature in the user's chosen unit (TwoWay slider value).</summary>
    [ObservableProperty]
    public partial double ThermalAlertThresholdDisplayValue { get; set; }

    /// <summary><see cref="ThermalAlertMonitor.MinimumThresholdCelsius"/> in the user's chosen unit (slider minimum).</summary>
    [ObservableProperty]
    public partial double ThermalAlertThresholdDisplayMinimum { get; set; }

    /// <summary><see cref="ThermalAlertMonitor.MaximumThresholdCelsius"/> in the user's chosen unit (slider maximum).</summary>
    [ObservableProperty]
    public partial double ThermalAlertThresholdDisplayMaximum { get; set; }

    /// <summary>Opt-in for service/fan-control status notifications (restart, install, curve applied, connection lost, …).</summary>
    [ObservableProperty]
    public partial bool StatusNotificationsEnabled { get; set; }

    [ObservableProperty]
    public partial bool AutorunIsOn { get; set; }

    [ObservableProperty]
    public partial bool CanToggleAutorun { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StartupStatusVisibility))]
    public partial string StartupStatusMessage { get; set; } = string.Empty;

    public Visibility StartupStatusVisibility => string.IsNullOrEmpty(StartupStatusMessage) ? Visibility.Collapsed : Visibility.Visible;

    /// <summary>True while any staged edit differs from the last saved/applied state (enables Save/Cancel).</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelCommand))]
    [NotifyPropertyChangedFor(nameof(UnsavedChangesVisibility))]
    [NotifyPropertyChangedFor(nameof(SavedVisibility))]
    public partial bool HasUnsavedChanges { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelCommand))]
    public partial bool IsSaving { get; set; }

    public Visibility UnsavedChangesVisibility => HasUnsavedChanges ? Visibility.Visible : Visibility.Collapsed;

    public Visibility SavedVisibility => HasUnsavedChanges ? Visibility.Collapsed : Visibility.Visible;

    partial void OnStartWithSystemBootChanged(bool value) => OnStagedEditChanged();

    partial void OnThermalAlertsEnabledChanged(bool value) => OnStagedEditChanged();

    partial void OnStatusNotificationsEnabledChanged(bool value) => OnStagedEditChanged();

    partial void OnAutorunIsOnChanged(bool value) => OnStagedEditChanged();

    private void OnStagedEditChanged()
    {
        if (!_suppressStagedCallbacks)
        {
            UpdateDirtyState();
        }
    }

    partial void OnThermalAlertThresholdDisplayValueChanged(double value)
    {
        if (_suppressThresholdSync)
        {
            return;
        }

        // The slider edits the display unit; staged canonical state stays Celsius. The tolerance swallows
        // the rounding introduced by the display→Celsius→display round trip.
        var celsius = Math.Clamp(
            _unitFormattingService.ConvertTemperatureToCelsius(value),
            ThermalAlertMonitor.MinimumThresholdCelsius,
            ThermalAlertMonitor.MaximumThresholdCelsius);
        if (Math.Abs(celsius - _thresholdCelsius) < ThresholdToleranceCelsius)
        {
            return;
        }

        _thresholdCelsius = celsius;
        ThermalAlertThresholdDisplay = _unitFormattingService.FormatTemperature(celsius);
        UpdateDirtyState();
    }

    private void UpdateDirtyState()
        => HasUnsavedChanges =
            StartWithSystemBoot != _savedStartWithSystemBoot
            || (_savedAutorunEnabled is bool savedAutorun && CanToggleAutorun && AutorunIsOn != savedAutorun)
            || ThermalAlertsEnabled != _savedThermalAlertsEnabled
            || StatusNotificationsEnabled != _savedStatusNotificationsEnabled
            || Math.Abs(_thresholdCelsius - _savedThresholdCelsius) >= ThresholdToleranceCelsius;

    private bool CanSaveOrCancel() => HasUnsavedChanges && !IsSaving;

    /// <summary>Applies every staged edit: registration, service autorun, and the client settings store.</summary>
    [RelayCommand(CanExecute = nameof(CanSaveOrCancel))]
    private async Task SaveAsync()
    {
        IsSaving = true;
        var failures = new List<string>();

        try
        {
            if (StartWithSystemBoot != _savedStartWithSystemBoot
                && !_startupRegistration.TrySetEnabled(StartWithSystemBoot))
            {
                failures.Add("Updating the launch-at-sign-in registration failed.");
            }

            if (_savedAutorunEnabled is bool savedAutorun && CanToggleAutorun && AutorunIsOn != savedAutorun)
            {
                // Command runs on the UI thread; without ConfigureAwait(false) the continuation returns
                // there, so every property write below is UI-thread-safe.
                var result = await (AutorunIsOn
                    ? _serviceControlClient.EnableAutorunAsync(CancellationToken.None)
                    : _serviceControlClient.DisableAutorunAsync(CancellationToken.None));
                if (result.Kind != FrameworkServiceCommandResultKind.Success)
                {
                    failures.Add(result.Message);
                }
            }

            _clientSettings.ThermalAlertsEnabled = ThermalAlertsEnabled;
            _clientSettings.StatusNotificationsEnabled = StatusNotificationsEnabled;
            _clientSettings.ThermalAlertThresholdCelsius = _thresholdCelsius;

            StartupStatusMessage = failures.Count == 0 ? "Settings saved." : string.Join(" ", failures);
        }
        finally
        {
            // Re-baseline from what actually took effect; anything that failed to apply stays dirty.
            ReloadFromActualState();
            IsSaving = false;
        }
    }

    /// <summary>Discards every staged edit and re-reads the actual persisted/applied state.</summary>
    [RelayCommand(CanExecute = nameof(CanSaveOrCancel))]
    private void Cancel()
    {
        ReloadFromActualState();
        StartupStatusMessage = string.Empty;
    }

    private void ReloadFromActualState()
    {
        _suppressStagedCallbacks = true;
        StartWithSystemBootSupported = _startupRegistration.IsSupported;
        StartWithSystemBoot = _savedStartWithSystemBoot = _startupRegistration.IsEnabled();
        ThermalAlertsEnabled = _savedThermalAlertsEnabled = _clientSettings.ThermalAlertsEnabled;
        StatusNotificationsEnabled = _savedStatusNotificationsEnabled = _clientSettings.StatusNotificationsEnabled;
        _thresholdCelsius = _savedThresholdCelsius = Math.Clamp(
            _clientSettings.ThermalAlertThresholdCelsius,
            ThermalAlertMonitor.MinimumThresholdCelsius,
            ThermalAlertMonitor.MaximumThresholdCelsius);

        var serviceInfo = _serviceControlClient.GetInfo();
        _savedAutorunEnabled = serviceInfo.IsAutorunEnabled;
        CanToggleAutorun = serviceInfo.IsSupported && serviceInfo.IsInstalled && serviceInfo.IsAutorunEnabled.HasValue;
        if (serviceInfo.IsAutorunEnabled is bool autorunEnabled)
        {
            AutorunIsOn = autorunEnabled;
        }

        _suppressStagedCallbacks = false;

        RefreshThresholdDisplay();
        UpdateDirtyState();
    }

    private void RefreshThresholdDisplay()
    {
        ThermalAlertThresholdDisplay = _unitFormattingService.FormatTemperature(_thresholdCelsius);

        _suppressThresholdSync = true;
        try
        {
            ThermalAlertThresholdDisplayMinimum = _unitFormattingService.ConvertTemperature(ThermalAlertMonitor.MinimumThresholdCelsius);
            ThermalAlertThresholdDisplayMaximum = _unitFormattingService.ConvertTemperature(ThermalAlertMonitor.MaximumThresholdCelsius);
            ThermalAlertThresholdDisplayValue = _unitFormattingService.ConvertTemperature(_thresholdCelsius);
        }
        finally
        {
            _suppressThresholdSync = false;
        }
    }

    /// <summary>
    /// Fires a notification through the exact same path a real thermal alert uses, so the user can
    /// confirm notifications reach the screen. Independent of staged edits — it uses the SAVED settings.
    /// </summary>
    [RelayCommand]
    private async Task SendTestNotificationAsync()
    {
        StartupStatusMessage = await _thermalAlertMonitor.TrySendTestNotificationAsync()
            ? "Test notification sent."
            : "Sending the test notification failed. See the log.";
    }
}
