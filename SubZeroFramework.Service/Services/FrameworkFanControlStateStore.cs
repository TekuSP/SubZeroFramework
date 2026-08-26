using System.Collections.Immutable;
using System.Reactive.Disposables;
using System.Reactive.Disposables.Fluent;

using DynamicData;

using Microsoft.Extensions.Options;

using SubZeroFramework.Models;
using SubZeroFramework.Service.Models;
using SubZeroFramework.Services;

namespace SubZeroFramework.Service.Services;

public sealed class FrameworkFanControlStateStore : IDisposable
{
    /// <summary>Maximum number of unique curve profile slots a single fan can store.</summary>
    public const int MaxCurveProfileSlots = 5;

    private readonly SourceCache<FanControlStateSnapshot, int> _fanControlStates = new(state => state.FanIndex);

    // Serializes every lookup -> mutate -> publish sequence. SourceCache.AddOrUpdate is individually
    // thread-safe, but two concurrent read-modify-writes (e.g. the curve worker's RecordAppliedDuty vs a
    // gRPC command like SetFanLink) can interleave so the later publish resurrects the earlier
    // lookup's stale fields, silently reverting a just-applied change (and persisting the reverted value).
    private readonly Lock _stateLock = new();
    private readonly CompositeDisposable _subscriptions = [];
    private readonly FrameworkFanControlSafetyTracker _fanControlSafetyTracker;
    private readonly IOptionsMonitor<FrameworkServiceOptions> _optionsMonitor;
    private readonly FanPreviewWatchdog? _previewWatchdog;
    private readonly ILogger<FrameworkFanControlStateStore> _logger;
    private bool _disposed;

    public FrameworkFanControlStateStore(
        IFrameworkDataProvider frameworkDataProvider,
        FrameworkFanControlSafetyTracker fanControlSafetyTracker,
        IOptionsMonitor<FrameworkServiceOptions> optionsMonitor,
        ILogger<FrameworkFanControlStateStore> logger,
        FanPreviewWatchdog? previewWatchdog = null)
    {
        ArgumentNullException.ThrowIfNull(frameworkDataProvider);
        ArgumentNullException.ThrowIfNull(fanControlSafetyTracker);
        ArgumentNullException.ThrowIfNull(optionsMonitor);

        _fanControlSafetyTracker = fanControlSafetyTracker;
        _optionsMonitor = optionsMonitor;
        // Optional so existing tests can construct the store without one; a null watchdog simply means no
        // fan is ever considered to be previewing.
        _previewWatchdog = previewWatchdog;
        _logger = logger;

        frameworkDataProvider
            .ConnectFanStates()
            .Subscribe(
                ApplyFanStateChanges,
                exception => _logger.LogError(exception, "The fan state stream faulted inside the fan control state store."))
            .DisposeWith(_subscriptions);

        Action<int> safetyStateChanged = ApplySafetyStateChange;
        _fanControlSafetyTracker.SafetyStateChanged += safetyStateChanged;
        Disposable.Create(() => _fanControlSafetyTracker.SafetyStateChanged -= safetyStateChanged)
            .DisposeWith(_subscriptions);

        var optionsSubscription = _optionsMonitor.OnChange(_ =>
        {
            _logger.LogInformation("Applying configured fan control states after service option changes.");
            ApplyConfiguredStates();
        });
        if (optionsSubscription is not null)
        {
            optionsSubscription.DisposeWith(_subscriptions);
        }

        _logger.LogInformation("Initialized the fan control state store.");
        ApplyConfiguredStates();
    }

    public IObservable<IChangeSet<FanControlStateSnapshot, int>> Connect()
        => _fanControlStates.Connect();

    /// <summary>Returns the current control-state snapshot for a fan, or null if the fan is unknown.</summary>
    public FanControlStateSnapshot? GetState(int fanIndex)
    {
        var lookup = _fanControlStates.Lookup(fanIndex);
        return lookup.HasValue ? lookup.Value : null;
    }

    /// <summary>
    /// Re-publishes a previously captured snapshot (used by the preview watchdog to revert a fan's in-memory
    /// state to what it was before an uncommitted preview). Non-curve modes still need an explicit EC
    /// actuation by the caller; a curve snapshot is re-actuated by the curve worker once republished.
    /// </summary>
    public void RestoreState(FanControlStateSnapshot state)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(state);

        lock (_stateLock)
        {
            PublishState(ApplySafetyState(NormalizeProfiles(state) with { ObservedAt = DateTimeOffset.UtcNow }), "preview revert");
        }
    }

    public void MarkManual(int fanIndex)
    {
        ThrowIfDisposed();
        UpsertState(
            fanIndex,
            existing => existing with
            {
                Mode = FanControlMode.Manual,
                ObservedAt = DateTimeOffset.UtcNow,
                CustomCurvePoints = ImmutableSortedDictionary<int, double>.Empty,
                DrivingSensorIndices = [],
                LastDutyPercent = null,
            },
            "manual command");
    }

    /// <summary>
    /// Publishes the latest adaptive controller tick for a fan, or clears it when the fan is not adaptively
    /// driven.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Called every evaluation for every adaptive fan, so it is deliberately a no-op when nothing changed:
    /// republishing an identical decision would push a stream update per fan per second to every connected
    /// client for no benefit. Reference equality on the record is enough — the worker builds a new decision
    /// object each tick, so equal values mean genuinely nothing moved.
    /// </para>
    /// <para>
    /// This is live telemetry and is NOT persisted. What IS persisted is the learned model inside it, which
    /// the caller extracts and saves on its own schedule; writing the file every second would be absurd.
    /// </para>
    /// </remarks>
    /// <param name="fanIndex">The fan.</param>
    /// <param name="decision">The tick, or null to clear.</param>
    public void RecordAdaptiveControl(int fanIndex, AdaptiveControlDecision? decision)
    {
        ThrowIfDisposed();

        lock (_stateLock)
        {
            var lookup = _fanControlStates.Lookup(fanIndex);
            if (!lookup.HasValue || lookup.Value.AdaptiveControl == decision)
            {
                return;
            }

            PublishState(lookup.Value with { AdaptiveControl = decision, ObservedAt = DateTimeOffset.UtcNow }, "adaptive control update");
        }
    }

    /// <summary>
    /// Stores what a fan's controller has learned, so it survives a restart.
    /// </summary>
    /// <param name="fanIndex">The fan.</param>
    /// <param name="learning">The learned state.</param>
    /// <returns>True when the fan is known and the state changed.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="learning"/> is null.</exception>
    public bool RecordAdaptiveLearning(int fanIndex, AdaptiveLearningState learning)
    {
        ArgumentNullException.ThrowIfNull(learning);
        ThrowIfDisposed();

        lock (_stateLock)
        {
            var lookup = _fanControlStates.Lookup(fanIndex);
            if (!lookup.HasValue || lookup.Value.AdaptiveLearning == learning)
            {
                return false;
            }

            PublishState(lookup.Value with { AdaptiveLearning = learning }, "adaptive learning update");
            return true;
        }
    }

    /// <summary>
    /// Arms a fan into <see cref="FanControlMode.Adaptive"/>.
    /// </summary>
    /// <param name="fanIndex">The fan.</param>
    /// <param name="drivingSensorIndices">The sensors whose aggregate temperature the loop holds.</param>
    /// <param name="aggregation">How to combine them.</param>
    /// <param name="settings">New target and floor, or null to keep whatever the fan already had.</param>
    /// <returns>Whether the fan was armed, with a message explaining a refusal.</returns>
    /// <remarks>
    /// <b>Deliberately not gated on calibration.</b> An uncalibrated fan arms on the conservative bootstrap
    /// model and learns from ordinary use; refusing to arm would leave it on firmware control, which is the
    /// worse outcome of the two. What Adaptive genuinely cannot do without is a driving sensor — with nothing
    /// to measure there is no loop to close, so that is the one refusal here.
    /// </remarks>
    public FanControlStoreResult SetAdaptiveMode(
        int fanIndex,
        IReadOnlyCollection<int> drivingSensorIndices,
        TemperatureAggregationMode aggregation,
        AdaptiveFanSettings? settings)
    {
        ArgumentNullException.ThrowIfNull(drivingSensorIndices);
        ThrowIfDisposed();

        lock (_stateLock)
        {
            var lookup = _fanControlStates.Lookup(fanIndex);
            if (!lookup.HasValue)
            {
                return FanControlStoreResult.Failed($"Unknown fan {fanIndex}.");
            }

            if (drivingSensorIndices.Count == 0)
            {
                return FanControlStoreResult.Failed("Adaptive needs at least one driving temperature sensor.");
            }

            // Nothing is known about this fan: never measured, and nothing learned from use. Calibration is
            // the door into Adaptive — once through it, ordinary use keeps refining the model forever after.
            //
            // The check is "measured OR learned", not "calibrated", so a fan that has built its own model
            // stays armed after a recalibration is discarded. What locks it out is genuine ignorance: a fresh
            // install, or a fan whose learning was just thrown away without a measurement behind it.
            if (!lookup.Value.Calibration.IsMeasured && !lookup.Value.AdaptiveLearning.HasLearned)
            {
                return FanControlStoreResult.Failed(
                    "Adaptive needs to learn this fan first. Run the learning test to measure how it moves heat.");
            }

            PublishState(
                lookup.Value with
                {
                    Mode = FanControlMode.Adaptive,
                    DrivingSensorIndices = [.. drivingSensorIndices.Distinct().Order()],
                    DrivingTemperatureAggregation = aggregation,
                    AdaptiveSettings = (settings ?? lookup.Value.AdaptiveSettings).Sanitized(),
                    ObservedAt = DateTimeOffset.UtcNow,
                },
                "adaptive mode command");

            return FanControlStoreResult.Ok;
        }
    }

    /// <summary>
    /// Updates a fan's Adaptive target and safety floor without changing its mode.
    /// </summary>
    /// <param name="fanIndex">The fan.</param>
    /// <param name="settings">The new settings; clamped before storage.</param>
    /// <returns>False when the fan is unknown.</returns>
    /// <remarks>
    /// Deliberately does NOT require the fan to be in Adaptive: the settings are per-fan and survive mode
    /// switches, so a user can set a target before arming the mode, and keep it after leaving.
    /// </remarks>
    public bool SetAdaptiveSettings(int fanIndex, AdaptiveFanSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ThrowIfDisposed();

        var sanitized = settings.Sanitized();

        lock (_stateLock)
        {
            var lookup = _fanControlStates.Lookup(fanIndex);
            if (!lookup.HasValue)
            {
                return false;
            }

            if (lookup.Value.AdaptiveSettings == sanitized)
            {
                return true;
            }

            PublishState(lookup.Value with { AdaptiveSettings = sanitized, ObservedAt = DateTimeOffset.UtcNow }, "adaptive settings change");
            return true;
        }
    }

    /// <summary>
    /// Stores a completed calibration for a fan.
    /// </summary>
    /// <param name="fanIndex">The fan.</param>
    /// <param name="calibration">The learned model.</param>
    /// <returns>False when the fan is unknown.</returns>
    /// <remarks>
    /// Storing a calibration also clears what was learned online: the previous refinement was measured around
    /// the OLD model, and a fresh hot test is a controlled measurement that supersedes it.
    /// </remarks>
    public bool SetCalibration(int fanIndex, FanCalibrationSnapshot calibration)
    {
        ArgumentNullException.ThrowIfNull(calibration);
        ThrowIfDisposed();

        lock (_stateLock)
        {
            var lookup = _fanControlStates.Lookup(fanIndex);
            if (!lookup.HasValue)
            {
                return false;
            }

            PublishState(
                lookup.Value with
                {
                    Calibration = calibration,
                    AdaptiveLearning = AdaptiveLearningState.None,
                    ObservedAt = DateTimeOffset.UtcNow,
                },
                "calibration stored");

            return true;
        }
    }

    /// <summary>
    /// Discards what a fan identified from ordinary use, returning it to its calibration or to the bootstrap.
    /// </summary>
    /// <param name="fanIndex">The fan.</param>
    /// <returns>False when the fan is unknown.</returns>
    /// <remarks>
    /// For a machine that changed physically — a repaste, a new heatsink, a cleaned vent — where the
    /// identified model describes hardware that no longer exists and would otherwise take a long time to
    /// forget on its own. The worker drops the running controller on the next tick, so the estimator restarts
    /// empty rather than resuming from what was just discarded.
    /// </remarks>
    public bool ForgetAdaptiveLearning(int fanIndex)
    {
        ThrowIfDisposed();

        lock (_stateLock)
        {
            var lookup = _fanControlStates.Lookup(fanIndex);
            if (!lookup.HasValue)
            {
                return false;
            }

            PublishState(
                lookup.Value with { AdaptiveLearning = AdaptiveLearningState.None, ObservedAt = DateTimeOffset.UtcNow },
                "adaptive learning forgotten");

            return true;
        }
    }

    public void RecordAppliedDuty(int fanIndex, double dutyPercent)
    {
        ThrowIfDisposed();
        UpsertState(
            fanIndex,
            existing => existing with
            {
                ObservedAt = DateTimeOffset.UtcNow,
                LastDutyPercent = dutyPercent,
            },
            "applied duty update");
    }

    /// <summary>
    /// Forgets the duty last written to a fan, without touching its mode, curve or driving sensors.
    /// </summary>
    /// <remarks>
    /// Used when the service stops driving a curve fan but the profile stays exactly as the user saved it —
    /// the firmware-safe fallback, where no driving sensor can be read. The last duty is then a fact about a
    /// command nobody is issuing any more: leaving it in place reports a speed the fan is not being held at.
    /// Deliberately NOT <see cref="MarkAuto"/>, which would wipe the profile itself.
    /// </remarks>
    public void ClearAppliedDuty(int fanIndex)
    {
        ThrowIfDisposed();
        UpsertState(
            fanIndex,
            existing => existing with
            {
                ObservedAt = DateTimeOffset.UtcNow,
                LastDutyPercent = null,
            },
            "applied duty cleared");
    }

    public void MarkAuto(int fanIndex)
    {
        ThrowIfDisposed();
        UpsertState(
            fanIndex,
            existing => existing with
            {
                Mode = FanControlMode.Auto,
                ObservedAt = DateTimeOffset.UtcNow,
                CustomCurvePoints = ImmutableSortedDictionary<int, double>.Empty,
                DrivingSensorIndices = [],
                LastDutyPercent = null,
            },
            "automatic restore");
    }

    public void MarkMax(int fanIndex)
    {
        ThrowIfDisposed();
        UpsertState(
            fanIndex,
            existing => existing with
            {
                Mode = FanControlMode.Max,
                ObservedAt = DateTimeOffset.UtcNow,
                CustomCurvePoints = ImmutableSortedDictionary<int, double>.Empty,
                DrivingSensorIndices = [],
                LastDutyPercent = null,
            },
            "max command");
    }

    /// <summary>Legacy single-curve entry point: saves into the active slot and activates curve mode.</summary>
    public void SetCustomCurve(int fanIndex, IReadOnlyDictionary<int, double> customCurvePoints, TemperatureAggregationMode aggregationMode, IReadOnlyCollection<int> drivingSensorIndices, bool treatMissingSensorsAsZero = false)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(customCurvePoints);
        ArgumentNullException.ThrowIfNull(drivingSensorIndices);

        var lookup = _fanControlStates.Lookup(fanIndex);
        var activeSlot = lookup.HasValue ? Math.Clamp(lookup.Value.ActiveCurveSlot, 0, MaxCurveProfileSlots - 1) : 0;
        var name = lookup.HasValue ? lookup.Value.CurveProfiles.ElementAtOrDefault(activeSlot)?.Name : null;

        SaveCurveProfile(fanIndex, activeSlot, name, customCurvePoints, aggregationMode, drivingSensorIndices, followFanIndex: null, activate: true, treatMissingSensorsAsZero);
    }

    /// <summary>Saves (or overwrites) one curve profile slot, optionally activating it.</summary>
    public void SaveCurveProfile(
        int fanIndex,
        int slot,
        string? name,
        IReadOnlyDictionary<int, double> curvePoints,
        TemperatureAggregationMode aggregationMode,
        IReadOnlyCollection<int> drivingSensorIndices,
        int? followFanIndex,
        bool activate,
        bool treatMissingSensorsAsZero = false)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(curvePoints);
        ArgumentNullException.ThrowIfNull(drivingSensorIndices);
        ArgumentOutOfRangeException.ThrowIfNegative(slot);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(slot, MaxCurveProfileSlots);

        UpsertState(
            fanIndex,
            existing =>
            {
                var normalized = NormalizeProfiles(existing);
                var profile = new FanCurveProfileSnapshot
                {
                    Slot = slot,
                    Name = name,
                    IsConfigured = true,
                    CurvePoints = curvePoints.Count == 0
                        ? ImmutableSortedDictionary<int, double>.Empty
                        : curvePoints.ToImmutableSortedDictionary(pair => pair.Key, pair => pair.Value),
                    DrivingTemperatureAggregation = aggregationMode,
                    DrivingSensorIndices = [.. drivingSensorIndices],
                    FollowFanIndex = followFanIndex,
                    TreatMissingSensorsAsZero = treatMissingSensorsAsZero,
                };

                var next = normalized with
                {
                    CurveProfiles = normalized.CurveProfiles.SetItem(slot, profile),
                    ObservedAt = DateTimeOffset.UtcNow,
                };

                if (activate)
                {
                    next = next with { Mode = FanControlMode.CustomCurve, ActiveCurveSlot = slot, LastDutyPercent = null };
                }

                return SyncActiveCurveFields(next);
            },
            "save curve profile");
    }

    /// <summary>Activates a curve profile slot (switches the fan into curve mode driven by that slot).</summary>
    public void SetActiveCurveProfile(int fanIndex, int slot)
    {
        ThrowIfDisposed();
        ArgumentOutOfRangeException.ThrowIfNegative(slot);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(slot, MaxCurveProfileSlots);

        UpsertState(
            fanIndex,
            existing =>
            {
                var normalized = NormalizeProfiles(existing);
                var next = normalized with
                {
                    Mode = FanControlMode.CustomCurve,
                    ActiveCurveSlot = slot,
                    LastDutyPercent = null,
                    ObservedAt = DateTimeOffset.UtcNow,
                };

                return SyncActiveCurveFields(next);
            },
            "set active curve profile");
    }

    /// <summary>Clears one curve profile slot back to an empty, unconfigured state.</summary>
    public void ClearCurveProfile(int fanIndex, int slot)
    {
        ThrowIfDisposed();
        ArgumentOutOfRangeException.ThrowIfNegative(slot);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(slot, MaxCurveProfileSlots);

        UpsertState(
            fanIndex,
            existing =>
            {
                var normalized = NormalizeProfiles(existing);
                var cleared = new FanCurveProfileSnapshot { Slot = slot, IsConfigured = false };
                var next = normalized with
                {
                    CurveProfiles = normalized.CurveProfiles.SetItem(slot, cleared),
                    ObservedAt = DateTimeOffset.UtcNow,
                };

                return SyncActiveCurveFields(next);
            },
            "clear curve profile");
    }

    /// <summary>Every fan index the store tracks (live fans, plus any materialized by a command), ascending.</summary>
    public ImmutableArray<int> GetKnownFanIndices()
    {
        lock (_stateLock)
        {
            return [.. _fanControlStates.Keys.Order()];
        }
    }

    /// <summary>
    /// Wipes every fan back to a fresh-install control state: Auto mode, no curve profiles, slot 0 active, no
    /// "applies to" link, no remembered manual duty. Live telemetry fields (display
    /// name, availability) and the safety overlay are preserved — the fan still exists, only its settings are
    /// gone. In-memory only: the caller clears the persisted copy and restores the EC. Returns the fans reset.
    /// </summary>
    /// <remarks>
    /// Publishes an UPSERT per fan rather than removing the entries: a Remove reaches clients as an
    /// "unavailable" update that keeps the last known profiles, so the UI would keep showing slots that no
    /// longer exist. An upsert carrying empty profiles reconciles correctly everywhere.
    /// </remarks>
    public ImmutableArray<int> ResetAllToFactoryDefaults()
    {
        ThrowIfDisposed();

        var fanIndices = GetKnownFanIndices();
        foreach (var fanIndex in fanIndices)
        {
            UpsertState(
                fanIndex,
                static existing => existing with
                {
                    Mode = FanControlMode.Auto,
                    CustomCurvePoints = ImmutableSortedDictionary<int, double>.Empty,
                    DrivingTemperatureAggregation = TemperatureAggregationMode.Maximum,
                    DrivingSensorIndices = [],
                    ActiveCurveSlot = 0,
                    CurveProfiles = CreateEmptyProfiles(),
                    LinkedLeaderIndex = null,
                    LastDutyPercent = null,

                    // A factory reset deletes every saved fan setting, and the calibration is one — the user
                    // asked for the machine as it shipped, and keeping a learned model would leave Adaptive
                    // armable against a model they just asked to be rid of.
                    Calibration = FanCalibrationSnapshot.None,
                    AdaptiveSettings = AdaptiveFanSettings.Default,
                    AdaptiveLearning = AdaptiveLearningState.None,
                    AdaptiveControl = null,
                    ObservedAt = DateTimeOffset.UtcNow,
                },
                "factory reset");
        }

        _logger.LogInformation("Reset {FanCount} fan control state(s) to factory defaults in memory.", fanIndices.Length);
        return fanIndices;
    }

    /// <summary>Builds a persistable options snapshot of a fan's profiles, or null if the fan is unknown.</summary>
    public FanControlStateOptions? BuildFanControlOptions(int fanIndex)
    {
        FanControlStateSnapshot state;
        lock (_stateLock)
        {
            var lookup = _fanControlStates.Lookup(fanIndex);
            if (!lookup.HasValue)
            {
                return null;
            }

            state = lookup.Value;
        }

        state = NormalizeProfiles(state);
        return new FanControlStateOptions
        {
            FanIndex = fanIndex,
            Mode = state.Mode,
            // The live top-level driving fields — for a curve fan they mirror the active slot, but for an
            // ADAPTIVE fan they are the only record of which sensors the loop holds. Leaving them out once
            // meant a service restart restored Mode=Adaptive with zero sensors, which the worker cannot
            // drive — the user applied Adaptive and came back to a fan behaving as Auto.
            DrivingTemperatureAggregation = state.DrivingTemperatureAggregation,
            DrivingSensorIndices = [.. state.DrivingSensorIndices],
            ActiveCurveSlot = state.ActiveCurveSlot,
            CurveProfiles =
            [
                .. state.CurveProfiles
                    .Where(static profile => profile.IsConfigured)
                    .Select(static profile => new FanCurveProfileOptions
                    {
                        Slot = profile.Slot,
                        Name = profile.Name,
                        CurvePoints = profile.CurvePoints.ToDictionary(static kv => kv.Key, static kv => kv.Value),
                        DrivingTemperatureAggregation = profile.DrivingTemperatureAggregation,
                        DrivingSensorIndices = [.. profile.DrivingSensorIndices],
                        FollowFanIndex = profile.FollowFanIndex,
                        TreatMissingSensorsAsZero = profile.TreatMissingSensorsAsZero,
                    }),
            ],
            LinkedLeaderIndex = state.LinkedLeaderIndex,
            Calibration = ToCalibrationOptions(state.Calibration),
            AdaptiveSettings = new AdaptiveFanSettingsOptions
            {
                TargetTemperatureCelsius = state.AdaptiveSettings.TargetTemperatureCelsius,
                SafetyFloorEnabled = state.AdaptiveSettings.SafetyFloorEnabled,
                SafetyFloorPercent = state.AdaptiveSettings.SafetyFloorPercent,
                LambdaSeconds = state.AdaptiveSettings.LambdaSeconds,
            },
            AdaptiveLearning = state.AdaptiveLearning.HasLearned
                ? new AdaptiveLearningOptions
                {
                    FeedForwardDutyPerWatt = state.AdaptiveLearning.FeedForwardDutyPerWatt,
                    CalibratedAnchorDutyPerWatt = state.AdaptiveLearning.CalibratedAnchorDutyPerWatt,
                    IdentifiedProcessGainCelsiusPerPercent = state.AdaptiveLearning.IdentifiedProcessGainCelsiusPerPercent,
                    IdentifiedCelsiusPerWatt = state.AdaptiveLearning.IdentifiedCelsiusPerWatt,
                    IdentifiedInterceptCelsius = state.AdaptiveLearning.IdentifiedInterceptCelsius,
                    ObservationCount = state.AdaptiveLearning.ObservationCount,
                    LastUpdatedAt = state.AdaptiveLearning.LastUpdatedAt,
                    LastMaterialChangeAt = state.AdaptiveLearning.LastMaterialChangeAt,
                    ThermalLoadSource = state.AdaptiveLearning.ThermalLoadSource,
                }
                : null,
        };
    }

    private static FanCalibrationOptions? ToCalibrationOptions(FanCalibrationSnapshot calibration)
        => calibration.State == FanCalibrationState.None
            ? null
            : new FanCalibrationOptions
            {
                State = calibration.State,
                CalibratedAt = calibration.CalibratedAt,
                ProcessGainCelsiusPerPercent = calibration.ProcessGainCelsiusPerPercent,
                TimeConstantSeconds = calibration.TimeConstantSeconds,
                DeadTimeSeconds = calibration.DeadTimeSeconds,
                MinimumSpinRpm = calibration.MinimumSpinRpm,
                MinimumSpinDutyPercent = calibration.MinimumSpinDutyPercent,
                MaximumRpm = calibration.MaximumRpm,
                ProportionalGain = calibration.ProportionalGain,
                IntegralGain = calibration.IntegralGain,
                FeedForwardDutyPerWatt = calibration.FeedForwardDutyPerWatt,
                TrackingMode = calibration.TrackingMode,
            };

    private static FanCalibrationSnapshot ToCalibrationSnapshot(FanCalibrationOptions? options)
        => options is null
            ? FanCalibrationSnapshot.None
            : new FanCalibrationSnapshot
            {
                State = options.State,
                CalibratedAt = options.CalibratedAt,
                ProcessGainCelsiusPerPercent = options.ProcessGainCelsiusPerPercent,
                TimeConstantSeconds = options.TimeConstantSeconds,
                DeadTimeSeconds = options.DeadTimeSeconds,
                MinimumSpinRpm = options.MinimumSpinRpm,
                MinimumSpinDutyPercent = options.MinimumSpinDutyPercent,
                MaximumRpm = options.MaximumRpm,
                ProportionalGain = options.ProportionalGain,
                IntegralGain = options.IntegralGain,
                FeedForwardDutyPerWatt = options.FeedForwardDutyPerWatt,
                TrackingMode = options.TrackingMode,
            };

    /// <summary>
    /// Sets (or clears, when <paramref name="leaderIndex"/> is null) which fan this one is grouped under for the
    /// "Applies to" link. Updates the in-memory snapshot and streams the change; the caller persists it. Returns
    /// false when the fan is unknown.
    /// </summary>
    public bool SetLinkedLeader(int fanIndex, int? leaderIndex)
    {
        var lookup = _fanControlStates.Lookup(fanIndex);
        if (!lookup.HasValue)
        {
            return false;
        }

        // A fan cannot be grouped under itself.
        var normalizedLeader = leaderIndex == fanIndex ? null : leaderIndex;

        lock (_stateLock)
        {
            lookup = _fanControlStates.Lookup(fanIndex);
            if (!lookup.HasValue)
            {
                return false;
            }

            if (lookup.Value.LinkedLeaderIndex == normalizedLeader)
            {
                return true;
            }

            PublishState(lookup.Value with { LinkedLeaderIndex = normalizedLeader }, "fan link change");
        }

        return true;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _logger.LogInformation("Disposing the fan control state store.");
        _subscriptions.Dispose();
        _fanControlStates.Dispose();
        _disposed = true;
    }

    /// <summary>
    /// Re-seeds live fan state from the persisted configuration.
    /// </summary>
    /// <remarks>
    /// Runs at startup AND on every configuration reload — and the service watches the very file it writes, so
    /// any persisting command (or a Settings save) re-enters here for EVERY fan, not just the one that changed.
    /// That made the persisted file behave as a live authority, which is exactly what the per-tick path
    /// refuses to do for the same reason (see the comment on the telemetry overlay): it re-asserted stale
    /// persisted state over whatever a fan was actually doing.
    ///
    /// Two guards make the re-entry harmless:
    /// <list type="bullet">
    /// <item>A fan with an OPEN PREVIEW HOLD is skipped. A preview is deliberately unpersisted volatile state;
    /// overlaying the persisted mode on top of it reverted what the user was in the middle of testing, and
    /// for a fan persisted as Auto it left the EC holding the preview duty — possibly a stopped fan — while
    /// every client reported Auto.</item>
    /// </list>
    ///
    /// A no-op check was tried here and removed: <see cref="FanControlStateSnapshot"/> is a record, but its
    /// <c>CurveProfiles</c> is an <see cref="ImmutableArray{T}"/>, whose equality compares the underlying
    /// array REFERENCE rather than its contents. The overlay rebuilds that array on every call, so an
    /// unchanged fan never compares equal and the check silently never fired. Skipping the republish would
    /// need a deep comparison the snapshot types do not offer; the redundant notifications are cheap and
    /// idempotent, so they are left alone rather than papered over with a comparison that looks right and
    /// does nothing.
    /// </remarks>
    private void ApplyConfiguredStates()
    {
        var optionsByFanIndex = _optionsMonitor.CurrentValue.FanControlStates
            .ToDictionary(option => option.FanIndex);

        _logger.LogDebug("Applying configured fan control state overlays for {ConfiguredFanCount} configured fan(s).", optionsByFanIndex.Count);

        lock (_stateLock)
        {
            foreach (var existingState in _fanControlStates.Items.ToArray())
            {
                if (!optionsByFanIndex.TryGetValue(existingState.FanIndex, out var configuredState))
                {
                    continue;
                }

                if (_previewWatchdog?.HasOpenHold(existingState.FanIndex) == true)
                {
                    _logger.LogDebug(
                        "Skipping the configured overlay for fan {FanIndex} because a preview hold is open; its live preview state stands until the preview is applied or reverted.",
                        existingState.FanIndex);
                    continue;
                }

                PublishState(ApplySafetyState(ApplyConfiguredState(existingState, configuredState)), "configured state refresh");
            }
        }
    }

    private void ApplyFanStateChanges(IChangeSet<FanStateSnapshot, int> changes)
    {
        var optionsByFanIndex = _optionsMonitor.CurrentValue.FanControlStates
            .ToDictionary(option => option.FanIndex);

        foreach (var change in changes)
        {
            if (change.Reason == ChangeReason.Remove)
            {
                lock (_stateLock)
                {
                    RemoveState(change.Key, "fan state removal");
                }

                continue;
            }

            lock (_stateLock)
            {
                var currentLookup = _fanControlStates.Lookup(change.Key);
                FanControlStateSnapshot updated;
                if (currentLookup.HasValue)
                {
                    // Already-tracked fan: a telemetry tick only refreshes live fields. The mode / curve / link are
                    // owned by commands at runtime — the persisted config is a startup seed, NOT a per-tick authority.
                    // Re-applying the config overlay here every poll would clobber a just-issued command (e.g. Max)
                    // back to the stale persisted Mode before/while it persists, so the command never sticks.
                    updated = currentLookup.Value with
                    {
                        DisplayName = change.Current.DisplayName,

                        // A live field like the name: it comes from the hardware, not from a command, so a
                        // module swap that changes what a fan cools is picked up on the next tick.
                        CoolingRole = change.Current.CoolingRole,
                        ObservedAt = change.Current.ObservedAt,
                        IsAvailable = change.Current.IsAvailable,
                    };
                }
                else
                {
                    // First time we see this fan: seed it from the persisted configured state (if any).
                    var seed = new FanControlStateSnapshot
                    {
                        FanIndex = change.Key,
                        DisplayName = change.Current.DisplayName,
                        CoolingRole = change.Current.CoolingRole,
                        Mode = FanControlMode.Auto,
                        DrivingTemperatureAggregation = TemperatureAggregationMode.Maximum,
                        DrivingSensorIndices = [],
                        ObservedAt = change.Current.ObservedAt,
                        IsAvailable = change.Current.IsAvailable,
                    };

                    updated = optionsByFanIndex.TryGetValue(change.Key, out var configuredState)
                        ? ApplyConfiguredState(seed, configuredState)
                        : seed;
                }

                PublishState(ApplySafetyState(updated), currentLookup.HasValue ? "fan state update" : "fan state initialization");
            }
        }
    }

    private void ApplySafetyStateChange(int fanIndex)
    {
        if (_disposed)
        {
            return;
        }

        UpsertState(
            fanIndex,
            existing => ApplySafetyState(existing with
            {
                ObservedAt = DateTimeOffset.UtcNow,
            }),
            "safety state change");
    }

    private FanControlStateSnapshot ApplySafetyState(FanControlStateSnapshot state)
    {
        var safetyState = _fanControlSafetyTracker.GetState(state.FanIndex);

        return state with
        {
            HasActiveOverride = safetyState.HasActiveOverride,
            LastAutoRestoreAttemptFailed = safetyState.LastAutoRestoreAttemptFailed,
            LastAutoRestoreAttemptAt = safetyState.LastAutoRestoreAttemptAt,
            LastAutoRestoreError = safetyState.LastAutoRestoreError,
        };
    }

    private static FanControlStateSnapshot ApplyConfiguredState(FanControlStateSnapshot state, FanControlStateOptions configuredState)
    {
        // Adaptive persisted without sensors (a config written before the sensors were persisted) cannot be
        // driven — the worker would sit at NotDriven forever while the UI claimed the loop was armed. Auto
        // is the honest restore: the fan is under firmware control either way, and re-applying Adaptive
        // re-picks the sensors.
        var mode = configuredState.Mode == FanControlMode.Adaptive && configuredState.DrivingSensorIndices.Length == 0
            ? FanControlMode.Auto
            : configuredState.Mode;

        var next = state with
        {
            Mode = mode,
            DrivingTemperatureAggregation = configuredState.DrivingTemperatureAggregation,
            DrivingSensorIndices = [.. configuredState.DrivingSensorIndices],
            ActiveCurveSlot = Math.Clamp(configuredState.ActiveCurveSlot, 0, MaxCurveProfileSlots - 1),
            CurveProfiles = BuildProfilesFromOptions(configuredState),
            LinkedLeaderIndex = configuredState.LinkedLeaderIndex,
            Calibration = ToCalibrationSnapshot(configuredState.Calibration),
            AdaptiveSettings = configuredState.AdaptiveSettings is { } adaptiveSettings
                ? new AdaptiveFanSettings
                {
                    TargetTemperatureCelsius = adaptiveSettings.TargetTemperatureCelsius,
                    SafetyFloorEnabled = adaptiveSettings.SafetyFloorEnabled,
                    SafetyFloorPercent = adaptiveSettings.SafetyFloorPercent,
                    LambdaSeconds = adaptiveSettings.LambdaSeconds,
                }.Sanitized()
                : AdaptiveFanSettings.Default,
            AdaptiveLearning = configuredState.AdaptiveLearning is { FeedForwardDutyPerWatt: not null } learning
                ? new AdaptiveLearningState
                {
                    FeedForwardDutyPerWatt = learning.FeedForwardDutyPerWatt,
                    CalibratedAnchorDutyPerWatt = learning.CalibratedAnchorDutyPerWatt,
                    IdentifiedProcessGainCelsiusPerPercent = learning.IdentifiedProcessGainCelsiusPerPercent,
                    IdentifiedCelsiusPerWatt = learning.IdentifiedCelsiusPerWatt,
                    IdentifiedInterceptCelsius = learning.IdentifiedInterceptCelsius,
                    ObservationCount = learning.ObservationCount,
                    LastUpdatedAt = learning.LastUpdatedAt,
                    LastMaterialChangeAt = learning.LastMaterialChangeAt,
                    ThermalLoadSource = learning.ThermalLoadSource,
                }
                : AdaptiveLearningState.None,
        };

        return SyncActiveCurveFields(NormalizeProfiles(next));
    }

    private static ImmutableArray<FanCurveProfileSnapshot> BuildProfilesFromOptions(FanControlStateOptions options)
    {
        var slots = new FanCurveProfileSnapshot[MaxCurveProfileSlots];
        for (var i = 0; i < MaxCurveProfileSlots; i++)
        {
            slots[i] = new FanCurveProfileSnapshot { Slot = i, IsConfigured = false };
        }

        if (options.CurveProfiles is { Length: > 0 })
        {
            foreach (var profile in options.CurveProfiles)
            {
                if (profile.Slot is < 0 or >= MaxCurveProfileSlots)
                {
                    continue;
                }

                slots[profile.Slot] = new FanCurveProfileSnapshot
                {
                    Slot = profile.Slot,
                    Name = profile.Name,
                    IsConfigured = true,
                    CurvePoints = profile.CurvePoints.Count == 0
                        ? ImmutableSortedDictionary<int, double>.Empty
                        : profile.CurvePoints.ToImmutableSortedDictionary(pair => pair.Key, pair => pair.Value),
                    DrivingTemperatureAggregation = profile.DrivingTemperatureAggregation,
                    DrivingSensorIndices = [.. profile.DrivingSensorIndices],
                    FollowFanIndex = profile.FollowFanIndex,
                    TreatMissingSensorsAsZero = profile.TreatMissingSensorsAsZero,
                };
            }
        }
        else if (options.Mode == FanControlMode.CustomCurve && options.CustomCurvePoints.Count > 0)
        {
            // Legacy migration: fold a single persisted curve into slot 0 so older configs keep working.
            slots[0] = new FanCurveProfileSnapshot
            {
                Slot = 0,
                IsConfigured = true,
                CurvePoints = options.CustomCurvePoints.ToImmutableSortedDictionary(pair => pair.Key, pair => pair.Value),
                DrivingTemperatureAggregation = options.DrivingTemperatureAggregation,
                DrivingSensorIndices = [.. options.DrivingSensorIndices],
            };
        }

        return [.. slots];
    }

    private static ImmutableArray<FanCurveProfileSnapshot> CreateEmptyProfiles()
        => [.. Enumerable.Range(0, MaxCurveProfileSlots).Select(static slot => new FanCurveProfileSnapshot { Slot = slot, IsConfigured = false })];

    // Guarantees the snapshot always carries exactly five slots (0..4) and an in-range active slot.
    private static FanControlStateSnapshot NormalizeProfiles(FanControlStateSnapshot state)
    {
        var profiles = state.CurveProfiles;
        if (profiles.IsDefaultOrEmpty)
        {
            profiles = CreateEmptyProfiles();
        }
        else if (profiles.Length != MaxCurveProfileSlots || profiles.Where((p, i) => p.Slot != i).Any())
        {
            var bySlot = profiles
                .Where(static p => p.Slot is >= 0 and < MaxCurveProfileSlots)
                .GroupBy(static p => p.Slot)
                .ToDictionary(static g => g.Key, static g => g.Last());
            profiles =
            [
                .. Enumerable.Range(0, MaxCurveProfileSlots)
                    .Select(slot => bySlot.TryGetValue(slot, out var p) ? p : new FanCurveProfileSnapshot { Slot = slot, IsConfigured = false }),
            ];
        }

        var activeSlot = Math.Clamp(state.ActiveCurveSlot, 0, MaxCurveProfileSlots - 1);
        return state with { CurveProfiles = profiles, ActiveCurveSlot = activeSlot };
    }

    // Mirrors the active slot's own curve into the active-curve fields the worker/clients read.
    // Follow slots (FollowFanIndex set) are resolved by the curve worker, not here.
    private static FanControlStateSnapshot SyncActiveCurveFields(FanControlStateSnapshot state)
    {
        if (state.Mode != FanControlMode.CustomCurve || state.CurveProfiles.IsDefaultOrEmpty)
        {
            return state;
        }

        var active = state.CurveProfiles.ElementAtOrDefault(state.ActiveCurveSlot);
        if (active is null || !active.IsConfigured || active.FollowFanIndex is not null)
        {
            return state;
        }

        return state with
        {
            CustomCurvePoints = active.CurvePoints,
            DrivingTemperatureAggregation = active.DrivingTemperatureAggregation,
            DrivingSensorIndices = active.DrivingSensorIndices,
            TreatMissingSensorsAsZero = active.TreatMissingSensorsAsZero,
        };
    }

    private void UpsertState(int fanIndex, Func<FanControlStateSnapshot, FanControlStateSnapshot> update, string reason)
    {
        lock (_stateLock)
        {
            var existingLookup = _fanControlStates.Lookup(fanIndex);
            var existing = existingLookup.HasValue
                ? existingLookup.Value
                : new FanControlStateSnapshot
            {
                FanIndex = fanIndex,
                DisplayName = $"Fan {fanIndex}",
                Mode = FanControlMode.Auto,
                CustomCurvePoints = ImmutableSortedDictionary<int, double>.Empty,
                DrivingTemperatureAggregation = TemperatureAggregationMode.Maximum,
                DrivingSensorIndices = [],
                ObservedAt = DateTimeOffset.UtcNow,
                IsAvailable = true,
            };

            PublishState(ApplySafetyState(update(existing)), reason);
        }
    }

    private void PublishState(FanControlStateSnapshot state, string reason)
    {
        _logger.LogDebug("Publishing fan control state for fan {FanIndex}. Reason={Reason}, Mode={Mode}, IsAvailable={IsAvailable}, HasActiveOverride={HasActiveOverride}, RestoreFailed={RestoreFailed}.", state.FanIndex, reason, state.Mode, state.IsAvailable, state.HasActiveOverride, state.LastAutoRestoreAttemptFailed);
        _fanControlStates.AddOrUpdate(state);
    }

    private void RemoveState(int fanIndex, string reason)
    {
        _logger.LogDebug("Removing fan control state for fan {FanIndex}. Reason={Reason}.", fanIndex, reason);
        _fanControlStates.Remove(fanIndex);
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
}