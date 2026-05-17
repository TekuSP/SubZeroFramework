using System.Collections.Immutable;

using SubZeroFramework.Models;

namespace SubZeroFramework.Services;

public sealed class FrameworkFanControlSafetyTracker
{
    private readonly Lock _syncLock = new();
    private readonly Dictionary<int, FanControlSafetyStateSnapshot> _fanSafetyStates = [];
    private readonly HashSet<int> _restoreInProgressFanIndices = [];

    public event Action<int>? SafetyStateChanged;

    public bool HasActiveOverrides
    {
        get
        {
            lock (_syncLock)
            {
                return _fanSafetyStates.Values.Any(state => state.HasActiveOverride);
            }
        }
    }

    public FanControlSafetyStateSnapshot GetState(int fanIndex)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(fanIndex);

        lock (_syncLock)
        {
            return GetStateCore(fanIndex);
        }
    }

    public void MarkOverrideActive(int fanIndex)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(fanIndex);

        UpdateState(fanIndex, existing => existing with
        {
            HasActiveOverride = true,
            LastAutoRestoreAttemptFailed = false,
            LastAutoRestoreAttemptAt = null,
            LastAutoRestoreError = null,
        });
    }

    public void MarkAutoRestored(int fanIndex)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(fanIndex);

        UpdateState(fanIndex, existing => existing with
        {
            HasActiveOverride = false,
            LastAutoRestoreAttemptFailed = false,
            LastAutoRestoreAttemptAt = null,
            LastAutoRestoreError = null,
        });
    }

    public ImmutableArray<int> BeginRestoreBatch()
    {
        lock (_syncLock)
        {
            List<int> orderedFanIndices = [.. _fanSafetyStates
                .Where(pair => pair.Value.HasActiveOverride)
                .Select(pair => pair.Key)];

            if (orderedFanIndices.Count == 0)
            {
                return [];
            }

            orderedFanIndices.Sort();

            ImmutableArray<int>.Builder builder = ImmutableArray.CreateBuilder<int>(orderedFanIndices.Count);
            foreach (var fanIndex in orderedFanIndices)
            {
                if (_restoreInProgressFanIndices.Add(fanIndex))
                {
                    builder.Add(fanIndex);
                }
            }

            return builder.ToImmutable();
        }
    }

    public void CompleteRestore(int fanIndex, bool restored, string? errorMessage = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(fanIndex);

        UpdateState(fanIndex, existing => existing with
        {
            HasActiveOverride = !restored,
            LastAutoRestoreAttemptFailed = !restored,
            LastAutoRestoreAttemptAt = DateTimeOffset.UtcNow,
            LastAutoRestoreError = restored ? null : errorMessage,
        });
    }

    private void UpdateState(int fanIndex, Func<FanControlSafetyStateSnapshot, FanControlSafetyStateSnapshot> update)
    {
        FanControlSafetyStateSnapshot existing;
        FanControlSafetyStateSnapshot updated;
        Action<int>? stateChanged;

        lock (_syncLock)
        {
            existing = GetStateCore(fanIndex);
            updated = update(existing);
            _fanSafetyStates[fanIndex] = updated;
            _restoreInProgressFanIndices.Remove(fanIndex);

            if (updated == existing)
            {
                return;
            }

            stateChanged = SafetyStateChanged;
        }

        stateChanged?.Invoke(fanIndex);
    }

    private FanControlSafetyStateSnapshot GetStateCore(int fanIndex)
    {
        return _fanSafetyStates.TryGetValue(fanIndex, out var state)
            ? state
            : new FanControlSafetyStateSnapshot();
    }
}