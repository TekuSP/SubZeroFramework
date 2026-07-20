using System.Collections.ObjectModel;
using System.Reactive.Disposables;
using System.Reactive.Disposables.Fluent;
using System.Reactive.Linq;

using CommunityToolkit.Mvvm.ComponentModel;

using DynamicData;

using FrameworkDotnet.Enums;

using Material.Icons;

using SubZeroFramework.Controls.Dashboard.Models;
using SubZeroFramework.Controls.Fans.Models;
using SubZeroFramework.Controls.Thermal.Models;
using SubZeroFramework.Models;
using SubZeroFramework.Services.Units;
using SubZeroFramework.Services;

namespace SubZeroFramework.Presentation.MenuItems.Dashboard;

/// <summary>
/// Page model for the redesigned Dashboard: cooling-profile presets (applied to every fan, selection derived
/// from the live control states), per-fan quick-control cards, thermal snapshot bars, and the power summary.
/// Everything here renders LIVE values only — no telemetry history is subscribed (the old dashboard's
/// one-hour history replay saturated the UI thread at startup once the service had been running a while).
/// </summary>
public partial class DashboardModel : ObservableObject, IDisposable
{
    private readonly CompositeDisposable _subscriptions = [];
    private readonly ObservableCollection<FanQuickControlModel> _quickFans = [];
    private readonly ObservableCollection<ThermalSensorModel> _thermalSensors = [];
    private readonly Dictionary<int, FanCardModel> _fanCardsByIndex = [];
    private readonly Dictionary<int, FanQuickControlModel> _quickFansByIndex = [];
    private readonly Dictionary<int, FanCapabilityState> _fanCapabilities = [];
    private readonly Dictionary<int, FanControlStateSnapshot> _fanControlStates = [];
    private readonly Dictionary<int, FanStateSnapshot> _fanStates = [];
    private readonly Dictionary<int, TemperatureTelemetrySnapshot> _temperatureSnapshots = [];
    private readonly Dictionary<int, ThermalSensorModel> _thermalSensorsByIndex = [];
    private readonly Dictionary<int, BatteryTelemetrySnapshot> _batterySnapshots = [];
    private readonly SynchronizationContext _synchronizationContext;
    private readonly IUnitFormattingService _unitFormattingService;
    private readonly IFanControlActuator _fanControlActuator;

    public DashboardModel(
        IStringLocalizer localizer,
        IOptions<AppConfig> appInfo,
        IFrameworkStatusClient frameworkStatusClient,
        IFrameworkTelemetryClient frameworkTelemetryClient,
        IFanCapabilityClient fanCapabilityClient,
        IFanControlStateClient fanControlStateClient,
        IFanStateClient fanStateClient,
        IFanTelemetryClient fanTelemetryClient,
        ITemperatureTelemetryClient temperatureTelemetryClient,
        IBatteryTelemetryClient batteryTelemetryClient,
        IUserUnitPreferencesClient userUnitPreferencesClient,
        IUnitFormattingService unitFormattingService,
        IFanControlActuator fanControlActuator,
        IPowerDeliveryClient powerDeliveryClient,
        SynchronizationContext synchronizationContext)
    {
        _unitFormattingService = unitFormattingService;
        _fanControlActuator = fanControlActuator;
        _synchronizationContext = synchronizationContext;

        QuickFans = new ReadOnlyObservableCollection<FanQuickControlModel>(_quickFans);
        ThermalSensors = new ReadOnlyObservableCollection<ThermalSensorModel>(_thermalSensors);

        frameworkStatusClient
            .WatchStatus()
            .ObserveOn(_synchronizationContext)
            .Subscribe(status => LastStatus = status)
            .DisposeWith(_subscriptions);

        powerDeliveryClient
            .WatchPorts()
            .ObserveOn(_synchronizationContext)
            .Subscribe(UpdateAdapterInput)
            .DisposeWith(_subscriptions);

        fanCapabilityClient
            .WatchFanCapabilities()
            .ObserveOn(_synchronizationContext)
            .Subscribe(set =>
            {
                foreach (var change in set)
                {
                    _fanCardsByIndex.TryGetValue(change.Key, out var fan);

                    if (change.Reason == ChangeReason.Remove)
                    {
                        _fanCapabilities.Remove(change.Key);
                        if (fan is not null)
                        {
                            fan.Capability = null;
                        }

                        continue;
                    }

                    _fanCapabilities[change.Key] = change.Current;
                    if (fan is not null)
                    {
                        fan.Capability = change.Current;
                    }
                }
            })
            .DisposeWith(_subscriptions);

        fanControlStateClient
            .WatchFanControlStates()
            .ObserveOn(_synchronizationContext)
            .Subscribe(set =>
            {
                foreach (var change in set)
                {
                    _fanCardsByIndex.TryGetValue(change.Key, out var fan);

                    if (change.Reason == ChangeReason.Remove)
                    {
                        _fanControlStates.Remove(change.Key);
                        if (fan is not null)
                        {
                            fan.ControlState = null;
                        }

                        continue;
                    }

                    _fanControlStates[change.Key] = change.Current;
                    if (fan is not null)
                    {
                        fan.ControlState = change.Current;
                    }
                }

                RecomputePresetSelection();
            })
            .DisposeWith(_subscriptions);

        fanStateClient
            .WatchFanStates()
            .ObserveOn(_synchronizationContext)
            .Subscribe(set =>
            {
                foreach (var change in set)
                {
                    _fanCardsByIndex.TryGetValue(change.Key, out var fan);

                    if (change.Reason == ChangeReason.Remove)
                    {
                        _fanStates.Remove(change.Key);
                        if (fan is not null)
                        {
                            fan.FanState = null;
                        }

                        continue;
                    }

                    _fanStates[change.Key] = change.Current;
                    if (fan is not null)
                    {
                        fan.FanState = change.Current;
                    }
                }
            })
            .DisposeWith(_subscriptions);

        fanTelemetryClient
            .WatchFans()
            .ObserveOn(_synchronizationContext)
            .Subscribe(set =>
            {
                foreach (var change in set)
                {
                    if (change.Reason == ChangeReason.Add)
                    {
                        if (_fanCardsByIndex.TryGetValue(change.Key, out var existingFan))
                        {
                            existingFan.Snapshot = change.Current;
                            continue;
                        }

                        var fan = new FanCardModel(_unitFormattingService)
                        {
                            Snapshot = change.Current,
                            Capability = _fanCapabilities.GetValueOrDefault(change.Current.FanIndex),
                            ControlState = _fanControlStates.GetValueOrDefault(change.Current.FanIndex),
                            FanState = _fanStates.GetValueOrDefault(change.Current.FanIndex),
                        };

                        _fanCardsByIndex[change.Key] = fan;

                        var quickFan = new FanQuickControlModel(fan);
                        _quickFansByIndex[change.Key] = quickFan;
                        InsertSorted(_quickFans, quickFan, model => model.FanIndex);
                        continue;
                    }

                    if (change.Reason == ChangeReason.Update || change.Reason == ChangeReason.Refresh)
                    {
                        if (_fanCardsByIndex.TryGetValue(change.Current.FanIndex, out var fan))
                        {
                            fan.Snapshot = change.Current;
                        }

                        continue;
                    }

                    if (change.Reason == ChangeReason.Remove)
                    {
                        _fanCardsByIndex.Remove(change.Key);

                        if (_quickFansByIndex.Remove(change.Key, out var quickFan))
                        {
                            quickFan.Detach();
                            _quickFans.Remove(quickFan);
                        }
                    }
                }

                UpdateAverageFanSpeed();
            })
            .DisposeWith(_subscriptions);

        temperatureTelemetryClient
            .WatchTemperatures()
            .ObserveOn(_synchronizationContext)
            .Subscribe(set =>
            {
                foreach (var change in set)
                {
                    if (change.Reason == ChangeReason.Remove)
                    {
                        _temperatureSnapshots.Remove(change.Key);
                        if (_thermalSensorsByIndex.Remove(change.Key, out var removedSensor))
                        {
                            _thermalSensors.Remove(removedSensor);
                        }

                        continue;
                    }

                    _temperatureSnapshots[change.Key] = change.Current;

                    if (_thermalSensorsByIndex.TryGetValue(change.Key, out var sensor))
                    {
                        sensor.Snapshot = change.Current;
                        continue;
                    }

                    var thermalSensor = new ThermalSensorModel(_unitFormattingService)
                    {
                        Snapshot = change.Current,
                    };

                    _thermalSensorsByIndex[change.Key] = thermalSensor;
                    InsertSorted(_thermalSensors, thermalSensor, item => item.Snapshot.SensorIndex);
                }

                UpdateThermalSummary();
            })
            .DisposeWith(_subscriptions);

        batteryTelemetryClient
            .WatchBatteries()
            .ObserveOn(_synchronizationContext)
            .Subscribe(set =>
            {
                foreach (var change in set)
                {
                    if (change.Reason == ChangeReason.Remove)
                    {
                        _batterySnapshots.Remove(change.Key);
                        continue;
                    }

                    _batterySnapshots[change.Key] = change.Current;
                }

                UpdatePowerSummary();
            })
            .DisposeWith(_subscriptions);

        userUnitPreferencesClient
            .WatchPreferences()
            .ObserveOn(_synchronizationContext)
            .Subscribe(_ => RefreshUnitFormatting())
            .DisposeWith(_subscriptions);
    }

    [ObservableProperty]
    public partial FrameworkSystemStatus? LastStatus { get; set; }

    public ReadOnlyObservableCollection<FanQuickControlModel> QuickFans { get; }

    public ReadOnlyObservableCollection<ThermalSensorModel> ThermalSensors { get; }

    private static void InsertSorted<TModel>(ObservableCollection<TModel> target, TModel item, Func<TModel, int> keySelector)
    {
        var itemKey = keySelector(item);
        var insertIndex = 0;

        while (insertIndex < target.Count && keySelector(target[insertIndex]) < itemKey)
        {
            insertIndex++;
        }

        target.Insert(insertIndex, item);
    }

    private void RefreshUnitFormatting()
    {
        foreach (var fan in _fanCardsByIndex.Values)
        {
            fan.RefreshUnitFormatting();
        }

        foreach (var sensor in _thermalSensors)
        {
            sensor.RefreshUnitFormatting();
        }

        UpdateAverageFanSpeed();
        UpdateThermalSummary();
        UpdatePowerSummary();
    }

    // ----- Cooling profile presets (one preset applied to every fan; selection derived from live states) -----

    /// <summary>Pre-release feature flag: cooling profiles are not supported yet, so the section renders
    /// grayed and inert (no selection derivation, no actuation). Flip when profiles ship.</summary>
    private static readonly bool CoolingProfilesEnabled = false;

    public IReadOnlyList<CoolingPresetCardModel> CoolingPresets { get; } =
    [
        new(CoolingPresetKind.Silent, "Silent", "Quietest — fans idle when cool", MaterialIconKind.VolumeLow),
        new(CoolingPresetKind.Balanced, "Balanced", "Steady 45% baseline airflow", MaterialIconKind.ScaleBalance),
        new(CoolingPresetKind.Performance, "Performance", "Cooler — higher baseline", MaterialIconKind.SpeedometerMedium),
        new(CoolingPresetKind.Turbo, "Turbo", "Max airflow, loudest", MaterialIconKind.Speedometer),
        new(CoolingPresetKind.Custom, "Custom", "Per-fan settings you tuned", MaterialIconKind.TuneVariant),
    ];

    private const double BalancedDutyPercent = 45d;
    private const double PerformanceDutyPercent = 65d;

    [ObservableProperty]
    public partial string AverageFanSpeedDisplay { get; set; } = "--";

    [ObservableProperty]
    public partial string CoolingProfileSubtitle { get; set; } = "Coming soon";

    [ObservableProperty]
    public partial bool IsFanControlEnabled { get; set; }

    partial void OnLastStatusChanged(FrameworkSystemStatus? value)
    {
        IsFanControlEnabled = value?.IsGrpcActive == true && value.IsFanControlEnabled;
    }

    public async Task ApplyPresetAsync(CoolingPresetKind kind)
    {
        if (!CoolingProfilesEnabled || !IsFanControlEnabled || kind == CoolingPresetKind.Custom)
        {
            // Custom is an indicator of per-fan tuned state, not an actuatable preset.
            return;
        }

        foreach (var fanIndex in _fanCardsByIndex.Keys.OrderBy(index => index).ToArray())
        {
            var (mode, duty) = kind switch
            {
                CoolingPresetKind.Silent => (FanControlMode.Auto, 0d),
                CoolingPresetKind.Balanced => (FanControlMode.Manual, BalancedDutyPercent),
                CoolingPresetKind.Performance => (FanControlMode.Manual, PerformanceDutyPercent),
                _ => (FanControlMode.Max, 100d),
            };

            await _fanControlActuator.ActuateSimpleAsync(fanIndex, mode, duty, preview: false).ConfigureAwait(false);
        }
    }

    /// <summary>Selection is derived from the live control states so it reflects reality across restarts.</summary>
    private void RecomputePresetSelection()
    {
        if (!CoolingProfilesEnabled)
        {
            return;
        }

        var states = _fanControlStates.Values;
        CoolingPresetKind? selected = states.Count == 0
            ? null
            : states.All(state => state.Mode == FanControlMode.Auto) ? CoolingPresetKind.Silent
            : states.All(state => state.Mode == FanControlMode.Max) ? CoolingPresetKind.Turbo
            : states.All(state => state.Mode == FanControlMode.Manual && Math.Abs((state.LastDutyPercent ?? -1) - BalancedDutyPercent) < 1d) ? CoolingPresetKind.Balanced
            : states.All(state => state.Mode == FanControlMode.Manual && Math.Abs((state.LastDutyPercent ?? -1) - PerformanceDutyPercent) < 1d) ? CoolingPresetKind.Performance
            : CoolingPresetKind.Custom;

        foreach (var preset in CoolingPresets)
        {
            preset.IsSelected = preset.Kind == selected;
        }
    }

    private void UpdateAverageFanSpeed()
    {
        var speeds = _fanCardsByIndex.Values
            .Where(fan => fan.Snapshot.IsAvailable)
            .Select(fan => fan.Snapshot.SpeedRpm)
            .ToArray();

        AverageFanSpeedDisplay = speeds.Length == 0 ? "--" : _unitFormattingService.FormatFanSpeed(speeds.Average());
    }

    // ----- Thermal snapshot summary -----

    [ObservableProperty]
    public partial string DrivingTemperatureDisplay { get; set; } = "--";

    private void UpdateThermalSummary()
    {
        var maxCelsius = _temperatureSnapshots.Values
            .Where(snapshot => snapshot.IsAvailable)
            .Select(snapshot => snapshot.TemperatureCelsius)
            .Max();

        DrivingTemperatureDisplay = _unitFormattingService.FormatTemperature(maxCelsius);
    }

    // ----- Power summary -----

    [ObservableProperty]
    public partial double BatteryChargeFraction { get; set; }

    [ObservableProperty]
    public partial string BatteryChargeText { get; set; } = "--";

    [ObservableProperty]
    public partial bool IsBatteryCharging { get; set; }

    [ObservableProperty]
    public partial string ChargingStateText { get; set; } = "Waiting for battery";

    [ObservableProperty]
    public partial string AdapterWattsDisplay { get; set; } = "—";

    [ObservableProperty]
    public partial string FullInDisplay { get; set; } = "—";

    private void UpdatePowerSummary()
    {
        var battery = _batterySnapshots.Values.FirstOrDefault(snapshot => snapshot.IsAvailable);

        if (battery is null)
        {
            BatteryChargeFraction = 0d;
            BatteryChargeText = "--";
            IsBatteryCharging = false;
            ChargingStateText = "No battery detected";
            FullInDisplay = "—";
            return;
        }

        BatteryChargeFraction = Math.Clamp((battery.ChargePercent ?? 0d) / 100d, 0d, 1d);
        // Bare value only: BatteryChargeRingView renders its own "%" suffix.
        BatteryChargeText = _unitFormattingService.FormatRatioValue(battery.ChargePercent, decimals: 0);
        IsBatteryCharging = battery.BatteryState == FrameworkBatteryState.Charging;

        ChargingStateText = battery.BatteryState switch
        {
            FrameworkBatteryState.Charging => "Charging",
            FrameworkBatteryState.Discharging => "Discharging",
            _ => battery.PowerSourceState?.ToString() is string source && source.Contains("Ac", StringComparison.OrdinalIgnoreCase)
                ? "On AC power"
                : "Idle",
        };

        // Time-to-full: remaining capacity gap over the live charge current.
        FullInDisplay = IsBatteryCharging
            && battery.LastFullChargeCapacityAmpereHours is double fullCapacity
            && battery.RemainingCapacityAmpereHours is double remaining
            && battery.Amperage is double amps
            && Math.Abs(amps) > 0.05d
            && fullCapacity > remaining
                ? $"~{Math.Round((fullCapacity - remaining) / Math.Abs(amps) * 60d):0} min"
                : "—";
    }

    private void UpdateAdapterInput(IReadOnlyList<PowerDeliveryPortStatus> ports)
    {
        var activePort = ports.FirstOrDefault(port => port.IsActivePort && port.HasContract);
        var watts = activePort is null ? 0d : activePort.VoltageVolts * activePort.CurrentAmperes;

        AdapterWattsDisplay = watts > 0d
            ? _unitFormattingService.FormatPowerWatts(Math.Round(watts), decimals: 0)
            : "—";
    }

    public void Dispose()
    {
        _subscriptions.Dispose();
        foreach (var quickFan in _quickFans) quickFan.Detach();
    }
}
