using System.Reactive.Disposables;
using System.Reactive.Disposables.Fluent;
using System.Reactive.Linq;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.WinUI;

using DynamicData;

using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

using SubZeroFramework.Controls.Settings.Models;
using SubZeroFramework.Services;
using SubZeroFramework.Services.Units;

namespace SubZeroFramework.Presentation.MenuItems.Settings.Sections;

/// <summary>
/// ViewModel for the Display units section: one segmented-picker row per UnitsNet quantity, each with a
/// live sample value rendered in the currently applied unit. Navigation constructs it (ViewMap-registered);
/// telemetry and preference callbacks marshal to the UI thread before touching bindable state. The page
/// that navigated here disposes it when another section takes over.
///
/// Unit picks are STAGED: tapping a segment updates only the row's pill selection, not the machine-wide
/// preference. Save persists every staged pick (applying them live across the app); Cancel re-reads the
/// applied selections. The live sample values follow the APPLIED unit, so they update after Save, not on a
/// staged pick.
/// </summary>
public partial class SettingsUnitsSectionModel : ObservableObject, IDisposable
{
    // Representative fallbacks shown until (or when no) live telemetry backs a sample row.
    private const double FallbackTemperatureCelsius = 65d;
    private const double FallbackFanRpm = 3200d;
    private const double FallbackClockMegahertz = 3600d;
    private const double FallbackRefreshHertz = 60d;
    private const ulong FallbackInformationBytes = 34_359_738_368; // 32 GiB
    private const double FallbackVoltageVolts = 15.4d;
    private const double FallbackCurrentAmperes = 1.2d;
    private const double FallbackChargeAmpereHours = 3.5d;
    private const double FallbackRatioPercent = 76d;
    private const double FallbackBitRateBitsPerSecond = 1_000_000_000d;
    private const double FallbackPowerWatts = 18d;
    // Length/airflow have no live telemetry source; these mirror typical FW16 fan specs.
    private const double SampleLengthMillimeters = 75d;
    private const double SampleAirflowCfm = 42d;

    private readonly CompositeDisposable _subscriptions = [];
    private readonly IUserUnitPreferencesClient _userUnitPreferencesClient;
    private readonly IUnitFormattingService _unitFormattingService;
    private readonly DispatcherQueue _dispatcherQueue;

    // The applied (persisted) option key per quantity — the baseline for dirty detection and Cancel.
    private readonly Dictionary<UnitQuantityKind, string> _savedSelection = [];
    // Each quantity's catalog default, used to stage "Reset to defaults".
    private readonly Dictionary<UnitQuantityKind, string> _defaultSelection = [];

    private double? _sampleTemperatureCelsius;
    private double? _sampleFanRpm;
    private double? _sampleClockMegahertz;
    private double? _sampleRefreshHertz;
    private ulong? _sampleInformationBytes;
    private double? _sampleVoltageVolts;
    private double? _sampleCurrentAmperes;
    private double? _sampleChargeAmpereHours;
    private double? _sampleRatioPercent;
    private double? _sampleBitRateBitsPerSecond;
    private double? _samplePowerWatts;

    public SettingsUnitsSectionModel(
        UnitPreferenceCatalog unitPreferenceCatalog,
        IUserUnitPreferencesClient userUnitPreferencesClient,
        IUnitFormattingService unitFormattingService,
        ITemperatureTelemetryClient temperatureTelemetryClient,
        IFanTelemetryClient fanTelemetryClient,
        IBatteryTelemetryClient batteryTelemetryClient,
        IHardwareInfoClient hardwareInfoClient,
        DispatcherQueue dispatcherQueue)
    {
        ArgumentNullException.ThrowIfNull(unitPreferenceCatalog);
        ArgumentNullException.ThrowIfNull(userUnitPreferencesClient);
        ArgumentNullException.ThrowIfNull(unitFormattingService);
        ArgumentNullException.ThrowIfNull(temperatureTelemetryClient);
        ArgumentNullException.ThrowIfNull(fanTelemetryClient);
        ArgumentNullException.ThrowIfNull(batteryTelemetryClient);
        ArgumentNullException.ThrowIfNull(hardwareInfoClient);
        ArgumentNullException.ThrowIfNull(dispatcherQueue);

        _userUnitPreferencesClient = userUnitPreferencesClient;
        _unitFormattingService = unitFormattingService;
        _dispatcherQueue = dispatcherQueue;

        UnitRows =
        [
            .. unitPreferenceCatalog.Definitions.Select(definition => new UnitPreferenceRowModel(definition, HandleUnitRowSelectionChanged))
        ];
        foreach (var definition in unitPreferenceCatalog.Definitions)
        {
            _defaultSelection[definition.Kind] = definition.DefaultOptionKey;
        }
        ApplyUnitPreferenceSnapshot(unitPreferenceCatalog.Normalize(_userUnitPreferencesClient.CurrentPreferences));

        userUnitPreferencesClient
            .WatchPreferences()
            .Select(snapshot => Observable.FromAsync(_ => _dispatcherQueue.EnqueueAsync(() => ApplyUnitPreferenceSnapshot(snapshot))))
            .Concat()
            .Subscribe()
            .DisposeWith(_subscriptions);

        // Live sample feeds for the rows. Each stream reduces to one representative number; sampling caps
        // UI churn to one refresh per two seconds.
        temperatureTelemetryClient
            .WatchTemperatures()
            .QueryWhenChanged(query => query.Items.Max(sensor => sensor.TemperatureCelsius))
            .Sample(TimeSpan.FromSeconds(2))
            .Subscribe(celsius => UpdateSample(() => _sampleTemperatureCelsius = celsius))
            .DisposeWith(_subscriptions);

        fanTelemetryClient
            .WatchFans()
            .QueryWhenChanged(query => query.Items.Where(fan => fan.IsAvailable).Select(fan => (double?)fan.SpeedRpm).Max())
            .Sample(TimeSpan.FromSeconds(2))
            .Subscribe(rpm => UpdateSample(() => _sampleFanRpm = rpm))
            .DisposeWith(_subscriptions);

        batteryTelemetryClient
            .WatchBatteries()
            .QueryWhenChanged(query => query.Items.FirstOrDefault(battery => battery.IsAvailable))
            .Sample(TimeSpan.FromSeconds(2))
            .Subscribe(battery => UpdateSample(() =>
            {
                _sampleVoltageVolts = battery?.Voltage;
                _sampleCurrentAmperes = battery?.Amperage is double amperage ? Math.Abs(amperage) : null;
                _sampleChargeAmpereHours = battery?.RemainingCapacityAmpereHours;
                _sampleRatioPercent = battery?.ChargePercent;
                _samplePowerWatts = battery?.Voltage is double volts && battery?.Amperage is double amps
                    ? Math.Abs(volts * amps)
                    : null;
            }))
            .DisposeWith(_subscriptions);

        hardwareInfoClient
            .WatchHardwareInfo()
            .Subscribe(snapshot => UpdateSample(() =>
            {
                _sampleClockMegahertz = snapshot.Cpus
                    .Select(cpu => cpu.CurrentClockSpeedMHz > 0 ? (double?)cpu.CurrentClockSpeedMHz : cpu.MaxClockSpeedMHz)
                    .FirstOrDefault(clock => clock > 0);
                _sampleRefreshHertz = snapshot.Monitors
                    .Select(monitor => (double?)monitor.CurrentRefreshRate)
                    .FirstOrDefault(rate => rate > 0);
                var totalMemoryBytes = snapshot.MemoryModules.Aggregate(0UL, (total, module) => total + module.CapacityBytes);
                _sampleInformationBytes = totalMemoryBytes > 0 ? totalMemoryBytes : null;
                _sampleBitRateBitsPerSecond = snapshot.NetworkAdapters
                    .Select(adapter => (double?)adapter.Speed)
                    .Where(speed => speed > 0)
                    .Max();
            }))
            .DisposeWith(_subscriptions);
    }

    public IReadOnlyList<UnitPreferenceRowModel> UnitRows { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UnitsStatusVisibility))]
    public partial string UnitsStatusMessage { get; set; } = string.Empty;

    public Visibility UnitsStatusVisibility => string.IsNullOrEmpty(UnitsStatusMessage) ? Visibility.Collapsed : Visibility.Visible;

    /// <summary>True while any staged pick differs from the applied selection (enables Save/Cancel).</summary>
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

    private bool CanSaveOrCancel() => HasUnsavedChanges && !IsSaving;

    /// <summary>Persists every staged pick, applying the new units live across the app.</summary>
    [RelayCommand(CanExecute = nameof(CanSaveOrCancel))]
    private async Task SaveAsync()
    {
        IsSaving = true;

        try
        {
            var snapshot = new UserUnitPreferencesSnapshot
            {
                SchemaVersion = UserUnitPreferencesSnapshot.CurrentSchemaVersion,
                Entries = [.. UnitRows.Select(unitRow => new UserUnitPreferenceEntry(unitRow.Kind, unitRow.SelectedKey))],
            };

            // On the UI thread; no ConfigureAwait(false) so the continuation stays here. Success streams
            // back through WatchPreferences, which re-baselines and refreshes the samples.
            var result = await _userUnitPreferencesClient.ApplyPreferencesAsync(snapshot, CancellationToken.None);
            if (result.Succeeded)
            {
                foreach (var row in UnitRows)
                {
                    _savedSelection[row.Kind] = row.SelectedKey;
                }

                UnitsStatusMessage = string.Empty;
                UpdateDirtyState();
            }
            else
            {
                UnitsStatusMessage = result.Message;
            }
        }
        finally
        {
            IsSaving = false;
        }
    }

    /// <summary>Discards staged picks, restoring the pills to the applied selection.</summary>
    [RelayCommand(CanExecute = nameof(CanSaveOrCancel))]
    private void Cancel()
    {
        foreach (var row in UnitRows)
        {
            row.ApplySelection(_savedSelection.GetValueOrDefault(row.Kind, row.SelectedKey));
        }

        UnitsStatusMessage = string.Empty;
        UpdateDirtyState();
    }

    /// <summary>Stages each quantity's catalog default (Save then applies them).</summary>
    [RelayCommand]
    private void ResetToDefaults()
    {
        foreach (var row in UnitRows)
        {
            row.ApplySelection(_defaultSelection.GetValueOrDefault(row.Kind, row.SelectedKey));
        }

        UpdateDirtyState();
    }

    // A tapped segment already updated the row's pill selection; staging just re-evaluates dirtiness.
    private void HandleUnitRowSelectionChanged(UnitPreferenceRowModel row) => UpdateDirtyState();

    private void UpdateDirtyState()
        => HasUnsavedChanges = UnitRows.Any(row =>
            !string.Equals(row.SelectedKey, _savedSelection.GetValueOrDefault(row.Kind, row.SelectedKey), StringComparison.Ordinal));

    private void ApplyUnitPreferenceSnapshot(UserUnitPreferencesSnapshot snapshot)
    {
        // The applied baseline always follows the persisted snapshot. Only re-seat the pills when there are
        // no staged picks in flight, so an external unit change never clobbers the user's uncommitted edits.
        var hadStagedPicks = HasUnsavedChanges;

        foreach (var row in UnitRows)
        {
            var key = snapshot.GetOptionKey(row.Kind, row.SelectedKey);
            _savedSelection[row.Kind] = key;

            if (!hadStagedPicks)
            {
                row.ApplySelection(key);
            }
        }

        UpdateSampleTexts();
        UpdateDirtyState();
    }

    private void UpdateSample(Action applyLatestValues)
    {
        _dispatcherQueue.TryEnqueue(() =>
        {
            applyLatestValues();
            UpdateSampleTexts();
        });
    }

    private void UpdateSampleTexts()
    {
        foreach (var row in UnitRows)
        {
            row.SampleText = row.Kind switch
            {
                UnitQuantityKind.Temperature => _unitFormattingService.FormatTemperature(_sampleTemperatureCelsius ?? FallbackTemperatureCelsius, decimals: 1),
                UnitQuantityKind.FanSpeed => _unitFormattingService.FormatFanSpeed(_sampleFanRpm ?? FallbackFanRpm),
                UnitQuantityKind.ClockFrequency => _unitFormattingService.FormatClockFrequencyMegahertz(_sampleClockMegahertz ?? FallbackClockMegahertz),
                UnitQuantityKind.RefreshRate => _unitFormattingService.FormatRefreshRateHertz(_sampleRefreshHertz ?? FallbackRefreshHertz),
                UnitQuantityKind.InformationSize => _unitFormattingService.FormatInformationBytes(_sampleInformationBytes ?? FallbackInformationBytes),
                UnitQuantityKind.Voltage => _unitFormattingService.FormatVoltage(_sampleVoltageVolts ?? FallbackVoltageVolts),
                UnitQuantityKind.Current => _unitFormattingService.FormatCurrent(_sampleCurrentAmperes ?? FallbackCurrentAmperes),
                UnitQuantityKind.ElectricChargeCapacity => _unitFormattingService.FormatChargeCapacity(_sampleChargeAmpereHours ?? FallbackChargeAmpereHours),
                UnitQuantityKind.Ratio => _unitFormattingService.FormatRatio(_sampleRatioPercent ?? FallbackRatioPercent),
                UnitQuantityKind.Length => _unitFormattingService.FormatLengthMillimeters(SampleLengthMillimeters),
                UnitQuantityKind.Airflow => _unitFormattingService.FormatAirflowCfm(SampleAirflowCfm),
                UnitQuantityKind.BitRate => _unitFormattingService.FormatBitRateBitsPerSecond(_sampleBitRateBitsPerSecond ?? FallbackBitRateBitsPerSecond),
                UnitQuantityKind.Power => _unitFormattingService.FormatPowerWatts(_samplePowerWatts ?? FallbackPowerWatts),
                _ => row.SampleText,
            };
        }
    }

    public void Dispose()
    {
        _subscriptions.Dispose();
    }
}
