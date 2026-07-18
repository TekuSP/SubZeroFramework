using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Reactive.Disposables;
using System.Reactive.Disposables.Fluent;
using System.Reactive.Linq;

using DynamicData;

using FrameworkDotnet.Snapshots;

using SubZeroFramework.Models;
using SubZeroFramework.Services;

namespace SubZeroFramework.Service.Services.Hosting;

/// <summary>
/// Drives the embedded controller to match each fan's control state: a <see cref="FanControlMode.CustomCurve"/>
/// fan is evaluated against the current driving-sensor temperature — plus the fan's CPU usage modifier, an
/// exponential feed-forward boost that ramps the fan before heat reaches the sensors — while Max (100%) and
/// Manual (last duty) are re-asserted so a persisted simple override is restored to the EC after a service
/// restart (the gRPC handlers only actuate on a live command). Auto fans are left to the EC's native control.
/// Without this loop a stored curve or restored override is only reported as active, never actually applied.
/// </summary>
public sealed class FrameworkFanCurveControlWorker : BackgroundService
{
    // Re-apply only when the target duty moves at least this much, to avoid writing the EC on every sample.
    private const double DutyChangeThresholdPercent = 1.0d;

    // Evaluate at a calmer cadence than the raw telemetry poll so the EC is not written every poll.
    private static readonly TimeSpan EvaluationInterval = TimeSpan.FromSeconds(1);

    // Smoothing for the CPU usage feeding the per-fan usage modifier: rising load is taken instantly so
    // fans ramp before heat reaches the sensors, falling load decays with this half-life so one-second
    // spikes do not make the fans surge and drop.
    private static readonly TimeSpan CpuUsageDecayHalfLife = TimeSpan.FromSeconds(5);

    private readonly IFrameworkDataProvider _frameworkDataProvider;
    private readonly FrameworkFanControlStateStore _fanControlStateStore;
    private readonly FrameworkFanControlAuthorizationService _authorizationService;
    private readonly ILogger<FrameworkFanCurveControlWorker> _logger;
    private readonly CancellationTokenRegistration _applicationStoppingRegistration;

    // Authoritative per-fan control state, mirrored from the state store.
    private readonly ConcurrentDictionary<int, FanControlStateSnapshot> _controlStates = new();

    // Last duty written per fan. Only touched inside the serialized evaluation, so a plain dictionary is safe.
    private readonly Dictionary<int, double> _lastAppliedDuty = [];

    // Smoothed CPU usage for the usage modifier. Only touched inside the serialized evaluation.
    private readonly FanUsageSmoothingFilter _cpuUsageFilter = new(CpuUsageDecayHalfLife);
    private long _lastCpuUsageSampleTimestamp;

    private readonly CompositeDisposable _subscriptions = [];

    public FrameworkFanCurveControlWorker(
        IFrameworkDataProvider frameworkDataProvider,
        FrameworkFanControlStateStore fanControlStateStore,
        FrameworkFanControlAuthorizationService authorizationService,
        IHostApplicationLifetime applicationLifetime,
        ILogger<FrameworkFanCurveControlWorker> logger)
    {
        _frameworkDataProvider = frameworkDataProvider;
        _fanControlStateStore = fanControlStateStore;
        _authorizationService = authorizationService;
        _logger = logger;
        _applicationStoppingRegistration = applicationLifetime.ApplicationStopping.Register(OnApplicationStopping);
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "Fan curve control loop is active. EvaluationInterval={EvaluationInterval}, DutyChangeThreshold={DutyChangeThreshold}%.",
            EvaluationInterval,
            DutyChangeThresholdPercent);

        // Track the authoritative per-fan control state published by the store.
        _fanControlStateStore
            .Connect()
            .Subscribe(
                ApplyControlStateChanges,
                exception => _logger.LogError(exception, "The fan control state stream faulted inside the curve control loop."))
            .DisposeWith(_subscriptions);

        // Evaluate curves on a sampled thermal cadence; Concat serializes evaluations so EC writes never overlap.
        _frameworkDataProvider.ThermalSnapshots
            .Sample(EvaluationInterval)
            .Select(snapshot => Observable.FromAsync(token => EvaluateAsync(snapshot, token)))
            .Concat()
            .Subscribe(
                static _ => { },
                exception => _logger.LogError(exception, "The fan curve control loop faulted."))
            .DisposeWith(_subscriptions);

        return Task.CompletedTask;
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _subscriptions.Dispose();
        await base.StopAsync(cancellationToken).ConfigureAwait(false);
    }

    public override void Dispose()
    {
        _applicationStoppingRegistration.Dispose();
        _subscriptions.Dispose();
        base.Dispose();
    }

    private void OnApplicationStopping()
    {
        // Stop actuating immediately so we do not fight the shutdown restore-to-auto path.
        _logger.LogInformation("Host shutdown requested. Stopping the fan curve control loop before fan restore runs.");
        _subscriptions.Dispose();
    }

    private void ApplyControlStateChanges(IChangeSet<FanControlStateSnapshot, int> changes)
    {
        foreach (var change in changes)
        {
            if (change.Reason == ChangeReason.Remove)
            {
                _controlStates.TryRemove(change.Key, out _);
                continue;
            }

            _controlStates[change.Key] = change.Current;
        }
    }

    private async Task EvaluateAsync(FrameworkThermalSnapshot thermalSnapshot, CancellationToken cancellationToken)
    {
        // A persisted curve can be restored at startup even when commands are disabled; never actuate then.
        if (!_authorizationService.IsFanControlEnabled)
        {
            return;
        }

        // One smoothed CPU reading per evaluation pass so every fan boosts from the same sample.
        var cpuUsageFraction = SampleSmoothedCpuUsage();

        foreach (var state in _controlStates.Values)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            // Resolve the duty this fan should run: a curve interpolates against temperature (following any
            // per-slot link to a leader); Max is 100%; Manual holds its last duty; Auto (or unresolved) yields
            // null so the EC keeps native control. Re-asserting Max/Manual here is what restores a persisted
            // simple override to the EC after a service restart — the gRPC handlers only actuate on a live command.
            if (ResolveTargetDuty(state.FanIndex, thermalSnapshot, cpuUsageFraction, []) is not double targetDuty)
            {
                // Not driven by us (Auto / unresolved): forget the last applied duty so re-entry re-applies at once.
                _lastAppliedDuty.Remove(state.FanIndex);
                continue;
            }

            if (_lastAppliedDuty.TryGetValue(state.FanIndex, out var lastDuty)
                && Math.Abs(targetDuty - lastDuty) < DutyChangeThresholdPercent)
            {
                continue;
            }

            try
            {
                var result = await _frameworkDataProvider.SetFanDutyAsync(state.FanIndex, targetDuty, cancellationToken).ConfigureAwait(false);

                // Record the applied duty without changing the mode (RecordAppliedDuty preserves CustomCurve).
                _lastAppliedDuty[state.FanIndex] = result.AppliedDutyPercent;
                _fanControlStateStore.RecordAppliedDuty(state.FanIndex, result.AppliedDutyPercent);

                _logger.LogDebug(
                    "Applied curve duty for fan {FanIndex}. TargetDuty={TargetDuty:0.#}%, AppliedDuty={AppliedDuty:0.#}%.",
                    state.FanIndex,
                    targetDuty,
                    result.AppliedDutyPercent);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (InvalidOperationException exception)
            {
                _logger.LogDebug(exception, "Skipped curve duty for fan {FanIndex} because the service is not in a writable state.", state.FanIndex);
            }
            catch (Exception exception)
            {
                _logger.LogWarning(exception, "Failed to apply curve duty for fan {FanIndex}.", state.FanIndex);
            }
        }
    }

    // Resolves the duty a curve-driven fan should run, walking the active slot's per-slot follow link.
    // Follow chains are walked with cycle detection; a leader that is not curve-driven contributes its
    // last applied duty (Max => 100%, Manual => last duty, Auto/unknown => no actuation, fan holds).
    // The CPU usage modifier is applied where the curve is interpolated, so a follower fan inherits its
    // leader's already-boosted duty rather than boosting twice.
    private double? ResolveTargetDuty(int fanIndex, FrameworkThermalSnapshot snapshot, double? cpuUsageFraction, HashSet<int> visited)
    {
        if (!_controlStates.TryGetValue(fanIndex, out var state))
        {
            return null;
        }

        if (state.Mode != FanControlMode.CustomCurve)
        {
            return state.Mode switch
            {
                FanControlMode.Max => 100d,
                FanControlMode.Manual => state.LastDutyPercent,
                _ => null,
            };
        }

        if (!visited.Add(fanIndex))
        {
            // Follow cycle (A -> B -> A): stop rather than oscillate; this fan holds its last duty.
            return null;
        }

        var active = state.CurveProfiles.ElementAtOrDefault(state.ActiveCurveSlot);
        if (active is { FollowFanIndex: int leaderFanIndex } && leaderFanIndex != fanIndex)
        {
            return ResolveTargetDuty(leaderFanIndex, snapshot, cpuUsageFraction, visited);
        }

        if (state.CustomCurvePoints.Count < 2 || state.DrivingSensorIndices.IsDefaultOrEmpty)
        {
            return null;
        }

        var temperature = AggregateDrivingTemperature(snapshot, state.DrivingSensorIndices, state.DrivingTemperatureAggregation);
        if (temperature is not double celsius)
        {
            return null;
        }

        var curveDuty = InterpolateDuty(state.CustomCurvePoints, celsius);
        return Clamp(curveDuty + FanUsageModifierMath.ComputeBoost(state.CpuUsageModifierStrength, cpuUsageFraction));
    }

    /// <summary>
    /// Feeds the latest Hardware.Info CPU reading (refreshed by the service's 1 s hardware-info poll)
    /// through the fast-attack / slow-decay filter. Returns null until a first reading exists, which
    /// disables the usage boost rather than guessing.
    /// </summary>
    private double? SampleSmoothedCpuUsage()
    {
        var timestamp = Stopwatch.GetTimestamp();
        var elapsed = _lastCpuUsageSampleTimestamp == 0
            ? TimeSpan.Zero
            : Stopwatch.GetElapsedTime(_lastCpuUsageSampleTimestamp, timestamp);
        _lastCpuUsageSampleTimestamp = timestamp;

        return _cpuUsageFilter.Sample(ReadCpuUsageFraction(), elapsed);
    }

    private double? ReadCpuUsageFraction()
    {
        var cpus = _frameworkDataProvider.GetLatestHardwareInfoSnapshot().Runtime.Cpus;
        var readings = new List<double>(cpus.Length);
        foreach (var cpu in cpus)
        {
            if (cpu.EffectivePercentProcessorTime is double percent)
            {
                readings.Add(Math.Clamp(percent, 0d, 100d));
            }
        }

        return readings.Count > 0 ? readings.Average() / 100d : null;
    }

    private static double? AggregateDrivingTemperature(FrameworkThermalSnapshot snapshot, ImmutableArray<int> sensorIndices, TemperatureAggregationMode aggregation)
    {
        var count = Math.Min((int)snapshot.SensorCount, snapshot.Temperatures.Count);
        var readings = new List<double>(sensorIndices.Length);
        foreach (var sensorIndex in sensorIndices)
        {
            if (sensorIndex >= 0 && sensorIndex < count)
            {
                readings.Add(snapshot.Temperatures[sensorIndex].Temperature.DegreesCelsius);
            }
        }

        if (readings.Count == 0)
        {
            return null;
        }

        return aggregation switch
        {
            TemperatureAggregationMode.Average => readings.Average(),
            TemperatureAggregationMode.Maximum => readings.Max(),
            TemperatureAggregationMode.Minimum => readings.Min(),
            TemperatureAggregationMode.Median => Median(readings),
            _ => readings.Max(),
        };
    }

    private static double Median(List<double> readings)
    {
        readings.Sort();
        var middle = readings.Count / 2;
        return readings.Count % 2 == 0 ? (readings[middle - 1] + readings[middle]) / 2d : readings[middle];
    }

    /// <summary>
    /// Interpolates the duty for a temperature, mirroring the editor's rendered curve: an implicit
    /// (0 C, 0%) anchor when the first point is above 0 C, and holding the last point's duty above it.
    /// Keeping this identical to the client preview means "what you preview is what the fan does".
    /// </summary>
    private static double InterpolateDuty(ImmutableSortedDictionary<int, double> curvePoints, double temperatureCelsius)
    {
        var points = new List<(double Temperature, double Duty)>(curvePoints.Count + 1);
        if (curvePoints.Keys.First() > 0)
        {
            points.Add((0d, 0d));
        }

        foreach (var pair in curvePoints)
        {
            points.Add((pair.Key, pair.Value));
        }

        if (temperatureCelsius <= points[0].Temperature)
        {
            return Clamp(points[0].Duty);
        }

        var last = points[^1];
        if (temperatureCelsius >= last.Temperature)
        {
            return Clamp(last.Duty);
        }

        for (var i = 1; i < points.Count; i++)
        {
            if (temperatureCelsius <= points[i].Temperature)
            {
                var lower = points[i - 1];
                var upper = points[i];
                var span = upper.Temperature - lower.Temperature;
                if (span <= 0d)
                {
                    return Clamp(upper.Duty);
                }

                var ratio = (temperatureCelsius - lower.Temperature) / span;
                return Clamp(lower.Duty + (ratio * (upper.Duty - lower.Duty)));
            }
        }

        return Clamp(last.Duty);
    }

    private static double Clamp(double duty) => Math.Clamp(duty, 0d, 100d);
}
