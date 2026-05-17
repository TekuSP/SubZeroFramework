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
    private readonly SourceCache<FanControlStateSnapshot, int> _fanControlStates = new(state => state.FanIndex);
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
            },
            "manual command");
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
            },
            "automatic restore");
    }

    public void SetCustomCurve(int fanIndex, IReadOnlyDictionary<int, double> customCurvePoints, TemperatureAggregationMode aggregationMode, IReadOnlyCollection<int> drivingSensorIndices)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(customCurvePoints);
        ArgumentNullException.ThrowIfNull(drivingSensorIndices);

        UpsertState(
            fanIndex,
            existing => existing with
            {
                Mode = FanControlMode.CustomCurve,
                ObservedAt = DateTimeOffset.UtcNow,
                CustomCurvePoints = customCurvePoints.Count == 0
                    ? ImmutableSortedDictionary<int, double>.Empty
                    : customCurvePoints.ToImmutableSortedDictionary(pair => pair.Key, pair => pair.Value),
                DrivingTemperatureAggregation = aggregationMode,
                DrivingSensorIndices = [.. drivingSensorIndices],
            },
            "custom curve update");
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

    private void ApplyConfiguredStates()
    {
        var optionsByFanIndex = _optionsMonitor.CurrentValue.FanControlStates
            .ToDictionary(option => option.FanIndex);

        _logger.LogDebug("Applying configured fan control state overlays for {ConfiguredFanCount} configured fan(s).", optionsByFanIndex.Count);

        foreach (var existingState in _fanControlStates.Items.ToArray())
        {
            if (!optionsByFanIndex.TryGetValue(existingState.FanIndex, out var configuredState))
            {
                continue;
            }

            PublishState(ApplySafetyState(ApplyConfiguredState(existingState, configuredState)), "configured state refresh");
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
                RemoveState(change.Key, "fan state removal");
                continue;
            }

            var currentLookup = _fanControlStates.Lookup(change.Key);
            var current = currentLookup.HasValue
                ? currentLookup.Value
                : new FanControlStateSnapshot
            {
                FanIndex = change.Key,
                DisplayName = change.Current.DisplayName,
                Mode = FanControlMode.Auto,
                DrivingTemperatureAggregation = TemperatureAggregationMode.Maximum,
                DrivingSensorIndices = [],
                ObservedAt = change.Current.ObservedAt,
                IsAvailable = change.Current.IsAvailable,
            };

            var updated = current with
            {
                DisplayName = change.Current.DisplayName,
                ObservedAt = change.Current.ObservedAt,
                IsAvailable = change.Current.IsAvailable,
            };

            if (optionsByFanIndex.TryGetValue(change.Key, out var configuredState))
            {
                updated = ApplyConfiguredState(updated, configuredState);
            }

            PublishState(ApplySafetyState(updated), currentLookup.HasValue ? "fan state update" : "fan state initialization");
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
        return state with
        {
            Mode = configuredState.Mode,
            CustomCurvePoints = configuredState.Mode == FanControlMode.CustomCurve && configuredState.CustomCurvePoints.Count > 0
                ? configuredState.CustomCurvePoints.ToImmutableSortedDictionary(pair => pair.Key, pair => pair.Value)
                : ImmutableSortedDictionary<int, double>.Empty,
            DrivingTemperatureAggregation = configuredState.DrivingTemperatureAggregation,
            DrivingSensorIndices = configuredState.Mode == FanControlMode.CustomCurve
                ? [.. configuredState.DrivingSensorIndices]
                : ImmutableArray<int>.Empty,
        };
    }

    private void UpsertState(int fanIndex, Func<FanControlStateSnapshot, FanControlStateSnapshot> update, string reason)
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