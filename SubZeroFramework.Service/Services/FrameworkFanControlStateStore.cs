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
    // gRPC command like SetCpuUsageModifier) can interleave so the later publish resurrects the earlier
    // lookup's stale fields, silently reverting a just-applied change (and persisting the reverted value).
    private readonly Lock _stateLock = new();
    private readonly CompositeDisposable _subscriptions = [];
    private readonly FrameworkFanControlSafetyTracker _fanControlSafetyTracker;
    private readonly IOptionsMonitor<FrameworkServiceOptions> _optionsMonitor;
    private readonly ILogger<FrameworkFanControlStateStore> _logger;
    private bool _disposed;

    public FrameworkFanControlStateStore(IFrameworkDataProvider frameworkDataProvider, FrameworkFanControlSafetyTracker fanControlSafetyTracker, IOptionsMonitor<FrameworkServiceOptions> optionsMonitor, ILogger<FrameworkFanControlStateStore> logger)
    {
        ArgumentNullException.ThrowIfNull(frameworkDataProvider);
        ArgumentNullException.ThrowIfNull(fanControlSafetyTracker);
        ArgumentNullException.ThrowIfNull(optionsMonitor);

        _fanControlSafetyTracker = fanControlSafetyTracker;
        _optionsMonitor = optionsMonitor;
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
    public void SetCustomCurve(int fanIndex, IReadOnlyDictionary<int, double> customCurvePoints, TemperatureAggregationMode aggregationMode, IReadOnlyCollection<int> drivingSensorIndices)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(customCurvePoints);
        ArgumentNullException.ThrowIfNull(drivingSensorIndices);

        var lookup = _fanControlStates.Lookup(fanIndex);
        var activeSlot = lookup.HasValue ? Math.Clamp(lookup.Value.ActiveCurveSlot, 0, MaxCurveProfileSlots - 1) : 0;
        var name = lookup.HasValue ? lookup.Value.CurveProfiles.ElementAtOrDefault(activeSlot)?.Name : null;

        SaveCurveProfile(fanIndex, activeSlot, name, customCurvePoints, aggregationMode, drivingSensorIndices, followFanIndex: null, activate: true);
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
        bool activate)
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
                    }),
            ],
            LinkedLeaderIndex = state.LinkedLeaderIndex,
            CpuUsageModifierStrength = state.CpuUsageModifierStrength,
        };
    }

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

    /// <summary>
    /// Sets (or clears, when <paramref name="strength"/> is null or NaN) the fan's CPU usage modifier: the
    /// duty points added on top of the active custom curve at 100% smoothed CPU usage. The modifier is
    /// per-fan — it survives mode switches and applies whenever a custom curve drives the fan. Updates the
    /// in-memory snapshot and streams the change; the caller persists it. Returns false when the fan is unknown.
    /// </summary>
    public bool SetCpuUsageModifier(int fanIndex, double? strength)
    {
        ThrowIfDisposed();

        var normalized = SanitizeModifierStrength(strength);

        lock (_stateLock)
        {
            var lookup = _fanControlStates.Lookup(fanIndex);
            if (!lookup.HasValue)
            {
                return false;
            }

            if (lookup.Value.CpuUsageModifierStrength == normalized)
            {
                return true;
            }

            PublishState(lookup.Value with { CpuUsageModifierStrength = normalized, ObservedAt = DateTimeOffset.UtcNow }, "cpu usage modifier change");
        }

        return true;
    }

    // Null/NaN/infinite means disabled; a stored strength is always a finite 0-100 duty-point value.
    private static double? SanitizeModifierStrength(double? strength)
        => strength is double value && double.IsFinite(value) ? Math.Clamp(value, 0d, 100d) : null;

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
        var next = state with
        {
            Mode = configuredState.Mode,
            ActiveCurveSlot = Math.Clamp(configuredState.ActiveCurveSlot, 0, MaxCurveProfileSlots - 1),
            CurveProfiles = BuildProfilesFromOptions(configuredState),
            LinkedLeaderIndex = configuredState.LinkedLeaderIndex,
            CpuUsageModifierStrength = SanitizeModifierStrength(configuredState.CpuUsageModifierStrength),
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