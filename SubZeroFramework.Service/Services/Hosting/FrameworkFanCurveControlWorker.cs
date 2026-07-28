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
public sealed partial class FrameworkFanCurveControlWorker : BackgroundService
{
    // Re-apply only when the target duty moves at least this much, to avoid writing the EC on every sample.
    private const double DutyChangeThresholdPercent = 1.0d;

    // Evaluate at a calmer cadence than the raw telemetry poll so the EC is not written every poll.
    private static readonly TimeSpan DefaultEvaluationInterval = TimeSpan.FromSeconds(1);

    // Instance-level so tests can drive evaluations without a real-time wait per assertion. Production
    // always uses the default; nothing but the test constructor overload passes anything else.
    private readonly TimeSpan _evaluationInterval;

    // Smoothing for the CPU usage feeding the per-fan usage modifier: rising load is taken instantly so
    // fans ramp before heat reaches the sensors, falling load decays with this half-life so one-second
    // spikes do not make the fans surge and drop.
    private static readonly TimeSpan CpuUsageDecayHalfLife = TimeSpan.FromSeconds(5);

    // A hardware-info snapshot older than this is a stalled poll, not a reading. Hardware.Info retains the
    // last successful CPU readings across failed refreshes, so without an age/availability guard a stale
    // "95% busy" would keep re-feeding the filter's fast-attack path and pin the boost forever.
    private static readonly TimeSpan MaxCpuUsageSnapshotAge = TimeSpan.FromSeconds(10);

    // How many consecutive no-usage evaluations (~1 s each) to tolerate before warning that configured
    // modifiers are inert. Covers Hardware.Info's slow first refresh at service start without noise.
    private const int MissingUsageWarningThreshold = 30;

    private readonly IFrameworkDataProvider _frameworkDataProvider;
    private readonly FrameworkFanControlStateStore _fanControlStateStore;
    private readonly FrameworkFanControlAuthorizationService _authorizationService;
    private readonly FrameworkFatalExitHandler _fatalExitHandler;
    private readonly ILogger<FrameworkFanCurveControlWorker> _logger;
    private readonly CancellationTokenRegistration _applicationStoppingRegistration;

    // Authoritative per-fan control state, mirrored from the state store.
    private readonly ConcurrentDictionary<int, FanControlStateSnapshot> _controlStates = new();

    // Last duty written per fan. Only touched inside the serialized evaluation, so a plain dictionary is safe.
    private readonly Dictionary<int, double> _lastAppliedDuty = [];

    // Fans currently handed back to firmware control because no driving sensor can be read, so the restore is
    // issued once per episode rather than every evaluation. Same threading rules as _lastAppliedDuty.
    private readonly HashSet<int> _fansInSafeFallback = [];

    // Smoothed CPU usage for the usage modifier. Only touched inside the serialized evaluation.
    private readonly FanUsageSmoothingFilter _cpuUsageFilter = new(CpuUsageDecayHalfLife);
    private long _lastCpuUsageSampleTimestamp;
    private int _consecutiveMissingUsageEvaluations;
    private bool _missingUsageWarningLogged;

    private readonly CompositeDisposable _subscriptions = [];

    public FrameworkFanCurveControlWorker(
        IFrameworkDataProvider frameworkDataProvider,
        FrameworkFanControlStateStore fanControlStateStore,
        FrameworkFanControlAuthorizationService authorizationService,
        FrameworkFatalExitHandler fatalExitHandler,
        IHostApplicationLifetime applicationLifetime,
        ILogger<FrameworkFanCurveControlWorker> logger,
        TimeSpan? evaluationInterval = null)
    {
        _frameworkDataProvider = frameworkDataProvider;
        _fanControlStateStore = fanControlStateStore;
        _authorizationService = authorizationService;
        _fatalExitHandler = fatalExitHandler;
        _logger = logger;
        _evaluationInterval = evaluationInterval ?? DefaultEvaluationInterval;
        _applicationStoppingRegistration = applicationLifetime.ApplicationStopping.Register(OnApplicationStopping);
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "Fan curve control loop is active. EvaluationInterval={EvaluationInterval}, DutyChangeThreshold={DutyChangeThreshold}%.",
            _evaluationInterval,
            DutyChangeThresholdPercent);

        // A faulted stream is fatal, not loggable-and-ignorable: it permanently stops curve actuation while
        // the EC may still hold a duty this worker applied. Restarting the service (which re-seeds the
        // persisted control state) is the safe recovery — and only a non-zero exit makes SCM/systemd do it.

        // Track the authoritative per-fan control state published by the store.
        _fanControlStateStore
            .Connect()
            .Subscribe(
                ApplyControlStateChanges,
                exception => _fatalExitHandler.HandleFatalFault(exception, "FrameworkFanCurveControlWorker control-state stream"))
            .DisposeWith(_subscriptions);

        // Evaluate curves on a sampled thermal cadence; Concat serializes evaluations so EC writes never overlap.
        _frameworkDataProvider.ThermalSnapshots
            .Sample(_evaluationInterval)
            .Select(snapshot => Observable.FromAsync(token => EvaluateAsync(snapshot, token)))
            .Concat()
            .Subscribe(
                static _ => { },
                exception => _fatalExitHandler.HandleFatalFault(exception, "FrameworkFanCurveControlWorker evaluation loop"))
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
        ReportMissingUsageIfModifiersInert(cpuUsageFraction);

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
            var decision = ResolveTargetDuty(state.FanIndex, thermalSnapshot, cpuUsageFraction, []);

            // Every pass, for every fan, including the passes where nothing happens. Without this a fan that
            // does not move leaves no record of WHY — the applied-duty log below is skipped both when the
            // fan is not driven and when the change threshold swallows the update, which is most ticks.
            LogFanDecision(state.FanIndex, decision.Outcome, decision.Duty, state.Mode);

            if (decision.Outcome == FanDutyOutcome.NotDriven)
            {
                // Not driven by us (Auto / unresolved): forget the last applied duty so re-entry re-applies at
                // once. The removal doubles as the record of whether WE were the last thing driving this fan.
                var wasDrivenByUs = _lastAppliedDuty.Remove(state.FanIndex);
                var alreadyHandedBack = _fansInSafeFallback.Remove(state.FanIndex);

                // Forgetting the duty is not enough: the EC is still holding whatever we last wrote. Until
                // this restore existed, a fan that stopped being driven — because its mode was overlaid back
                // to Auto, or a link/curve stopped resolving — stayed physically overridden at our last duty,
                // possibly 0%, while the store, the streams and the UI all reported Auto. A stopped fan
                // reported as Auto is the worst state this service can leave hardware in.
                // Safe-fallback already handed the fan to the EC, so it needs no second restore.
                if (wasDrivenByUs && !alreadyHandedBack)
                {
                    await RestoreUndrivenFanAsync(state.FanIndex, cancellationToken).ConfigureAwait(false);
                }

                continue;
            }

            if (decision.Outcome == FanDutyOutcome.SafeFallback)
            {
                await EnterSafeFallbackAsync(state.FanIndex, cancellationToken).ConfigureAwait(false);
                continue;
            }

            if (_fansInSafeFallback.Remove(state.FanIndex))
            {
                _logger.LogInformation("Fan {FanIndex} can read a driving sensor again; resuming its curve.", state.FanIndex);
            }

            // Round to the whole percent the EC actually takes BEFORE the change-threshold check, so
            // sub-point boost jitter from idle CPU noise cannot trigger a write every evaluation.
            var targetDuty = Math.Round(decision.Duty, MidpointRounding.AwayFromZero);

            if (_lastAppliedDuty.TryGetValue(state.FanIndex, out var lastDuty)
                && Math.Abs(targetDuty - lastDuty) < DutyChangeThresholdPercent)
            {
                LogDutyUnchanged(state.FanIndex, targetDuty, lastDuty);
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

    // Source-generated so the arguments are not boxed into an object[] before the level is checked. These
    // fire once per fan per evaluation pass, so at Release verbosity — where they are filtered out — the
    // remaining cost is the level check itself.

    [LoggerMessage(
        Level = LogLevel.Trace,
        Message = "Fan {FanIndex} evaluated. Mode={Mode}, Outcome={Outcome}, TargetDuty={TargetDuty:0.#}%.")]
    private partial void LogFanDecision(int fanIndex, FanDutyOutcome outcome, double targetDuty, FanControlMode mode);

    [LoggerMessage(
        Level = LogLevel.Trace,
        Message = "Fan {FanIndex} left alone: target {TargetDuty:0.#}% is within the change threshold of the applied {LastDuty:0.#}%.")]
    private partial void LogDutyUnchanged(int fanIndex, double targetDuty, double lastDuty);

    [LoggerMessage(
        Level = LogLevel.Trace,
        Message = "Fan {FanIndex} curve evaluated. Sensors=[{SensorReadings}] Aggregation={Aggregation} => {DrivingTemperature:0.#}C; " +
                  "curve gives {CurveDuty:0.#}%, CPU usage {CpuUsagePercent:0.#}% with strength {ModifierStrength} adds {UsageBoost:0.#}pp; target {TargetDuty:0.#}%.")]
    private partial void LogCurveEvaluated(
        int fanIndex,
        string sensorReadings,
        TemperatureAggregationMode aggregation,
        double drivingTemperature,
        double curveDuty,
        double? cpuUsagePercent,
        double? modifierStrength,
        double usageBoost,
        double targetDuty);

    [LoggerMessage(
        Level = LogLevel.Trace,
        Message = "Fan {FanIndex} is curve-driven but blind. Sensors=[{SensorReadings}] Aggregation={Aggregation}, TreatMissingAsZero={TreatMissingAsZero}; handing back to firmware control.")]
    private partial void LogCurveBlind(
        int fanIndex,
        string sensorReadings,
        TemperatureAggregationMode aggregation,
        bool treatMissingAsZero);

    /// <summary>
    /// Returns a fan we were driving to EC control once nothing drives it any more.
    /// </summary>
    /// <remarks>
    /// Distinct from <see cref="EnterSafeFallbackAsync"/>: that one is a temporary handover while a sensor is
    /// unreadable and the fan is still logically curve-driven, whereas this is the permanent case — the fan's
    /// mode no longer asks us to drive it at all. Both end at the same EC restore, and neither touches the
    /// stored mode, curve or sensors.
    /// </remarks>
    private async Task RestoreUndrivenFanAsync(int fanIndex, CancellationToken cancellationToken)
    {
        try
        {
            await _frameworkDataProvider.RestoreAutoFanControlAsync(fanIndex, cancellationToken).ConfigureAwait(false);
            _fanControlStateStore.ClearAppliedDuty(fanIndex);

            _logger.LogInformation(
                "Fan {FanIndex} is no longer driven by a mode or curve; returned it to firmware fan control.",
                fanIndex);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            // Put the marker back so the next pass retries. Leaving it removed would strand the fan on our
            // duty forever after a single transient failure, which is the very state this method exists to
            // prevent. The value is stale but only ever read as "we were driving this fan".
            _lastAppliedDuty[fanIndex] = 0d;

            _logger.LogWarning(
                exception,
                "Failed to return undriven fan {FanIndex} to firmware control; it may still be held at the last duty we applied. Retrying on the next pass.",
                fanIndex);
        }
    }

    /// <summary>
    /// Hands a curve-driven fan back to the EC's own (firmware-safe) control while none of its driving sensors
    /// can be read — the promise the curve editor makes. The stored mode stays CustomCurve: this is a runtime
    /// fallback, NOT a mode change, so the profile is untouched and the curve resumes by itself as soon as a
    /// sensor reports again.
    /// </summary>
    private async Task EnterSafeFallbackAsync(int fanIndex, CancellationToken cancellationToken)
    {
        // Forget the remembered duty FIRST: whatever happens to the restore below, leaving the fallback must
        // re-apply the curve at full authority instead of being swallowed by the change threshold.
        _lastAppliedDuty.Remove(fanIndex);

        // Once per episode — after the restore the EC owns the fan, and re-issuing it every tick is noise.
        if (!_fansInSafeFallback.Add(fanIndex))
        {
            return;
        }

        try
        {
            // Deliberately the EC-level restore, NOT FrameworkFanControlStateStore.MarkAuto: MarkAuto clears the
            // mode, the curve points and the driving sensors, i.e. it would delete the user's profile to cope
            // with a sensor blinking out.
            await _frameworkDataProvider.RestoreAutoFanControlAsync(fanIndex, cancellationToken).ConfigureAwait(false);

            // The EC owns the fan now, so the duty we last wrote is no longer what it is running. Clearing it
            // keeps every client honest — a stale value here reads as "the curve is holding this speed".
            // Mode / curve / sensors are untouched, so the profile resumes intact when a sensor returns.
            _fanControlStateStore.ClearAppliedDuty(fanIndex);

            _logger.LogWarning(
                "Fan {FanIndex} has no readable driving sensor; handed back to firmware fan control until one returns.",
                fanIndex);
        }
        catch (OperationCanceledException)
        {
            _fansInSafeFallback.Remove(fanIndex);
            throw;
        }
        catch (Exception exception)
        {
            // Retry on the next pass rather than sitting out on a duty we can no longer justify.
            _fansInSafeFallback.Remove(fanIndex);
            _logger.LogWarning(exception, "Failed to hand fan {FanIndex} back to firmware control after its driving sensors became unreadable.", fanIndex);
        }
    }

    // Resolves the duty a curve-driven fan should run, walking the active slot's per-slot follow link.
    // Follow chains are walked with cycle detection; a leader that is not curve-driven contributes its
    // last applied duty (Max => 100%, Manual => last duty, Auto/unknown => no actuation, fan holds).
    // The CPU usage modifier is applied where the curve is interpolated, so a follower fan inherits its
    // leader's already-boosted duty rather than boosting twice.
    private FanDutyDecision ResolveTargetDuty(int fanIndex, FrameworkThermalSnapshot snapshot, double? cpuUsageFraction, HashSet<int> visited)
    {
        if (!_controlStates.TryGetValue(fanIndex, out var state))
        {
            return FanDutyDecision.NotDriven;
        }

        if (state.Mode != FanControlMode.CustomCurve)
        {
            return state.Mode switch
            {
                FanControlMode.Max => FanDutyDecision.Drive(100d),
                FanControlMode.Manual => state.LastDutyPercent is double manualDuty ? FanDutyDecision.Drive(manualDuty) : FanDutyDecision.NotDriven,
                _ => FanDutyDecision.NotDriven,
            };
        }

        if (!visited.Add(fanIndex))
        {
            // Follow cycle (A -> B -> A): stop rather than oscillate; this fan holds its last duty.
            return FanDutyDecision.NotDriven;
        }

        var active = state.CurveProfiles.ElementAtOrDefault(state.ActiveCurveSlot);
        if (active is { FollowFanIndex: int leaderFanIndex } && leaderFanIndex != fanIndex)
        {
            // A follower of a blind leader is blind too — it must fall back with it, not hold a stale duty.
            return ResolveTargetDuty(leaderFanIndex, snapshot, cpuUsageFraction, visited);
        }

        if (state.CustomCurvePoints.Count < 2 || state.DrivingSensorIndices.IsDefaultOrEmpty)
        {
            return FanDutyDecision.NotDriven;
        }

        var temperature = AggregateDrivingTemperature(snapshot, state.DrivingSensorIndices, state.DrivingTemperatureAggregation, state.TreatMissingSensorsAsZero);
        if (temperature is not double celsius)
        {
            // The inputs matter more than the outcome here: "no driving temperature" is almost always one
            // specific sensor having stopped reporting, and without the per-sensor readings the log cannot
            // say which.
            if (_logger.IsEnabled(LogLevel.Trace))
            {
                LogCurveBlind(
                    fanIndex,
                    DescribeSensorReadings(snapshot, state.DrivingSensorIndices),
                    state.DrivingTemperatureAggregation,
                    state.TreatMissingSensorsAsZero);
            }

            // Curve-driven but BLIND: not the same as "we don't drive this fan". Holding the last duty here is
            // what the editor's "falls back to its firmware-safe curve" promise rules out — the fan would sit
            // at whatever the curve last asked for while nothing can observe the heat.
            return FanDutyDecision.SafeFallback;
        }

        var curveDuty = InterpolateDuty(state.CustomCurvePoints, celsius);
        var usageBoost = FanUsageModifierMath.ComputeBoost(state.CpuUsageModifierStrength, cpuUsageFraction);
        var targetDuty = Clamp(curveDuty + usageBoost);

        // The whole derivation in one record: which sensors were read and what each said, how they were
        // combined, the temperature that came out, the duty the curve interpolated for it, what the CPU
        // usage modifier added, and the clamped result. This is what makes "why is my fan at 45%?"
        // answerable from a log instead of a guess.
        if (_logger.IsEnabled(LogLevel.Trace))
        {
            LogCurveEvaluated(
                fanIndex,
                DescribeSensorReadings(snapshot, state.DrivingSensorIndices),
                state.DrivingTemperatureAggregation,
                celsius,
                curveDuty,
                cpuUsageFraction * 100d,
                state.CpuUsageModifierStrength,
                usageBoost,
                targetDuty);
        }

        return FanDutyDecision.Drive(targetDuty);
    }

    /// <summary>
    /// Formats each driving sensor and its current reading, e.g. <c>"0=62.0C, 3=unreadable"</c>.
    /// </summary>
    /// <remarks>
    /// Allocates, so every caller guards it behind <see cref="ILogger.IsEnabled"/> — at Release verbosity
    /// this never runs.
    /// </remarks>
    private static string DescribeSensorReadings(FrameworkThermalSnapshot snapshot, ImmutableArray<int> sensorIndices)
    {
        if (sensorIndices.IsDefaultOrEmpty)
        {
            return "none selected";
        }

        var count = Math.Min((int)snapshot.SensorCount, snapshot.Temperatures.Count);
        var descriptions = new List<string>(sensorIndices.Length);

        foreach (var sensorIndex in sensorIndices)
        {
            if (sensorIndex >= 0
                && sensorIndex < count
                && snapshot.Temperatures[sensorIndex] is { State: FrameworkDotnet.Enums.FrameworkTemperatureState.Ok } reading)
            {
                descriptions.Add(FormattableString.Invariant($"{sensorIndex}={reading.Temperature.DegreesCelsius:0.#}C"));
                continue;
            }

            // Naming the state rather than just "missing" distinguishes a powered-down sensor from one that
            // is out of range, which is the difference between "expected" and "investigate".
            var state = sensorIndex >= 0 && sensorIndex < count
                ? snapshot.Temperatures[sensorIndex].State.ToString()
                : "out of range";
            descriptions.Add(FormattableString.Invariant($"{sensorIndex}={state}"));
        }

        return string.Join(", ", descriptions);
    }

    private enum FanDutyOutcome
    {
        /// <summary>Not ours to drive (Auto, an unknown fan, or a follow cycle): leave the EC alone.</summary>
        NotDriven,

        /// <summary>Drive the fan at <see cref="FanDutyDecision.Duty"/>.</summary>
        Drive,

        /// <summary>Curve-driven but no driving temperature can be read: hand the fan back to firmware control.</summary>
        SafeFallback,
    }

    private readonly record struct FanDutyDecision(FanDutyOutcome Outcome, double Duty)
    {
        public static readonly FanDutyDecision NotDriven = new(FanDutyOutcome.NotDriven, 0d);

        public static readonly FanDutyDecision SafeFallback = new(FanDutyOutcome.SafeFallback, 0d);

        public static FanDutyDecision Drive(double duty) => new(FanDutyOutcome.Drive, duty);
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
        var snapshot = _frameworkDataProvider.GetLatestHardwareInfoSnapshot();

        // Hardware.Info retains the last successful readings across failed refreshes, and a stopped
        // hardware-info poll keeps the last snapshot forever. Treat unavailable or stale snapshots as
        // "no reading" so the smoothing filter decays the boost instead of pinning it to a frozen value.
        if (!snapshot.IsAvailable || DateTimeOffset.UtcNow - snapshot.ObservedAt > MaxCpuUsageSnapshotAge)
        {
            return null;
        }

        var cpus = snapshot.Runtime.Cpus;
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

    // Warns once when fans have a usage modifier configured but no CPU usage reading has been available
    // for a sustained stretch — otherwise the modifier is silently inert (enabled on the wire, zero effect).
    private void ReportMissingUsageIfModifiersInert(double? cpuUsageFraction)
    {
        if (cpuUsageFraction is not null)
        {
            if (_missingUsageWarningLogged)
            {
                _logger.LogInformation("CPU usage readings are available again. Fan usage modifiers are active.");
            }

            _consecutiveMissingUsageEvaluations = 0;
            _missingUsageWarningLogged = false;
            return;
        }

        if (_missingUsageWarningLogged || _controlStates.Values.All(static state => state.CpuUsageModifierStrength is null))
        {
            return;
        }

        if (++_consecutiveMissingUsageEvaluations >= MissingUsageWarningThreshold)
        {
            _missingUsageWarningLogged = true;
            _logger.LogWarning(
                "No CPU usage reading has been available for {Evaluations} evaluations, but at least one fan has a CPU usage modifier configured. The modifier is inactive until hardware-info readings return.",
                _consecutiveMissingUsageEvaluations);
        }
    }

    private static double? AggregateDrivingTemperature(FrameworkThermalSnapshot snapshot, ImmutableArray<int> sensorIndices, TemperatureAggregationMode aggregation, bool treatMissingSensorsAsZero)
    {
        // A sensor that is not reporting Ok has NO reading. It used to be folded in at whatever the EC left in
        // the array — typically 0 °C for a powered-down GPU — which silently halved an Average and under-cooled
        // the machine. Missing is now explicit, and the profile decides whether it counts as 0 °C or is skipped.
        var count = Math.Min((int)snapshot.SensorCount, snapshot.Temperatures.Count);
        var readings = new List<double?>(sensorIndices.Length);
        foreach (var sensorIndex in sensorIndices)
        {
            double? celsius = null;
            if (sensorIndex >= 0
                && sensorIndex < count
                && snapshot.Temperatures[sensorIndex] is { State: FrameworkDotnet.Enums.FrameworkTemperatureState.Ok } reading)
            {
                celsius = reading.Temperature.DegreesCelsius;
            }

            readings.Add(celsius);
        }

        return FanDrivingTemperature.Aggregate(readings, aggregation, treatMissingSensorsAsZero);
    }

    /// <summary>
    /// Interpolates the duty for a temperature. Deliberately delegates to the SAME implementation the client
    /// uses to draw the curve and predict its duty — a second copy here is how a preview ends up promising one
    /// speed while the fan does another.
    /// </summary>
    private static double InterpolateDuty(ImmutableSortedDictionary<int, double> curvePoints, double temperatureCelsius)
        => FanCurveDomain.InterpolateDuty(
            curvePoints.Select(static pair => (pair.Key, pair.Value)),
            temperatureCelsius);

    private static double Clamp(double duty) => Math.Clamp(duty, 0d, 100d);
}
