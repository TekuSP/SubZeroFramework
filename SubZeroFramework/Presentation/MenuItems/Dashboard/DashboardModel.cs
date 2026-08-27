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
public partial class DashboardModel : ObservableObject, IDisposable, IProfileCardActions
{
    private readonly CompositeDisposable _subscriptions = [];
    private readonly ObservableCollection<FanQuickControlModel> _quickFans = [];
    private readonly ObservableCollection<ThermalSensorModel> _thermalSensors = [];
    private readonly ObservableCollection<FanProfileCardModel> _profiles = [];

    /// <summary>The service's library, mirrored so a change set can be turned into a whole list.</summary>
    private readonly SourceCache<CoolingProfile, string> _profileLibrary = new(static profile => profile.Id);
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
    private readonly ICoolingProfileClient _coolingProfileClient;
    private readonly IDesktopNotificationService _notifications;
    private readonly IFanHistoryStore _historyStore;

    /// <summary>The service's current selection, mirrored so card state can be recomputed synchronously.</summary>
    private string? _activeProfileId;

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
        ICoolingProfileClient coolingProfileClient,
        IDesktopNotificationService notifications,
        IFanHistoryStore historyStore,
        IPowerDeliveryClient powerDeliveryClient,
        SynchronizationContext synchronizationContext)
    {
        _unitFormattingService = unitFormattingService;
        _fanControlActuator = fanControlActuator;
        _fanControlClient = fanControlClient;
        _coolingProfileClient = coolingProfileClient;
        _notifications = notifications;
        _historyStore = historyStore;
        _synchronizationContext = synchronizationContext;

        AttachFanHistory();

        QuickFans = new ReadOnlyObservableCollection<FanQuickControlModel>(_quickFans);
        ThermalSensors = new ReadOnlyObservableCollection<ThermalSensorModel>(_thermalSensors);
        Profiles = new ReadOnlyObservableCollection<FanProfileCardModel>(_profiles);

        // The library is the SERVICE'S, so following its stream is the one path for "the list changed" —
        // whether the change came from a dialog on this page, from another running client, or from a restart.
        _coolingProfileClient
            .WatchCoolingProfiles()
            .ObserveOn(_synchronizationContext)
            .Subscribe(RefreshProfiles)
            .DisposeWith(_subscriptions);

        _coolingProfileClient
            .WatchActiveProfileId()
            .ObserveOn(_synchronizationContext)
            .Subscribe(activeProfileId =>
            {
                _activeProfileId = activeProfileId;
                RecomputeProfileSelection();
            })
            .DisposeWith(_subscriptions);

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

                // Seeding is the service's job now — it happens once the machine reports its fans, whether or
                // not a client is connected.
                RecomputeProfileSelection();

                // Driving sensors can change with the control state, so the history watches are re-checked
                // here rather than once at startup.
                EnsureFanHistorySubscriptions();
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

    // ----- Cooling profiles (one profile applied to every fan; the service owns the library) -----

    /// <summary>Average speed across available fans, canonical RPM; null until a fan reports. Formatted by UnitFormatConverter.</summary>
    [ObservableProperty]
    public partial double? AverageFanSpeedRpm { get; set; }

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
    /// Asks the service to switch to a profile.
    /// </summary>
    /// <returns>The fans it could not be applied to. Empty on success.</returns>
    /// <remarks>
    /// The per-fan loop lives in the SERVICE now, so every client applies a profile the same way and a
    /// machine cannot end up half-applied because the app was closed mid-way through the batch.
    /// </remarks>
    public async Task<IReadOnlyList<string>> ApplyProfileAsync(CoolingProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        // No local pre-check on whether fan control is available: the service answers that authoritatively
        // in its reply, and a second opinion here could only ever be a staler one.
        var result = await _coolingProfileClient.SetActiveAsync(profile.Id).ConfigureAwait(true);

        if (result.FailedFanNames.Count > 0)
        {
            return result.FailedFanNames;
        }

        if (!result.Succeeded)
        {
            return [result.Message];
        }

        // Only on a CLEAN apply. A partial one already opens a dialog naming the fans that refused, and a
        // notification celebrating the same switch alongside it would contradict the dialog.
        _ = _notifications.TryShowStatusAsync($"{profile.Name} applied", DescribeProfileForNotification(profile));

        return [];
    }

    /// <summary>
    /// What a profile just did to each fan, as one line for a notification.
    /// </summary>
    /// <remarks>
    /// Per fan and by NAME, because the whole point of a profile is that it changes several fans at once and
    /// a notification saying only "Gaming applied" leaves the user to go and look at what that meant. Fans the
    /// machine no longer has are left out rather than named as unchanged.
    ///
    /// Every quantity goes through the formatting service, so the notification agrees with the units the user
    /// reads everywhere else — a target shown as 72 °C on the page must not arrive as 161.6 here.
    /// </remarks>
    private string DescribeProfileForNotification(CoolingProfile profile)
    {
        var parts = profile.Fans
            .OrderBy(static entry => entry.FanIndex)
            .Where(entry => _fanControlStates.ContainsKey(entry.FanIndex))
            .Select(entry => $"{FanName(entry.FanIndex)}: {DescribeEntryMode(entry)}");

        return string.Join(" · ", parts);
    }

    private string FanName(int fanIndex)
        => _fanControlStates.TryGetValue(fanIndex, out var state) ? state.DisplayName : $"Fan {fanIndex}";

    private string DescribeEntryMode(CoolingProfileFanEntry entry) => entry.Mode switch
    {
        FanControlMode.Manual => $"Manual {_unitFormattingService.FormatRatio(entry.DutyPercent, decimals: 0)}",
        FanControlMode.Adaptive => $"Adaptive {_unitFormattingService.FormatTemperature(entry.AdaptiveTargetCelsius)}",
        FanControlMode.CustomCurve => "Curve",
        FanControlMode.Max => "Max",
        _ => "Auto",
    };

    /// <summary>A new profile with every fan on Auto.</summary>
    /// <param name="name">What to call it.</param>
    /// <param name="iconName">The chosen icon, or null to let the card derive one from the setup.</param>
    /// <param name="accentColorArgb">The chosen tint, or null for none.</param>
    /// <remarks>
    /// The plus card starts from AUTO rather than from whatever the fans happen to be doing, so making a
    /// profile is a deliberate act with a known starting point. Capturing the live setup is the other
    /// entry point — the prompt that appears once the fans no longer match the selected profile.
    /// </remarks>
    public CoolingProfile CreateAutoSetup(string name, string? iconName = null, uint? accentColorArgb = null) => new()
    {
        Id = Guid.NewGuid().ToString("N"),
        Name = name,
        IconName = iconName,
        AccentColorArgb = accentColorArgb,
        Fans =
        [
            .. _fanControlStates.Values
                .Where(static state => state.IsAvailable)
                .OrderBy(static state => state.FanIndex)
                .Select(static state => new CoolingProfileFanEntry
                {
                    FanIndex = state.FanIndex,
                    Mode = FanControlMode.Auto,
                }),
        ],
    };

    /// <summary>Captures what every fan is doing right now as a new profile.</summary>
    /// <param name="name">What to call it.</param>
    /// <param name="iconName">The chosen icon, or null to let the card derive one from the setup.</param>
    /// <param name="accentColorArgb">The chosen tint, or null for none.</param>
    public CoolingProfile CaptureCurrentSetup(string name, string? iconName = null, uint? accentColorArgb = null) => new()
    {
        Id = Guid.NewGuid().ToString("N"),
        Name = name,
        IconName = iconName,
        AccentColorArgb = accentColorArgb,
        Fans =
        [
            .. _fanControlStates.Values
                .Where(static state => state.IsAvailable)
                .OrderBy(static state => state.FanIndex)
                .Select(static state => new CoolingProfileFanEntry
                {
                    FanIndex = state.FanIndex,
                    Mode = state.Mode,

                    // Every mode's settings are captured, not just the active one's, so re-saving a profile
                    // after switching one fan to Auto does not throw away the duty it had before.
                    DutyPercent = state.LastDutyPercent ?? 0d,
                    AdaptiveTargetCelsius = state.AdaptiveSettings.TargetTemperatureCelsius,

                    // The CURVE, not the slot it lives in. A profile that pointed at a slot would silently
                    // start meaning something else the next time that slot was edited.
                    CurvePoints = state.CustomCurvePoints,
                    Aggregation = state.DrivingTemperatureAggregation,
                }),
        ],
    };

    /// <summary>Formatting for the profile dialogs, so their summaries obey the user's chosen units too.</summary>
    public IUnitFormattingService UnitFormattingService => _unitFormattingService;

    public Task<CoolingProfileCommandResult> SaveProfileAsync(CoolingProfile profile)
        => _coolingProfileClient.SaveAsync(profile);

    // No manage dialog: renaming, editing and deleting are all on the cards themselves, so the shelf is the
    // whole interface and there is no second, parallel place to learn.

    /// <summary>Rebuilds the card list from the store, preserving which one reads as active.</summary>
    /// <summary>Rebuilds the cards from a change set on the service's library.</summary>
    /// <remarks>
    /// A full rebuild rather than a per-change edit: the list is three to a handful of items, it is rebuilt
    /// only when the library actually changes, and reconciling adds, removes and renames by hand would be a
    /// great deal of code guarding a collection small enough to redraw whole.
    /// </remarks>
    private void RefreshProfiles(IChangeSet<CoolingProfile, string> changes)
    {
        _profileLibrary.Edit(updater => updater.Clone(changes));

        _profiles.Clear();

        foreach (var profile in _profileLibrary.Items.OrderBy(static profile => profile.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            _profiles.Add(new FanProfileCardModel(profile, _unitFormattingService) { Owner = this });
        }

        // Last, so it reads as the end of the shelf rather than as the first thing on it.
        _profiles.Add(FanProfileCardModel.CreateAddCard(this, _unitFormattingService));

        RecomputeProfileSelection();
    }

    /// <summary>Raised when a card's button asks for something that needs a dialog.</summary>
    /// <remarks>
    /// An event rather than a direct call, because every one of these opens a ContentDialog and a dialog needs
    /// a XamlRoot — which the page has and a view model does not.
    /// </remarks>
    public event EventHandler<ProfileCardActionEventArgs>? ProfileActionRequested;

    /// <inheritdoc />
    public void RequestProfileAction(CoolingProfile? profile, ProfileCardAction action)
        => ProfileActionRequested?.Invoke(this, new ProfileCardActionEventArgs(profile, action));

    /// <summary>The profile the service is on, or null when nothing is selected.</summary>
    public CoolingProfile? ActiveProfile
    {
        get
        {
            var found = _activeProfileId is { } id ? _profileLibrary.Lookup(id) : default;
            return found.HasValue ? found.Value : null;
        }
    }

    /// <summary>
    /// Writes what the fans are doing right now into the selected profile.
    /// </summary>
    /// <remarks>
    /// This is how a profile becomes anything other than Auto: select it, change the fans by hand, then keep
    /// the result. Editing a profile's appearance deliberately cannot do this — a colour change must never
    /// rewrite behaviour — so saving the live setup is its own explicit act, offered only once the fans have
    /// actually stopped matching.
    /// </remarks>
    public async Task<CoolingProfileCommandResult> SaveCurrentSetupToActiveProfileAsync()
    {
        if (ActiveProfile is not { } profile)
        {
            return new CoolingProfileCommandResult(false, "No profile is selected.", []);
        }

        var captured = CaptureCurrentSetup(profile.Name);

        return await _coolingProfileClient
            .SaveAsync(profile with { Fans = captured.Fans })
            .ConfigureAwait(true);
    }

    public Task<CoolingProfileCommandResult> RenameProfileAsync(string profileId, string name)
        => _coolingProfileClient.RenameAsync(profileId, name);

    public Task<CoolingProfileCommandResult> DeleteProfileAsync(string profileId)
        => _coolingProfileClient.DeleteAsync(profileId);

    /// <summary>
    /// Which card is selected, and whether the fans still agree with it.
    /// </summary>
    /// <remarks>
    /// SELECTION comes from the service — it is the profile the user chose, and it survives restarts. Whether
    /// that profile is still in EFFECT is a separate question, answered here by comparing it against live fan
    /// state: change one fan by hand and the card stays selected while the page starts saying Modified, which
    /// is the honest description of what just happened.
    /// </remarks>
    private void RecomputeProfileSelection()
    {
        FanProfileCardModel? active = null;

        foreach (var card in _profiles)
        {
            var isActive = string.Equals(card.Id, _activeProfileId, StringComparison.Ordinal);
            card.IsSelected = isActive;

            if (isActive)
            {
                active = card;
            }
        }

        ActiveProfileName = active?.Name;

        // Only once fans have actually reported. Before that, "the fans do not match" is true but
        // meaningless, and showing Modified on a page that has not finished loading is just noise.
        IsModified = _fanControlStates.Count > 0
            && active is not null
            && !active.Profile.Matches(_fanControlStates);

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
        _subscriptions.Dispose();
        _profileLibrary.Dispose();
        foreach (var quickFan in _quickFans) quickFan.Detach();
    }
}
