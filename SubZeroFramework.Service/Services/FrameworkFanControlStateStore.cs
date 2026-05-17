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
    private readonly IOptionsMonitor<FrameworkServiceOptions> _optionsMonitor;
    private bool _disposed;

    public FrameworkFanControlStateStore(IFrameworkDataProvider frameworkDataProvider, IOptionsMonitor<FrameworkServiceOptions> optionsMonitor)
    {
        ArgumentNullException.ThrowIfNull(frameworkDataProvider);
        ArgumentNullException.ThrowIfNull(optionsMonitor);

        _optionsMonitor = optionsMonitor;

        frameworkDataProvider
            .ConnectFanStates()
            .Subscribe(ApplyFanStateChanges)
            .DisposeWith(_subscriptions);

        var optionsSubscription = _optionsMonitor.OnChange(_ => ApplyConfiguredStates());
        if (optionsSubscription is not null)
        {
            optionsSubscription.DisposeWith(_subscriptions);
        }
        ApplyConfiguredStates();
    }

    public IObservable<IChangeSet<FanControlStateSnapshot, int>> Connect()
        => _fanControlStates.Connect();

    public void MarkManual(int fanIndex)
    {
        ThrowIfDisposed();
        UpsertState(fanIndex, existing => existing with
        {
            Mode = FanControlMode.Manual,
            ObservedAt = DateTimeOffset.UtcNow,
            CustomCurvePoints = ImmutableSortedDictionary<int, double>.Empty,
            DrivingSensorIndices = [],
        });
    }

    public void MarkAuto(int fanIndex)
    {
        ThrowIfDisposed();
        UpsertState(fanIndex, existing => existing with
        {
            Mode = FanControlMode.Auto,
            ObservedAt = DateTimeOffset.UtcNow,
            CustomCurvePoints = ImmutableSortedDictionary<int, double>.Empty,
            DrivingSensorIndices = [],
        });
    }

    public void SetCustomCurve(int fanIndex, IReadOnlyDictionary<int, double> customCurvePoints, TemperatureAggregationMode aggregationMode, IReadOnlyCollection<int> drivingSensorIndices)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(customCurvePoints);
        ArgumentNullException.ThrowIfNull(drivingSensorIndices);

        UpsertState(fanIndex, existing => existing with
        {
            Mode = FanControlMode.CustomCurve,
            ObservedAt = DateTimeOffset.UtcNow,
            CustomCurvePoints = customCurvePoints.Count == 0
                ? ImmutableSortedDictionary<int, double>.Empty
                : customCurvePoints.ToImmutableSortedDictionary(pair => pair.Key, pair => pair.Value),
            DrivingTemperatureAggregation = aggregationMode,
            DrivingSensorIndices = [.. drivingSensorIndices],
        });
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _subscriptions.Dispose();
        _fanControlStates.Dispose();
        _disposed = true;
    }

    private void ApplyConfiguredStates()
    {
        var optionsByFanIndex = _optionsMonitor.CurrentValue.FanControlStates
            .ToDictionary(option => option.FanIndex);

        foreach (var existingState in _fanControlStates.Items.ToArray())
        {
            if (!optionsByFanIndex.TryGetValue(existingState.FanIndex, out var configuredState))
            {
                continue;
            }

            _fanControlStates.AddOrUpdate(ApplyConfiguredState(existingState, configuredState));
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
                _fanControlStates.Remove(change.Key);
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

            _fanControlStates.AddOrUpdate(updated);
        }
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

    private void UpsertState(int fanIndex, Func<FanControlStateSnapshot, FanControlStateSnapshot> update)
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

        _fanControlStates.AddOrUpdate(update(existing));
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
}