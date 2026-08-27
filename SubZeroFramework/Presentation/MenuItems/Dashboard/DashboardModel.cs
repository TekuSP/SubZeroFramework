using System.Collections.ObjectModel;
using System.Reactive.Disposables;
using System.Reactive.Disposables.Fluent;
using System.Reactive.Linq;

using CommunityToolkit.Mvvm.ComponentModel;

using DynamicData;

using FrameworkDotnet.Enums;

using Material.Icons;

using Microsoft.UI.Xaml.Media;

using SubZeroFramework.Controls.Dashboard.Models;
using SubZeroFramework.Controls.Fans.Models;
using SubZeroFramework.Controls.Thermal.Models;
using SubZeroFramework.Models;
using SubZeroFramework.Services.Units;
using SubZeroFramework.Services;
using SubZeroFramework.Themes;

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
    private readonly ObservableCollection<FanProfileCardModel> _profiles = [];
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
    private readonly IFrameworkFanControlClient _fanControlClient;
    private readonly ILocalFanProfileStore _profileStore;

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
        IFrameworkFanControlClient fanControlClient,
        ILocalFanProfileStore profileStore,
        IPowerDeliveryClient powerDeliveryClient,
        SynchronizationContext synchronizationContext)
    {
        _unitFormattingService = unitFormattingService;
        _fanControlActuator = fanControlActuator;
        _fanControlClient = fanControlClient;
        _profileStore = profileStore;
        _synchronizationContext = synchronizationContext;

        QuickFans = new ReadOnlyObservableCollection<FanQuickControlModel>(_quickFans);
        ThermalSensors = new ReadOnlyObservableCollection<ThermalSensorModel>(_thermalSensors);
        Profiles = new ReadOnlyObservableCollection<FanProfileCardModel>(_profiles);

        if (ProfilesEnabled)
        {
            // The store is edited from dialogs this page owns, so following it rather than re-reading after
            // each command keeps one path for "the list changed" whether the change came from here or from a
            // restart.
            _profileStore.Changed += OnProfileStoreChanged;
            RefreshProfiles();
        }

        frameworkStatusClient
            .WatchStatus()
            .Sample(TelemetryRateLimits.LiveReadout)
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
            .Batch(TelemetryRateLimits.LiveReadout)
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

                if (ProfilesEnabled)
                {
                    // Seeded from the fans the service actually reports, so a machine with three fans does
                    // not get profiles describing four. Only ever on an empty list; see SeedIfEmpty.
                    _profileStore.SeedIfEmpty([.. _fanControlStates.Keys]);

                    RecomputeProfileSelection();
                }
            })
            .DisposeWith(_subscriptions);

        fanStateClient
            .WatchFanStates()
            .Batch(TelemetryRateLimits.LiveReadout)
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
            .Batch(TelemetryRateLimits.LiveReadout)
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

                        var quickFan = new FanQuickControlModel(fan, _unitFormattingService);
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
            .Batch(TelemetryRateLimits.LiveReadout)
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
            .Batch(TelemetryRateLimits.LiveReadout)
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
        RefreshAdapterBadge();
        BatteryChargeUnitSuffix = _unitFormattingService.RatioUnitSuffix;

        // The canonical readings on this page are formatted by UnitFormatConverter at render time, so they
        // only need their bindings to run again — that is what the null property name asks for. See
        // UnitFormatConverter.
        OnPropertyChanged(propertyName: null);
    }

    // ----- Saved fan profiles (one profile applied to every fan; selection derived from live states) -----

    /// <summary>
    /// Pre-release feature flag: profiles are built but not switched on yet. Flip to ship them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Off means <b>entirely</b> off, not merely un-clickable: nothing seeds, nothing is written to disk, and
    /// the section does not render. A greyed-out section would be worse than none, because with seeding off
    /// there are no cards to grey — it would draw a heading over an empty space and read as broken rather
    /// than as unfinished.
    /// </para>
    /// <para>
    /// Not a <c>const</c>: that makes every guarded branch unreachable code and buries the feature under
    /// compiler warnings the moment it is turned off.
    /// </para>
    /// </remarks>
    private static readonly bool ProfilesEnabled = false;

    /// <summary>Whether the Profiles section renders at all. See <see cref="ProfilesEnabled"/>.</summary>
    public bool AreProfilesAvailable => ProfilesEnabled;

    /// <summary>Average speed across available fans, canonical RPM; null until a fan reports. Formatted by UnitFormatConverter.</summary>
    [ObservableProperty]
    public partial double? AverageFanSpeedRpm { get; set; }

    [ObservableProperty]
    public partial bool IsFanControlEnabled { get; set; }

    /// <summary>
    /// True when the fans are not doing what any saved profile asks for.
    /// </summary>
    /// <remarks>
    /// The prompt to save. Without it, tuning a fan silently deselects every card and the row just goes blank
    /// — which reads as the profiles having stopped working rather than as the user having moved past them.
    /// </remarks>
    [ObservableProperty]
    public partial bool IsModified { get; set; }

    /// <summary>The profile the fans currently match, or null when none does.</summary>
    [ObservableProperty]
    public partial string? ActiveProfileName { get; set; }

    public ReadOnlyObservableCollection<FanProfileCardModel> Profiles { get; }

    /// <summary>
    /// Reports which fans a profile could not be applied to.
    /// </summary>
    /// <remarks>
    /// Empty on success. Applying is a batch of independent commands and any one of them can legitimately
    /// fail — arming Adaptive on a fan with no driving sensors is the common case — so the caller is told
    /// exactly which fans were left alone rather than being given a single pass-or-fail.
    /// </remarks>
    public async Task<IReadOnlyList<string>> ApplyProfileAsync(FanProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        if (!ProfilesEnabled)
        {
            return [];
        }

        if (!IsFanControlEnabled)
        {
            return ["Fan control is not available right now."];
        }

        List<string> failures = [];

        // In fan order, so a machine where several fans share a heatsink ramps predictably rather than in
        // whatever order the profile happened to be written.
        foreach (var entry in profile.Fans.OrderBy(static entry => entry.FanIndex))
        {
            if (!_fanControlStates.TryGetValue(entry.FanIndex, out var state) || !state.IsAvailable)
            {
                // Not a failure worth reporting: a profile written while a module was attached should apply
                // cleanly to the fans that remain rather than complaining about the ones that left.
                continue;
            }

            var failure = await ApplyEntryAsync(entry, state).ConfigureAwait(false);
            if (failure is not null)
            {
                failures.Add($"{state.DisplayName}: {failure}");
            }
        }

        return failures;
    }

    private async Task<string?> ApplyEntryAsync(FanProfileEntry entry, FanControlStateSnapshot state)
    {
        try
        {
            switch (entry.Mode)
            {
                case FanControlMode.Adaptive:
                    // The profile carries the TARGET, and the fan keeps its own driving sensors. Sensor
                    // choice is a property of the hardware — which sensors this fan actually cools — not of
                    // the mood the user is in, and a profile overwriting it would silently undo work done on
                    // the Fan Control page.
                    var armed = await _fanControlClient.SetAdaptiveModeAsync(
                        entry.FanIndex,
                        [.. state.DrivingSensorIndices],
                        state.DrivingTemperatureAggregation,
                        state.AdaptiveSettings with { TargetTemperatureCelsius = entry.AdaptiveTargetCelsius })
                        .ConfigureAwait(false);

                    return armed.Succeeded ? null : armed.Message ?? "could not switch to Adaptive";

                case FanControlMode.CustomCurve:
                    var curve = await _fanControlClient
                        .SetActiveCurveProfileAsync(entry.FanIndex, entry.CurveSlot)
                        .ConfigureAwait(false);

                    return curve.Succeeded ? null : curve.Message ?? "could not switch to its saved curve";

                default:
                    await _fanControlActuator
                        .ActuateSimpleAsync(entry.FanIndex, entry.Mode, entry.DutyPercent, preview: false)
                        .ConfigureAwait(false);

                    return null;
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // One fan refusing must not abandon the rest of the profile half-applied.
            return exception.Message;
        }
    }

    /// <summary>Captures what every fan is doing right now as a new profile.</summary>
    public FanProfile CaptureCurrentSetup(string name) => new()
    {
        Id = Guid.NewGuid().ToString("N"),
        Name = name,
        Fans =
        [
            .. _fanControlStates.Values
                .Where(static state => state.IsAvailable)
                .OrderBy(static state => state.FanIndex)
                .Select(static state => new FanProfileEntry
                {
                    FanIndex = state.FanIndex,
                    Mode = state.Mode,

                    // Every mode's settings are captured, not just the active one's, so re-saving a profile
                    // after switching one fan to Auto does not throw away the duty it had before.
                    DutyPercent = state.LastDutyPercent ?? 0d,
                    CurveSlot = state.ActiveCurveSlot,
                    AdaptiveTargetCelsius = state.AdaptiveSettings.TargetTemperatureCelsius,
                }),
        ],
    };

    /// <summary>Formatting for the profile dialogs, so their summaries obey the user's chosen units too.</summary>
    public IUnitFormattingService UnitFormattingService => _unitFormattingService;

    public void SaveProfile(FanProfile profile) => _profileStore.Save(profile);

    /// <summary>
    /// Builds the model behind the manage dialog.
    /// </summary>
    /// <remarks>
    /// Constructed here rather than in the page because it needs the store, and handing a page a service just
    /// so it can hand it straight back to a dialog is a dependency the page has no other use for.
    /// </remarks>
    public FanProfileManageDialogModel CreateManageProfilesModel()
        => new(_profileStore, _unitFormattingService);

    /// <summary>Rebuilds the card list from the store, preserving which one reads as active.</summary>
    private void RefreshProfiles()
    {
        _profiles.Clear();

        foreach (var profile in _profileStore.Profiles)
        {
            _profiles.Add(new FanProfileCardModel(profile, _unitFormattingService));
        }

        RecomputeProfileSelection();
    }

    /// <summary>
    /// Selection is derived from the live control states, so it reflects reality across restarts.
    /// </summary>
    /// <remarks>
    /// Nothing stores "the active profile". A stored flag would keep claiming a profile was in effect after
    /// the user changed a fan by hand, which is precisely the moment the claim stops being true.
    /// </remarks>
    private void RecomputeProfileSelection()
    {
        var defaultId = _profileStore.DefaultProfileId;
        FanProfileCardModel? active = null;

        foreach (var card in _profiles)
        {
            card.IsDefault = card.Id == defaultId;

            // First match wins. Two profiles CAN describe the same state — "all fans Auto" saved twice under
            // different names — and lighting up both would suggest the app is confused rather than that the
            // user saved a duplicate.
            var matches = active is null && card.Profile.Matches(_fanControlStates);
            card.IsSelected = matches;

            if (matches)
            {
                active = card;
            }
        }

        ActiveProfileName = active?.Name;

        // Only once fans have actually reported. Before that, "no profile matches" is true but meaningless,
        // and showing Modified on a page that has not finished loading is just noise.
        IsModified = _fanControlStates.Count > 0 && active is null;

        foreach (var fan in _quickFans)
        {
            fan.ActiveProfileName = ActiveProfileName;
        }
    }

    private void UpdateAverageFanSpeed()
    {
        var speeds = _fanCardsByIndex.Values
            .Where(fan => fan.Snapshot.IsAvailable)
            .Select(fan => fan.Snapshot.SpeedRpm)
            .ToArray();

        AverageFanSpeedRpm = speeds.Length == 0 ? null : speeds.Average();
    }

    // ----- Thermal snapshot summary -----

    /// <summary>Hottest available sensor, canonical Celsius; null until one reports. Formatted by UnitFormatConverter.</summary>
    [ObservableProperty]
    public partial double? DrivingTemperatureCelsius { get; set; }

    private void UpdateThermalSummary()
    {
        var maxCelsius = _temperatureSnapshots.Values
            .Where(snapshot => snapshot.IsAvailable)
            .Select(snapshot => snapshot.TemperatureCelsius)
            .Max();

        DrivingTemperatureCelsius = maxCelsius;
    }

    // ----- Power summary -----

    [ObservableProperty]
    public partial double BatteryChargeFraction { get; set; }

    /// <summary>Charge in canonical percent for the ring centre, formatted (value-only) by UnitFormatConverter.</summary>
    [ObservableProperty]
    public partial double? BatteryChargePercent { get; set; }

    /// <summary>The unit the ring draws beside the figure — "%" only under the percent preference.</summary>
    [ObservableProperty]
    public partial string BatteryChargeUnitSuffix { get; set; } = "%";

    [ObservableProperty]
    public partial bool IsBatteryCharging { get; set; }

    [ObservableProperty]
    public partial string ChargingStateText { get; set; } = "Waiting for battery";

    /// <summary>State colour for the dot beside <see cref="ChargingStateText"/> inside the ring — green
    /// charging, amber discharging, neutral otherwise. Assigned at runtime on the UI thread (never in a
    /// field initializer) like the Power page's state brushes.</summary>
    [ObservableProperty]
    public partial Brush? ChargeStatusDotBrush { get; set; }

    /// <summary>Negotiated adapter input, canonical watts; null when no adapter is attached.</summary>
    [ObservableProperty]
    public partial double? AdapterInputWatts { get; set; }

    /// <summary>Adapter figure for the pill in the ring's bottom mouth ("240 W"); empty hides the pill.</summary>
    [ObservableProperty]
    public partial string AdapterBadgeText { get; set; } = string.Empty;

    /// <summary>Fine print under the state line ("full in ~21 min"); empty when there is no estimate.</summary>
    [ObservableProperty]
    public partial string FullInDetailText { get; set; } = string.Empty;

    private void UpdatePowerSummary()
    {
        var battery = _batterySnapshots.Values.FirstOrDefault(snapshot => snapshot.IsAvailable);

        if (battery is null)
        {
            BatteryChargeFraction = 0d;
            BatteryChargePercent = null;
            IsBatteryCharging = false;
            ChargingStateText = "No battery detected";
            ChargeStatusDotBrush = AppThemeBrushes.Get("TextSecondaryBrush", AppThemeBrushes.TextSecondaryColor);
            FullInDetailText = string.Empty;
            return;
        }

        BatteryChargeFraction = Math.Clamp((battery.ChargePercent ?? 0d) / 100d, 0d, 1d);
        BatteryChargePercent = battery.ChargePercent;
        IsBatteryCharging = battery.BatteryState == FrameworkBatteryState.Charging;

        ChargingStateText = battery.BatteryState switch
        {
            FrameworkBatteryState.Charging => "Charging",
            FrameworkBatteryState.Discharging => "Discharging",
            _ => battery.PowerSourceState?.ToString() is string source && source.Contains("Ac", StringComparison.OrdinalIgnoreCase)
                ? "On AC power"
                : "Idle",
        };

        ChargeStatusDotBrush = battery.BatteryState switch
        {
            FrameworkBatteryState.Charging => AppThemeBrushes.Get("StatusSuccessBrush", AppThemeBrushes.StatusSuccessColor),
            FrameworkBatteryState.Discharging => AppThemeBrushes.Get("StatusWarningBrush", AppThemeBrushes.StatusWarningColor),
            _ => AppThemeBrushes.Get("TextSecondaryBrush", AppThemeBrushes.TextSecondaryColor),
        };

        // Time-to-full: remaining capacity gap over the live charge current.
        FullInDetailText = IsBatteryCharging
            && battery.LastFullChargeCapacityAmpereHours is double fullCapacity
            && battery.RemainingCapacityAmpereHours is double remaining
            && battery.Amperage is double amps
            && Math.Abs(amps) > 0.05d
            && fullCapacity > remaining
                ? $"full in ~{Math.Round((fullCapacity - remaining) / Math.Abs(amps) * 60d):0} min"
                : string.Empty;
    }

    private void UpdateAdapterInput(IReadOnlyList<PowerDeliveryPortStatus> ports)
    {
        var activePort = ports.FirstOrDefault(port => port.IsActivePort && port.HasContract);
        var watts = activePort is null ? 0d : activePort.VoltageVolts * activePort.CurrentAmperes;

        AdapterInputWatts = watts > 0d ? watts : null;
        RefreshAdapterBadge();
    }

    // Composed here (not converter-formatted in XAML) so the pill can collapse entirely when no adapter is
    // attached — a converter would have to render a placeholder. Re-run on unit-preference changes too.
    private void RefreshAdapterBadge() =>
        AdapterBadgeText = AdapterInputWatts is double adapterWatts
            ? _unitFormattingService.FormatPowerWatts(adapterWatts, decimals: 0)
            : string.Empty;

    public void Dispose()
    {
        // The store outlives this page, so a page that forgot to unhook would be kept alive by it — and every
        // later profile edit would refresh a card list nothing is showing.
        _profileStore.Changed -= OnProfileStoreChanged;

        _subscriptions.Dispose();
        foreach (var quickFan in _quickFans) quickFan.Detach();
    }

    private void OnProfileStoreChanged(object? sender, EventArgs e)
        => _synchronizationContext.Post(_ => RefreshProfiles(), null);
}
