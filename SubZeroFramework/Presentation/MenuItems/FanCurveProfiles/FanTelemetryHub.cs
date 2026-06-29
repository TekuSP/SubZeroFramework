using System.Collections.Immutable;
using System.Collections.ObjectModel;

using DynamicData;

using SubZeroFramework.Controls.Fans.Models;
using SubZeroFramework.Models;
using SubZeroFramework.Services;
using SubZeroFramework.Services.Units;

namespace SubZeroFramework.Presentation.MenuItems.FanCurveProfiles;

/// <summary>
/// Owns the live fan fleet for the Fan Control page: the per-fan caches and the <see cref="FanCardModel"/>
/// collection, kept current by the page's telemetry subscriptions forwarding change sets to the Apply*
/// methods. The page coordinator reacts to <see cref="FanAdded"/> / <see cref="FanRemoved"/> (selection, link
/// chips) and re-renders on its own; the hub only maintains data. Registered as a DI singleton.
/// </summary>
public sealed class FanTelemetryHub
{
    private readonly IFanHistoryStore _historyStore;
    private readonly IUnitFormattingService _unitFormattingService;

    private readonly ObservableCollection<FanCardModel> _fans = [];
    private readonly Dictionary<int, FanCardModel> _fanCardsByIndex = [];
    private readonly Dictionary<int, FanCapabilityState> _capabilities = [];
    private readonly Dictionary<int, FanControlStateSnapshot> _controlStates = [];
    private readonly Dictionary<int, FanStateSnapshot> _fanStates = [];
    private readonly Dictionary<int, FanTelemetrySnapshot> _fanSnapshots = [];
    private readonly Dictionary<int, TemperatureTelemetrySnapshot> _temperatureSnapshots = [];

    public FanTelemetryHub(IFanHistoryStore historyStore, IUnitFormattingService unitFormattingService)
    {
        _historyStore = historyStore;
        _unitFormattingService = unitFormattingService;
        Fans = new ReadOnlyObservableCollection<FanCardModel>(_fans);
    }

    /// <summary>The fan cards, kept sorted by fan index.</summary>
    public ReadOnlyObservableCollection<FanCardModel> Fans { get; }

    /// <summary>Raised after a new fan card is created (so the coordinator can auto-select / link it).</summary>
    public event Action<int>? FanAdded;

    /// <summary>Raised after a fan card is removed (so the coordinator can re-select / relink).</summary>
    public event Action<int>? FanRemoved;

    public FanCardModel? GetFan(int fanIndex) => _fanCardsByIndex.GetValueOrDefault(fanIndex);

    public void ApplyCapabilityChanges(IChangeSet<FanCapabilityState, int> changes)
    {
        foreach (var change in changes)
        {
            if (change.Reason == ChangeReason.Remove)
            {
                _capabilities.Remove(change.Key);
                if (_fanCardsByIndex.TryGetValue(change.Key, out var existing))
                {
                    existing.Capability = null;
                }
                continue;
            }

            _capabilities[change.Key] = change.Current;
            var fan = EnsureFanCard(change.Key);
            fan.Capability = change.Current;
        }
    }

    public void ApplyControlStateChanges(IChangeSet<FanControlStateSnapshot, int> changes)
    {
        foreach (var change in changes)
        {
            if (change.Reason == ChangeReason.Remove)
            {
                _controlStates.Remove(change.Key);
                if (_fanCardsByIndex.TryGetValue(change.Key, out var existing))
                {
                    existing.ControlState = null;
                    existing.DrivingSensors = [];
                    existing.DrivingTemperatureHistory = [];
                }
                continue;
            }

            _controlStates[change.Key] = change.Current;
            var fan = EnsureFanCard(change.Key);
            fan.ControlState = change.Current;
            UpdateDrivingSensors(fan);
            EnsureTemperatureHistorySubscriptionsForFan(fan);
        }
    }

    public void ApplyFanStateChanges(IChangeSet<FanStateSnapshot, int> changes)
    {
        foreach (var change in changes)
        {
            if (change.Reason == ChangeReason.Remove)
            {
                _fanStates.Remove(change.Key);
                if (_fanCardsByIndex.TryGetValue(change.Key, out var existing))
                {
                    existing.FanState = null;
                }
                continue;
            }

            _fanStates[change.Key] = change.Current;
            var fan = EnsureFanCard(change.Key);
            fan.FanState = change.Current;
        }
    }

    public void ApplyFanTelemetryChanges(IChangeSet<FanTelemetrySnapshot, int> changes)
    {
        foreach (var change in changes)
        {
            if (change.Reason is ChangeReason.Add or ChangeReason.Update or ChangeReason.Refresh)
            {
                _fanSnapshots[change.Key] = change.Current;
                var fan = EnsureFanCard(change.Key);
                fan.Snapshot = change.Current;
                continue;
            }

            if (change.Reason == ChangeReason.Remove)
            {
                _fanSnapshots.Remove(change.Key);
                RemoveFanCard(change.Key);
            }
        }
    }

    /// <summary>Updates the temperature cache and each fan's driving-sensor readouts (the coordinator owns the sensor chips).</summary>
    public void ApplyTemperatureSnapshots(IChangeSet<TemperatureTelemetrySnapshot, int> changes)
    {
        var anyChanged = false;

        foreach (var change in changes)
        {
            if (change.Reason == ChangeReason.Remove)
            {
                _temperatureSnapshots.Remove(change.Key);
                anyChanged = true;
                continue;
            }

            _temperatureSnapshots[change.Key] = change.Current;
            anyChanged = true;
        }

        if (anyChanged)
        {
            foreach (var fan in _fans)
            {
                UpdateDrivingSensors(fan);
                EnsureTemperatureHistorySubscriptionsForFan(fan);
            }
        }
    }

    private FanCardModel EnsureFanCard(int fanIndex)
    {
        if (_fanCardsByIndex.TryGetValue(fanIndex, out var existing))
        {
            return existing;
        }

        var fan = new FanCardModel(_unitFormattingService)
        {
            Snapshot = _fanSnapshots.GetValueOrDefault(fanIndex) ?? new FanTelemetrySnapshot
            {
                FanIndex = fanIndex,
                DisplayName = $"Fan {fanIndex}",
                UnitSymbol = "rpm",
                ObservedAt = DateTimeOffset.UtcNow,
                SpeedRpm = 0d,
                IsAvailable = false,
            },
            Capability = _capabilities.GetValueOrDefault(fanIndex),
            ControlState = _controlStates.GetValueOrDefault(fanIndex),
            DrivingSensors = GetDrivingSensors(_controlStates.GetValueOrDefault(fanIndex)),
            FanState = _fanStates.GetValueOrDefault(fanIndex),
        };

        _fanCardsByIndex[fanIndex] = fan;
        InsertSorted(fan);
        _historyStore.EnsureFanHistory(fanIndex, PresentationDefaults.RecentTelemetryHistoryWindow);
        EnsureTemperatureHistorySubscriptionsForFan(fan);

        FanAdded?.Invoke(fanIndex);
        return fan;
    }

    private void RemoveFanCard(int fanIndex)
    {
        if (!_fanCardsByIndex.Remove(fanIndex, out var fan))
        {
            return;
        }

        _fans.Remove(fan);
        _historyStore.RemoveFanHistory(fanIndex);

        FanRemoved?.Invoke(fanIndex);
    }

    private void InsertSorted(FanCardModel fan)
    {
        var insertIndex = 0;
        while (insertIndex < _fans.Count && _fans[insertIndex].Snapshot.FanIndex < fan.Snapshot.FanIndex)
        {
            insertIndex++;
        }
        _fans.Insert(insertIndex, fan);
    }

    private void UpdateDrivingSensors(FanCardModel fan)
    {
        fan.DrivingSensors = GetDrivingSensors(fan.ControlState);
    }

    private ImmutableArray<TemperatureTelemetrySnapshot> GetDrivingSensors(FanControlStateSnapshot? state)
    {
        if (state is null || state.DrivingSensorIndices.IsDefaultOrEmpty)
        {
            return [];
        }

        var builder = ImmutableArray.CreateBuilder<TemperatureTelemetrySnapshot>(state.DrivingSensorIndices.Length);
        foreach (var sensorIndex in state.DrivingSensorIndices)
        {
            if (_temperatureSnapshots.TryGetValue(sensorIndex, out var snapshot))
            {
                builder.Add(snapshot);
            }
        }

        return builder.ToImmutable();
    }

    private void EnsureTemperatureHistorySubscriptionsForFan(FanCardModel fan)
    {
        if (fan.ControlState is null || fan.ControlState.DrivingSensorIndices.IsDefaultOrEmpty)
        {
            return;
        }

        foreach (var sensorIndex in fan.ControlState.DrivingSensorIndices)
        {
            _historyStore.EnsureTemperatureHistory(sensorIndex, PresentationDefaults.RecentTelemetryHistoryWindow);
        }
    }
}
