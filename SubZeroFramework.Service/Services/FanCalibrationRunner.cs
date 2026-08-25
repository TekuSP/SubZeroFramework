using System.Diagnostics;

using FrameworkDotnet.Enums;
using FrameworkDotnet.Snapshots;

using SubZeroFramework.Models;
using SubZeroFramework.Service.Models;
using SubZeroFramework.Services;
using SubZeroFramework.Services.Control;

namespace SubZeroFramework.Service.Services;

/// <summary>
/// Runs the hot test that identifies a fan's thermal model: load the machine, step the fan, watch the
/// temperature fall, fit K, τ and L.
/// </summary>
/// <remarks>
/// <para>
/// <b>This is the most physically consequential code in the service.</b> It deliberately heats the machine
/// and drives a fan to extremes, so every exit path — success, failure, cancellation, the client vanishing —
/// must stop the load and hand the fan back. The whole run therefore lives inside one try/finally, and the
/// finally does not depend on anything the run may have corrupted.
/// </para>
/// <para>
/// Only one run at a time, machine-wide. Two concurrent runs would heat the same chassis while each assumed
/// it owned the thermal conditions, and both models would be wrong in ways nothing downstream could detect.
/// </para>
/// </remarks>
public sealed class FanCalibrationRunner : IDisposable
{
    /// <summary>Hard ceiling, checked on every sample of every step ahead of all other logic.</summary>
    /// <remarks>Defined in <see cref="FanCalibrationLimits"/> so the UI can quote the same number.</remarks>
    public const double SafetyCeilingCelsius = FanCalibrationLimits.SafetyCeilingCelsius;

    /// <summary>Average package power the run must reach for the result to mean anything.</summary>
    public const double MinimumAveragePowerWatts = FanCalibrationLimits.MinimumAveragePowerWatts;

    /// <summary>The low duty the fan is held at while heat builds, before the step.</summary>
    /// <remarks>
    /// Low enough to let the machine get properly hot — which is what makes the subsequent fall large and
    /// therefore measurable — but above a typical stall point so the fan is genuinely turning throughout.
    /// </remarks>
    public const double PreStepDutyPercent = 22d;

    /// <summary>
    /// Total spread, in °C, that a settle window may cover.
    /// </summary>
    /// <remarks>
    /// Wider than one quantisation step on purpose. A genuinely steady EC reading still flickers between two
    /// adjacent whole degrees, so a threshold under 1 °C would never be met and every run would sit out the
    /// full settle timeout.
    /// </remarks>
    private const double SettledRangeCelsius = 1.5d;

    /// <summary>
    /// How many consecutive samples may report no driving temperature before the run gives up.
    /// </summary>
    /// <remarks>
    /// Small on purpose. A run that cannot see the temperature it is controlling to is heating the machine
    /// with nothing watching, and the correct amount of time to spend in that state is barely any. A few
    /// samples of tolerance covers an ordinary dropped read; anything more is a failed sensor.
    /// </remarks>
    private const int BlindSampleLimit = 5;

    /// <summary>
    /// How old the newest thermal reading may be before the run gives up.
    /// </summary>
    /// <remarks>
    /// Generous against the slowest polling tier — the primary tier can be seconds — while still far shorter
    /// than the minutes a stalled stream would otherwise go unnoticed for. The run heats the machine the
    /// whole time it is waiting, so the tolerance for "no idea how hot it is" is small.
    /// </remarks>
    private static readonly TimeSpan StaleTelemetryLimit = TimeSpan.FromSeconds(15);

    /// <summary>
    /// The intermediate duties visited to measure how cooling varies across the range.
    /// </summary>
    /// <remarks>
    /// Three, not ten. Together with the two the run already settles at — the pre-step duty and full — they
    /// make five points, which is enough to capture a curve whose whole shape is "steep at the bottom,
    /// flattening at the top". Every extra level costs another dwell on a deliberately loaded machine, and
    /// the shape does not have detail that more points would reveal.
    /// </remarks>
    private static readonly double[] GainCurveDutyPercents = [80d, 60d, 40d];

    private readonly IFrameworkDataProvider _frameworkDataProvider;
    private readonly FrameworkFanControlStateStore _fanControlStateStore;
    private readonly ICpuLoadGenerator _loadGenerator;
    private readonly IGpuLoadGenerator _gpuLoadGenerator;
    private readonly FanCalibrationArbiter _arbiter;
    private readonly FanCalibrationTimings _timings;
    private readonly FanCalibrationSchedule _schedule;
    private readonly ILogger<FanCalibrationRunner> _logger;
    private bool _disposed;

    public FanCalibrationRunner(
        IFrameworkDataProvider frameworkDataProvider,
        FrameworkFanControlStateStore fanControlStateStore,
        ICpuLoadGenerator loadGenerator,
        IGpuLoadGenerator gpuLoadGenerator,
        FanCalibrationArbiter arbiter,
        ILogger<FanCalibrationRunner> logger,
        FanCalibrationTimings? timings = null)
    {
        ArgumentNullException.ThrowIfNull(frameworkDataProvider);
        ArgumentNullException.ThrowIfNull(fanControlStateStore);
        ArgumentNullException.ThrowIfNull(loadGenerator);
        ArgumentNullException.ThrowIfNull(gpuLoadGenerator);
        ArgumentNullException.ThrowIfNull(arbiter);
        ArgumentNullException.ThrowIfNull(logger);

        _frameworkDataProvider = frameworkDataProvider;
        _fanControlStateStore = fanControlStateStore;
        _loadGenerator = loadGenerator;
        _gpuLoadGenerator = gpuLoadGenerator;
        _arbiter = arbiter;
        _logger = logger;
        _timings = timings ?? FanCalibrationTimings.Default;
        _schedule = new FanCalibrationSchedule(_timings);
    }

    /// <summary>True while a run is in progress anywhere on this machine.</summary>
    public bool IsRunning => _arbiter.ClaimedFanIndex is not null;

    /// <summary>
    /// Runs a calibration, reporting progress as it goes.
    /// </summary>
    /// <param name="fanIndex">The fan to calibrate.</param>
    /// <param name="drivingSensorIndices">The sensors whose aggregate is the driving temperature.</param>
    /// <param name="onProgress">Called for each update; must not throw.</param>
    /// <param name="cancellationToken">Cancels the run. The fan is restored regardless.</param>
    /// <returns>The identified model, or why it could not be produced.</returns>
    public async Task<FanCalibrationRunResult> RunAsync(
        int fanIndex,
        IReadOnlyCollection<int> drivingSensorIndices,
        Func<FanCalibrationProgress, Task> onProgress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(drivingSensorIndices);
        ArgumentNullException.ThrowIfNull(onProgress);
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (drivingSensorIndices.Count == 0)
        {
            return Failed(fanIndex, FanCalibrationFailure.InsufficientData, FanCalibrationStep.None, TimeSpan.Zero, restored: true);
        }

        // Claiming is what stops the curve worker driving this fan, so it must happen before the first duty
        // command and must not be released until after the last one.
        if (!_arbiter.TryClaim(fanIndex))
        {
            throw new InvalidOperationException("A calibration is already running on this machine.");
        }

        var session = new RunSession(fanIndex, drivingSensorIndices, onProgress, this);

        try
        {
            return await session.ExecuteAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            // Each step is INDEPENDENTLY guarded, and the fan comes first.
            //
            // Sequencing them bare meant one throwing skipped everything after it: a load generator failing
            // to stop would leave the fan pinned wherever the run put it — 0% from the minimum-spin walk, or
            // 100% from the step — and leave the machine-wide claim held, which permanently stops the curve
            // worker touching that fan and blocks every future calibration. The restore is the promise this
            // class exists to keep, so it runs first and nothing can get in front of it.
            await SafelyAsync(session.RestoreFanAsync, "restore the fan").ConfigureAwait(false);
            Safely(_loadGenerator.Stop, "stop CPU load");
            Safely(_gpuLoadGenerator.Stop, "stop GPU load");

            // Released last, so nothing else can drive the fan until it is back under automatic control.
            _arbiter.Release(fanIndex);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        // BOTH generators. Disposal is an exit path like any other, and stopping only the processor would
        // leave a GPU calibration dispatching at full rate after the runner that owns it is gone.
        Safely(_loadGenerator.Stop, "stop CPU load");
        Safely(_gpuLoadGenerator.Stop, "stop GPU load");
    }

    /// <summary>
    /// Runs a cleanup step, logging rather than propagating.
    /// </summary>
    /// <remarks>
    /// Cleanup runs on every exit path INCLUDING the ones already reporting a failure. Letting one step throw
    /// would both replace a useful error with a meaningless one and skip every step after it — which is how a
    /// fan gets left pinned.
    /// </remarks>
    private void Safely(Action step, string description)
    {
        try
        {
            step();
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Failed to {Description} after a calibration.", description);
        }
    }

    private async Task SafelyAsync(Func<Task> step, string description)
    {
        try
        {
            await step().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Failed to {Description} after a calibration.", description);
        }
    }

    private static FanCalibrationRunResult Failed(
        int fanIndex,
        FanCalibrationFailure failure,
        FanCalibrationStep stoppedAt,
        TimeSpan duration,
        bool restored,
        double? averagePower = null,
        double? swing = null,
        double? peak = null)
        => new()
        {
            FanIndex = fanIndex,
            Succeeded = false,
            Failure = failure,
            StoppedAt = stoppedAt,
            Duration = duration,
            FansRestored = restored,
            AveragePackagePowerWatts = averagePower,
            TemperatureSwingCelsius = swing,
            PeakTemperatureCelsius = peak,
        };

    /// <summary>One run's mutable state, so the runner itself stays re-entrant-safe by construction.</summary>
    private sealed class RunSession(
        int fanIndex,
        IReadOnlyCollection<int> drivingSensorIndices,
        Func<FanCalibrationProgress, Task> onProgress,
        FanCalibrationRunner owner)
    {
        private readonly Stopwatch _elapsed = Stopwatch.StartNew();
        private readonly List<double> _powerSamples = [];
        private double _peakCelsius;
        private bool _fanWasDriven;

        /// <summary>Consecutive samples where no driving sensor reported. Reset by any successful reading.</summary>
        private int _blindSamples;

        /// <summary>The duty last commanded, for the live plot. Null until the run first drives the fan.</summary>
        private double? _commandedDutyPercent;

        /// <summary>What the machine settled at under load at the pre-step duty — the hot end of the gain curve.</summary>
        private double? _settledAtLowDuty;

        private FanCalibrationStep _reportedStep = FanCalibrationStep.None;
        private TimeSpan _stepStartedAt = TimeSpan.Zero;

        // Speed at each of the two operating points the run creates. Kept separately rather than as one
        // series because only the settled ends are comparable — the transition between them is the machine
        // on its way somewhere, not a speed it sustains.
        private readonly List<double> _cpuRatioAtLowDuty = [];
        private readonly List<double> _cpuRatioAtFullDuty = [];
        private readonly List<double> _gpuClockAtLowDuty = [];
        private readonly List<double> _gpuClockAtFullDuty = [];

        /// <summary>
        /// Files a speed reading under the duty it was taken at.
        /// </summary>
        /// <remarks>
        /// Only the two settled steps count. Readings taken while the fan is spinning up belong to neither
        /// operating point, and averaging them into either would understate the difference between them —
        /// making the cooling look less useful than it is.
        /// </remarks>
        private void RecordSpeed(FanCalibrationStep step, ControlTelemetrySample sample)
        {
            var (cpuTarget, gpuTarget) = step switch
            {
                FanCalibrationStep.LoadingAndSettling => (_cpuRatioAtLowDuty, _gpuClockAtLowDuty),
                FanCalibrationStep.MeasuringResponse => (_cpuRatioAtFullDuty, _gpuClockAtFullDuty),
                _ => (null, null),
            };

            if (cpuTarget is null || gpuTarget is null)
            {
                return;
            }

            // Only the LOADED component's speed is recorded. Filing both would let a GPU run report the idle
            // processor's clock as "what the extra fan bought" — a number about a component this run never
            // heated and this fan does not cool, presented as the headline answer.
            var loading = ResolveLoadTarget();

            if (loading != ThermalLoadTarget.Gpu
                && sample.CpuPerformanceRatio is double ratio
                && double.IsFinite(ratio)
                && ratio > 0d)
            {
                cpuTarget.Add(ratio);
            }

            if (loading == ThermalLoadTarget.Gpu
                && sample.GpuCoreClockMegahertz is double megahertz
                && double.IsFinite(megahertz)
                && megahertz > 0d)
            {
                gpuTarget.Add(megahertz);
            }
        }

        /// <summary>
        /// Builds the speed comparison from the settled TAIL of each step.
        /// </summary>
        /// <remarks>
        /// The tail, not the whole step: after the fan steps to full the machine spends a while accelerating
        /// back up, and including that stretch would average the recovery in with the speed finally reached —
        /// under-reporting the gain by exactly the amount that makes it interesting.
        /// </remarks>
        private FanPerformanceResponse BuildPerformanceResponse()
        {
            return new FanPerformanceResponse
            {
                LowDutyPercent = PreStepDutyPercent,
                FullDutyPercent = 100d,
                CpuPerformanceRatioAtLowDuty = SettledMean(_cpuRatioAtLowDuty),
                CpuPerformanceRatioAtFullDuty = SettledMean(_cpuRatioAtFullDuty),
                GpuCoreClockAtLowDutyMegahertz = SettledMean(_gpuClockAtLowDuty),
                GpuCoreClockAtFullDutyMegahertz = SettledMean(_gpuClockAtFullDuty),
            };
        }

        /// <summary>The mean of the last third of a series, or null when there is too little to be meaningful.</summary>
        private static double? SettledMean(List<double> samples)
        {
            if (samples.Count < 3)
            {
                return null;
            }

            var tailCount = Math.Max(1, samples.Count / 3);
            return samples.Skip(samples.Count - tailCount).Average();
        }

        /// <summary>The thermal readings the whole run is measured against; lives only as long as the run.</summary>
        private LatestSnapshotCache<FrameworkThermalSnapshot>? _snapshots;

        /// <summary>Where the machine's power is coming from, watched for the whole run.</summary>
        private LatestSnapshotCache<FrameworkPowerSnapshot>? _power;

        public async Task<FanCalibrationRunResult> ExecuteAsync(CancellationToken cancellationToken)
        {
            using var snapshots = new LatestSnapshotCache<FrameworkThermalSnapshot>(owner._frameworkDataProvider.ThermalSnapshots);
            using var power = new LatestSnapshotCache<FrameworkPowerSnapshot>(owner._frameworkDataProvider.PowerSnapshots);
            _snapshots = snapshots;
            _power = power;

            try
            {
                return await ExecuteCoreAsync(cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                // Cleared before the caches are disposed. A disposed cache still returns its last snapshot —
                // indistinguishable from a live reading — so leaving the fields set would let any later code
                // path read a frozen value and believe it current.
                _snapshots = null;
                _power = null;
            }
        }

        /// <summary>
        /// Starts whichever load this fan's sensors actually respond to.
        /// </summary>
        /// <remarks>
        /// Refusing outright when the required GPU load is unavailable is the whole point. The alternative —
        /// running CPU load anyway — would heat something this fan does not cool, and the run would either
        /// fail confusingly minutes later or, worse, fit a model to whatever incidental coupling exists and
        /// hand the controller a confident description of the wrong thing.
        /// </remarks>
        private FanCalibrationRunResult? StartLoad()
        {
            if (ResolveLoadTarget() == ThermalLoadTarget.Gpu)
            {
                if (!owner._gpuLoadGenerator.IsAvailable || !owner._gpuLoadGenerator.Start())
                {
                    owner._logger.LogWarning(
                        "Cannot calibrate fan {FanIndex}: it cools the GPU, and no GPU accelerator is available to load.",
                        fanIndex);

                    return Failed(fanIndex, FanCalibrationFailure.GpuLoadUnavailable, FanCalibrationStep.LoadingAndSettling, _elapsed.Elapsed, restored: true, peak: _peakCelsius);
                }

                owner._logger.LogInformation(
                    "Calibrating fan {FanIndex} under GPU load ({Accelerator}).",
                    fanIndex,
                    owner._gpuLoadGenerator.AcceleratorName);

                return null;
            }

            // CPU load for everything else, including fans whose role and sensors say nothing identifiable.
            // It is the one heat source every machine has, and an unattributable fan is far more likely to be
            // cooling the processor than nothing at all.
            owner._loadGenerator.Start();
            owner._logger.LogInformation("Calibrating fan {FanIndex} under CPU load.", fanIndex);
            return null;
        }

        /// <summary>
        /// The ONE thing this run heats.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>Exactly one, never both.</b> CPU and GPU share a power budget and a chassis: run them together
        /// and each one's draw cuts the other's limit while both dump heat into the same air. Neither
        /// component reaches the state the model is supposed to describe, and the fan is calibrated for a
        /// condition that only exists during calibration.
        /// </para>
        /// <para>
        /// The fan's cooling role decides it, because that is the physical fact — the right fan on a
        /// Framework 16 cools the GPU whichever sensors the user has pointed it at. The sensors are consulted
        /// only when the platform does not name the role, where they are the best evidence available.
        /// </para>
        /// </remarks>
        private ThermalLoadTarget ResolveLoadTarget()
        {
            var role = owner._fanControlStateStore.GetState(fanIndex)?.CoolingRole ?? FanCoolingRole.Unknown;

            switch (role)
            {
                case FanCoolingRole.Gpu:
                    return ThermalLoadTarget.Gpu;
                case FanCoolingRole.Cpu:
                    return ThermalLoadTarget.Cpu;
            }

            var snapshot = _snapshots?.Latest;
            if (snapshot is null)
            {
                return ThermalLoadTarget.None;
            }

            var names = drivingSensorIndices
                .Where(index => index >= 0 && index < snapshot.Temperatures.Count)
                .Select(index => snapshot.Temperatures[index].Name);

            // Sensors can name both. GPU wins that tie: a fan watching any GPU sensor is being asked to hold
            // a temperature only GPU load produces, and CPU load would leave it flat.
            var fromSensors = ThermalLoadTargetResolver.Resolve(names);
            return fromSensors.HasFlag(ThermalLoadTarget.Gpu) ? ThermalLoadTarget.Gpu : fromSensors;
        }

        /// <summary>
        /// True when the machine is positively known to be running on battery.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Calibrating on battery is worse than merely wasteful. A processor on battery runs to different
        /// power limits than the same processor on AC, so the run would identify a model for a machine that
        /// only exists while unplugged — and then the adaptive controller would use it for the plugged-in case
        /// too. On top of that the run deliberately holds every core at full load for several minutes.
        /// </para>
        /// <para>
        /// <b>Fails open, deliberately.</b> Only a positive reading counts: a machine whose EC reports no power
        /// source at all, and whose battery is idle, is allowed to proceed. Refusing on missing information
        /// would permanently lock calibration out of any machine that does not populate these fields, and the
        /// cost of wrongly allowing a run is a wasted few minutes rather than damage.
        /// </para>
        /// </remarks>
        private bool IsOnBattery()
        {
            if (_power?.Latest is not { } snapshot)
            {
                return false;
            }

            // AC attached settles it, whatever the batteries are doing. ChargingAndDischarging happens under
            // a load the adapter alone cannot carry — the charger IS attached, so this is not "on battery".
            if (snapshot.PowerSourceState is FrameworkPowerSourceState.AcOnly or FrameworkPowerSourceState.AcAndBattery)
            {
                return false;
            }

            if (snapshot.PowerSourceState == FrameworkPowerSourceState.BatteryOnly)
            {
                return true;
            }

            // No usable power-source reading: fall back to the batteries themselves. Something actively
            // draining with no charger reported is the definition of running on battery.
            return snapshot.ReportedBatteries.Any(static battery =>
                battery.BatteryState is FrameworkBatteryState.Discharging or FrameworkBatteryState.Critical);
        }

        private async Task<FanCalibrationRunResult> ExecuteCoreAsync(CancellationToken cancellationToken)
        {
            owner._logger.LogInformation("Starting calibration for fan {FanIndex}.", fanIndex);

            // 1 — settle at idle, so the baseline is a real idle rather than the tail of whatever came before.
            owner._loadGenerator.Stop();
            if (await RunStepAsync(FanCalibrationStep.SettlingAtIdle, owner._timings.IdleSettle, cancellationToken).ConfigureAwait(false) is { } idleAbort)
            {
                return idleAbort;
            }

            // The last moment before anything is driven or heated, and the first at which the power source is
            // reliably known — settling at idle is long enough for the stream to have produced a reading.
            // The per-sample check cannot stand in for this one: the minimum-spin walk commands a duty BEFORE
            // its first sample, so a run started on battery would already have moved the fan by the time that
            // check next ran.
            if (IsOnBattery())
            {
                owner._logger.LogWarning("Refusing to calibrate fan {FanIndex}: the machine is running on battery.", fanIndex);
                return Failed(fanIndex, FanCalibrationFailure.OnBattery, FanCalibrationStep.SettlingAtIdle, _elapsed.Elapsed, restored: true, peak: _peakCelsius);
            }

            // 2 — minimum spin. Walked down rather than searched, because the interesting quantity is where
            // the fan STOPS reliably turning, and that is only observable by going there.
            var minimumSpin = await FindMinimumSpinAsync(cancellationToken).ConfigureAwait(false);
            if (minimumSpin.Abort is { } spinAbort)
            {
                return spinAbort;
            }

            // 3 — load, and hold a low duty until the temperature stops climbing. The fit assumes the only
            // thing that changes at the step is duty, so everything else has to be steady first.
            _fanWasDriven = true;
            await SetDutyAsync(PreStepDutyPercent, cancellationToken).ConfigureAwait(false);

            if (StartLoad() is { } loadFailure)
            {
                return loadFailure;
            }

            if (await SettleUnderLoadAsync(cancellationToken).ConfigureAwait(false) is { } loadAbort)
            {
                return loadAbort;
            }

            var averagePower = _powerSamples.Count > 0 ? _powerSamples.Average() : 0d;
            if (averagePower < MinimumAveragePowerWatts)
            {
                owner._logger.LogWarning(
                    "Calibration for fan {FanIndex} produced only {AveragePower:0.#} W on average; {Required} W is needed.",
                    fanIndex,
                    averagePower,
                    MinimumAveragePowerWatts);

                return Failed(fanIndex, FanCalibrationFailure.InsufficientLoad, FanCalibrationStep.LoadingAndSettling, _elapsed.Elapsed, restored: true, averagePower: averagePower, peak: _peakCelsius);
            }

            // 4/5 — the step, then record the fall it produces.
            await ReportAsync(FanCalibrationStep.SteppingFan, stepMarker: true).ConfigureAwait(false);
            await SetDutyAsync(100d, cancellationToken).ConfigureAwait(false);

            var response = await RecordResponseAsync(cancellationToken).ConfigureAwait(false);
            if (response.Abort is { } responseAbort)
            {
                return responseAbort;
            }

            // 6 — fit.
            await ReportAsync(FanCalibrationStep.FittingModel).ConfigureAwait(false);
            var dutyStep = 100d - PreStepDutyPercent;
            var fit = FopdtIdentification.Identify(response.Samples, dutyStep);

            if (!fit.IsSuccess)
            {
                return Failed(fanIndex, fit.Failure, FanCalibrationStep.FittingModel, _elapsed.Elapsed, restored: true, averagePower: averagePower, swing: fit.TemperatureSwingCelsius, peak: _peakCelsius);
            }

            // 7 — does the EC actually hold a commanded speed?
            await ReportAsync(FanCalibrationStep.VerifyingSpeedTracking).ConfigureAwait(false);
            var tracking = await VerifySpeedTrackingAsync(response.MaximumRpm, cancellationToken).ConfigureAwait(false);

            // 8 — how cooling varies across the duty range. Last, because it uses the τ from the fit to
            // extrapolate each level instead of waiting it out.
            var gainCurve = await MeasureGainCurveAsync(fit, response.SettledCelsius, cancellationToken).ConfigureAwait(false);

            var calibration = BuildCalibration(fit, minimumSpin, response.MaximumRpm, averagePower, tracking, gainCurve);

            owner._fanControlStateStore.SetCalibration(fanIndex, calibration);
            owner._logger.LogInformation(
                "Calibrated fan {FanIndex}: K={ProcessGain:0.###} C/%, tau={TimeConstant:0.#} s, L={DeadTime:0.#} s, tracking={Tracking}.",
                fanIndex,
                calibration.ProcessGainCelsiusPerPercent,
                calibration.TimeConstantSeconds,
                calibration.DeadTimeSeconds,
                calibration.TrackingMode);

            await ReportAsync(FanCalibrationStep.Completed).ConfigureAwait(false);

            return new FanCalibrationRunResult
            {
                FanIndex = fanIndex,
                Succeeded = true,
                Calibration = calibration,
                StoppedAt = FanCalibrationStep.Completed,
                Duration = _elapsed.Elapsed,
                AveragePackagePowerWatts = averagePower,
                TemperatureSwingCelsius = fit.TemperatureSwingCelsius,
                PeakTemperatureCelsius = _peakCelsius,
                FansRestored = true,
            };
        }

        /// <summary>
        /// Hands the fan back to whatever was driving it before the run.
        /// </summary>
        /// <remarks>
        /// Deliberately swallows everything. This is the last thing that runs on every path, including the
        /// ones already reporting a failure, and letting it throw would replace a useful error with a
        /// meaningless one while ALSO leaving the fan overridden.
        /// </remarks>
        public async Task RestoreFanAsync()
        {
            if (!_fanWasDriven)
            {
                return;
            }

            try
            {
                await owner._frameworkDataProvider.RestoreAutoFanControlAsync(fanIndex, CancellationToken.None).ConfigureAwait(false);
                owner._logger.LogInformation("Restored fan {FanIndex} to automatic control after calibration.", fanIndex);
            }
            catch (Exception exception)
            {
                owner._logger.LogError(exception, "Failed to restore fan {FanIndex} after calibration. The fan may still be overridden.", fanIndex);
            }
        }

        private FanCalibrationSnapshot BuildCalibration(
            FopdtIdentificationResult fit,
            MinimumSpinResult minimumSpin,
            double maximumRpm,
            double averagePower,
            FanSpeedTrackingMode tracking,
            FanGainCurve gainCurve)
        {
            // Feed-forward from what the run actually observed: the duty needed to hold the temperature at
            // the measured load. Derived here rather than guessed, so it already carries this chassis.
            var feedForward = averagePower > 0d ? PreStepDutyPercent / averagePower : 0d;

            var calibration = new FanCalibrationSnapshot
            {
                State = FanCalibrationState.Ok,
                CalibratedAt = DateTimeOffset.UtcNow,
                ProcessGainCelsiusPerPercent = fit.ProcessGainCelsiusPerPercent,
                TimeConstantSeconds = fit.TimeConstantSeconds,
                DeadTimeSeconds = fit.DeadTimeSeconds,
                MinimumSpinRpm = minimumSpin.Rpm,
                MinimumSpinDutyPercent = minimumSpin.DutyPercent,
                MaximumRpm = maximumRpm,
                FeedForwardDutyPerWatt = feedForward,
                TrackingMode = tracking,
                PerformanceResponse = BuildPerformanceResponse(),
                GainCurve = gainCurve,
            };

            // Stored for display only. The controller re-derives gains every tick from K, τ and L through the
            // same rule, because λ is a live user setting — persisting gains as the authority would freeze the
            // tuning at whatever λ happened to be the day the calibration ran.
            var gains = AdaptivePidTuning.Compute(calibration);

            return calibration with
            {
                ProportionalGain = gains.ProportionalGain,
                IntegralGain = gains.IntegralGain,
            };
        }

        private async Task<FanCalibrationRunResult?> RunStepAsync(
            FanCalibrationStep step,
            TimeSpan duration,
            CancellationToken cancellationToken)
        {
            var deadline = _elapsed.Elapsed + duration;

            while (_elapsed.Elapsed < deadline)
            {
                if (await SampleAsync(step, cancellationToken).ConfigureAwait(false) is { Abort: { } abort })
                {
                    return abort;
                }
            }

            return null;
        }

        /// <summary>
        /// Takes one sample, reports it, waits out the interval, and hands back what it read.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The ceiling check happens here, on every sample of every step, before anything else — so no step
        /// can accidentally omit it by forgetting to call something.
        /// </para>
        /// <para>
        /// <b>The reading is returned rather than re-read by the caller</b>, and it is stamped with the moment
        /// it was taken, before the interval delay. A caller that called this and then read the temperature
        /// itself would be recording a value one whole interval younger than the timestamp it files it under —
        /// which, on the step response, silently discards the fastest part of the fall and biases K low.
        /// </para>
        /// </remarks>
        private async Task<SampleResult> SampleAsync(FanCalibrationStep step, CancellationToken cancellationToken)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return SampleResult.Aborted(Failed(fanIndex, FanCalibrationFailure.Cancelled, step, _elapsed.Elapsed, restored: true, peak: _peakCelsius));
            }

            // A dead telemetry stream leaves the last snapshot in place looking current. Checked by AGE
            // rather than by fault: the provider never errors this stream on a failed EC read and ends it
            // cleanly on shutdown, so a stall produces no notification whatsoever. Age is the only signal
            // that covers a stall, a completion and a fault alike — and without it a frozen reading silently
            // disables both the safety ceiling and the blind-sample backstop below, on a machine this run is
            // deliberately holding at full load.
            if (_snapshots is null || _snapshots.IsStale(StaleTelemetryLimit))
            {
                owner._logger.LogError(
                    "Aborting calibration for fan {FanIndex}: no thermal reading for {Age:0.#} s.",
                    fanIndex,
                    _snapshots?.Age.TotalSeconds ?? 0d);

                return SampleResult.Aborted(Failed(fanIndex, FanCalibrationFailure.InsufficientData, step, _elapsed.Elapsed, restored: true, peak: _peakCelsius));
            }

            // Checked every sample, not just at the start. Unplugging mid-run moves the processor onto
            // different power limits, so everything measured before the unplug and everything after describe
            // two different machines — and a fit across the two describes neither.
            if (IsOnBattery())
            {
                owner._logger.LogWarning("Aborting calibration for fan {FanIndex}: the machine is running on battery.", fanIndex);
                return SampleResult.Aborted(Failed(fanIndex, FanCalibrationFailure.OnBattery, step, _elapsed.Elapsed, restored: true, peak: _peakCelsius));
            }

            var takenAt = _elapsed.Elapsed;
            var temperature = ReadDrivingTemperature();
            var speed = ReadSpeedRpm();

            // Safety watches EVERY sensor, not just the ones being fitted against. The two are different
            // questions: the driving sensors decide what the MODEL describes, but the run heats the whole
            // machine, and a fan pinned low while something else cooks is exactly what the ceiling exists to
            // stop. Watching only the driving sensors would miss it entirely — calibrating the GPU fan against
            // GPU sensors leaves the CPU's temperature unwatched by this run.
            if (ReadHottestSensor() is { } hottest)
            {
                _peakCelsius = Math.Max(_peakCelsius, hottest.Celsius);

                if (hottest.Celsius >= SafetyCeilingCelsius)
                {
                    owner._logger.LogWarning(
                        "Aborting calibration for fan {FanIndex}: sensor {SensorIndex} ({SensorName}) reached {Celsius:0.#} C, at or above the {Ceiling} C safety ceiling.",
                        fanIndex,
                        hottest.Index,
                        hottest.Name,
                        hottest.Celsius,
                        SafetyCeilingCelsius);

                    return SampleResult.Aborted(Failed(fanIndex, FanCalibrationFailure.TemperatureCeiling, step, _elapsed.Elapsed, restored: true, peak: _peakCelsius));
                }
            }

            // A run that cannot read the temperature it is controlling to is heating the machine blind. The
            // ceiling above only fires on a reading, so without this a failed sensor would leave full load and
            // a deliberately low fan running for the rest of the test with nothing watching at all.
            if (temperature is null)
            {
                _blindSamples++;

                if (_blindSamples >= BlindSampleLimit)
                {
                    owner._logger.LogError(
                        "Aborting calibration for fan {FanIndex}: no driving sensor has reported for {Count} consecutive samples.",
                        fanIndex,
                        _blindSamples);

                    return SampleResult.Aborted(Failed(fanIndex, FanCalibrationFailure.InsufficientData, step, _elapsed.Elapsed, restored: true, peak: _peakCelsius));
                }
            }
            else
            {
                _blindSamples = 0;
            }

            var control = owner._frameworkDataProvider.GetLatestControlTelemetry();

            // Read the power of whatever this run is actually HEATING. A GPU-cooled fan's run loads the GPU
            // and leaves the processor idle, so measuring CPU package power would see ~8 W, decide the machine
            // never got busy, and abort every single GPU calibration after minutes of heating — while the GPU
            // sat at full load the whole time. The same figure becomes the feed-forward gain, so reading the
            // wrong component would also make it duty-per-CPU-watt on a fan that cools neither.
            var power = ResolveLoadTarget() == ThermalLoadTarget.Gpu
                ? control.Sample.GpuPowerWatts ?? control.Sample.SystemPowerWatts
                : control.Sample.CpuPackagePowerWatts ?? control.Sample.SystemPowerWatts;

            // Only the loaded steps count. Idle and minimum-spin readings would drag the average down, and
            // because feed-forward is derived as duty-per-watt, a too-low average produces a too-HIGH gain —
            // the direction that overshoots. Averaging the whole run would bias every machine toward it.
            if (power is double watts && step is FanCalibrationStep.LoadingAndSettling or FanCalibrationStep.MeasuringResponse)
            {
                _powerSamples.Add(watts);
            }

            // Speed, recorded against the same two operating points the thermal fit uses. The step response
            // already holds load constant and sweeps duty from the pre-step value to 100%, so this costs one
            // extra reading per sample and answers what the cooling actually bought.
            RecordSpeed(step, control.Sample);

            await ReportAsync(step, temperature: temperature, speed: speed, power: power).ConfigureAwait(false);

            try
            {
                await Task.Delay(owner._timings.SampleInterval, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return SampleResult.Aborted(Failed(fanIndex, FanCalibrationFailure.Cancelled, step, _elapsed.Elapsed, restored: true, peak: _peakCelsius));
            }

            return new SampleResult(null, takenAt, temperature, speed);
        }

        private async Task ReportAsync(
            FanCalibrationStep step,
            double? temperature = null,
            double? speed = null,
            double? power = null,
            bool stepMarker = false)
        {
            // Each step's own clock, so progress within a step is measured from when that step began rather
            // than from the start of the run.
            if (step != _reportedStep)
            {
                _reportedStep = step;
                _stepStartedAt = _elapsed.Elapsed;
            }

            var elapsedInStep = _elapsed.Elapsed - _stepStartedAt;

            try
            {
                await onProgress(new FanCalibrationProgress
                {
                    FanIndex = fanIndex,
                    Step = step,
                    OverallProgress = owner._schedule.ProgressAt(step, elapsedInStep),
                    EstimatedRemaining = owner._schedule.RemainingAt(step, elapsedInStep),
                    ElapsedSeconds = _elapsed.Elapsed.TotalSeconds,
                    TemperatureCelsius = temperature ?? ReadDrivingTemperature(),
                    DutyPercent = _commandedDutyPercent,
                    SpeedRpm = speed ?? ReadSpeedRpm(),
                    PackagePowerWatts = power,
                    IsStepMarker = stepMarker,
                }).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                // A dead client must not abort the physical run half-way — the finally in RunAsync still
                // restores the fan, but only once the run actually ends.
                owner._logger.LogDebug(exception, "A calibration progress update could not be delivered.");
            }
        }

        /// <summary>
        /// The hottest of the driving sensors, which is what the fan is actually being asked to control.
        /// </summary>
        /// <remarks>
        /// Hottest rather than mean: a mean would let one cool sensor mask the one that is about to throttle,
        /// and the model would be fitted against a temperature nothing in the machine ever reaches.
        /// </remarks>
        private double? ReadDrivingTemperature()
        {
            var snapshot = _snapshots?.Latest;
            if (snapshot is null)
            {
                return null;
            }

            double? hottest = null;
            foreach (var index in drivingSensorIndices)
            {
                if (index < 0 || index >= snapshot.Temperatures.Count)
                {
                    continue;
                }

                var reading = snapshot.Temperatures[index];
                if (reading.State != FrameworkDotnet.Enums.FrameworkTemperatureState.Ok)
                {
                    continue;
                }

                var celsius = reading.Temperature.DegreesCelsius;
                hottest = hottest is double current ? Math.Max(current, celsius) : celsius;
            }

            return hottest;
        }

        /// <summary>
        /// The hottest reading anywhere on the machine, with enough identity to say what tripped the ceiling.
        /// </summary>
        /// <remarks>
        /// A blanket ceiling across every sensor is right while the only heat this run generates is CPU load —
        /// nothing else should approach it. If GPU loading is ever added, sensors like the dGPU VRM run
        /// legitimately hot under load and this will need a per-sensor limit rather than one number.
        /// </remarks>
        private (int Index, FrameworkSensorName Name, double Celsius)? ReadHottestSensor()
        {
            var snapshot = _snapshots?.Latest;
            if (snapshot is null)
            {
                return null;
            }

            (int Index, FrameworkSensorName Name, double Celsius)? hottest = null;

            for (var index = 0; index < snapshot.Temperatures.Count; index++)
            {
                var reading = snapshot.Temperatures[index];
                if (reading.State != FrameworkTemperatureState.Ok)
                {
                    continue;
                }

                var celsius = reading.Temperature.DegreesCelsius;
                if (hottest is null || celsius > hottest.Value.Celsius)
                {
                    hottest = (index, reading.Name, celsius);
                }
            }

            return hottest;
        }

        private double? ReadSpeedRpm()
        {
            var snapshot = _snapshots?.Latest;
            if (snapshot is null || fanIndex < 0 || fanIndex >= snapshot.Fans.Count)
            {
                return null;
            }

            var fan = snapshot.Fans[fanIndex];

            // A non-Ok reading is not "zero RPM" — it is no reading. Returning 0 here would make the minimum
            // spin search believe the fan had stalled and stop one step early, every time.
            return fan.FanState == FrameworkDotnet.Enums.FrameworkFanState.Ok
                ? fan.Speed.RevolutionsPerMinute
                : null;
        }

        /// <summary>
        /// Commands a duty, remembering it so progress updates can report what the fan was told.
        /// </summary>
        /// <remarks>
        /// The commanded duty is not readable back from anywhere — the tachometer reports speed, which lags
        /// it and is scaled differently. Recording it here is the only way the live plot can draw the step
        /// that the temperature curve is a response to.
        /// </remarks>
        private Task SetDutyAsync(double dutyPercent, CancellationToken cancellationToken)
        {
            _commandedDutyPercent = dutyPercent;
            return owner._frameworkDataProvider.SetFanDutyAsync(fanIndex, dutyPercent, cancellationToken);
        }

        /// <summary>
        /// Holds a low duty under load until the temperature stops climbing.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Settling is judged over a WINDOW, never against the previous sample. EC temperatures arrive
        /// quantised to whole degrees, so a machine climbing at half a degree per second still reports the
        /// same value on most consecutive one-second samples — an adjacent-sample check would read that as
        /// settled within seconds, and the run would step the fan while the baseline was still moving. The
        /// resulting fall would be the step's effect plus the climb it interrupted, and K would come out
        /// wrong.
        /// </para>
        /// <para>
        /// Timing out is deliberately NOT a failure. A machine that never entirely stops creeping still gives
        /// a perfectly usable step response, and failing it here would reject runs that would have worked.
        /// </para>
        /// </remarks>
        /// <summary>True once whichever generator is running has finished ramping up.</summary>
        private bool IsLoadSteady()
            => owner._gpuLoadGenerator.IsRunning
                ? owner._gpuLoadGenerator.IsAtTargetLoad
                : owner._loadGenerator.IsAtTargetLoad;

        private async Task<FanCalibrationRunResult?> SettleUnderLoadAsync(CancellationToken cancellationToken)
        {
            var start = _elapsed.Elapsed;
            var deadline = start + owner._timings.LoadSettleTimeout;
            List<(TimeSpan At, double Celsius)> window = [];

            while (_elapsed.Elapsed < deadline)
            {
                var sample = await SampleAsync(FanCalibrationStep.LoadingAndSettling, cancellationToken).ConfigureAwait(false);
                if (sample.Abort is { } abort)
                {
                    return abort;
                }

                if (sample.Celsius is not double celsius)
                {
                    continue;
                }

                var now = sample.TakenAt;
                window.Add((now, celsius));
                window.RemoveAll(sample => now - sample.At > owner._timings.SettleWindow);

                // Never settle early. Load takes time to reach the die and longer to reach the sensor, and
                // the first seconds after Start() look flat for reasons that have nothing to do with settling.
                // Nothing counts until the load has finished climbing. During the ramp the temperature rises
                // because the load is still growing, and any flat stretch of that rise is a coincidence, not
                // a settled machine — believing one would step the fan against a baseline still moving.
                if (!IsLoadSteady())
                {
                    window.Clear();
                    start = now;
                    continue;
                }

                if (now - start < owner._timings.MinimumLoad || window.Count < 2 || now - window[0].At < owner._timings.SettleWindow)
                {
                    continue;
                }

                var range = window.Max(sample => sample.Celsius) - window.Min(sample => sample.Celsius);
                if (range <= SettledRangeCelsius)
                {
                    // The hot end of the gain curve, taken across the settle window rather than from the last
                    // reading — the window is by definition the stretch that held still.
                    _settledAtLowDuty = window.Average(sample => sample.Celsius);
                    return null;
                }
            }

            // Timed out without settling. The last window is still the best available estimate of where it
            // was heading, and losing the whole gain curve because the machine crept slightly would be a poor
            // trade for a value that only refines tuning.
            if (window.Count > 0)
            {
                _settledAtLowDuty = window.Average(sample => sample.Celsius);
            }

            return null;
        }

        /// <summary>
        /// Walks back down through intermediate duties, measuring what each one settles at.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The two ends are already known — the run settles at the pre-step duty on the way up and at full
        /// duty on the way down — so only the middle needs visiting. Walking DOWN rather than up on purpose:
        /// each level is hotter than the last, so the run approaches the safety ceiling gradually and from
        /// below, with the ceiling check watching every sample.
        /// </para>
        /// <para>
        /// Each level is EXTRAPOLATED rather than waited out. With τ already identified, a partial transient
        /// determines the asymptote it is heading for, which turns four time constants of waiting per level
        /// into about two — the difference between a few extra minutes and a quarter of an hour.
        /// </para>
        /// <para>
        /// A failure here is not a failure of the run. The curve refines tuning; without it the controller
        /// falls back to the single averaged gain, which is what it used before this existed.
        /// </para>
        /// </remarks>
        private async Task<FanGainCurve> MeasureGainCurveAsync(
            FopdtIdentificationResult fit,
            double? settledAtFullDuty,
            CancellationToken cancellationToken)
        {
            if (_settledAtLowDuty is not double lowSettled || settledAtFullDuty is not double fullSettled)
            {
                return FanGainCurve.None;
            }

            List<FanGainPoint> points =
            [
                new(PreStepDutyPercent, lowSettled),
                new(100d, fullSettled),
            ];

            // Two time constants: enough of the transient to extrapolate confidently, bounded so a machine
            // with an unusual τ neither rushes it nor spends all afternoon on it.
            var dwell = owner._timings.GainCurveDwell
                ?? TimeSpan.FromSeconds(Math.Clamp(fit.TimeConstantSeconds * 2d, 30d, 90d));

            foreach (var duty in GainCurveDutyPercents)
            {
                var startCelsius = ReadDrivingTemperature();
                await SetDutyAsync(duty, cancellationToken).ConfigureAwait(false);

                var level = await RecordLevelAsync(dwell, cancellationToken).ConfigureAwait(false);
                if (level.Abort is not null)
                {
                    // Whatever was gathered so far still describes the shape; an aborted sweep is not a
                    // reason to discard the levels that did complete.
                    break;
                }

                if (startCelsius is double from
                    && ExtrapolateSettled(level.Samples, from, fit.TimeConstantSeconds, fit.DeadTimeSeconds) is double settled)
                {
                    points.Add(new FanGainPoint(duty, settled));
                }
            }

            var curve = new FanGainCurve { Points = [.. points.OrderBy(static point => point.DutyPercent)] };

            owner._logger.LogInformation(
                "Fan {FanIndex} gain curve: {Points}.",
                fanIndex,
                string.Join(", ", curve.Points.Select(static point => $"{point.DutyPercent:0}%={point.SettledCelsius:0.#}C")));

            return curve;
        }

        /// <summary>Holds the current duty for a dwell, returning the transient it produced.</summary>
        private async Task<(IReadOnlyList<(double Seconds, double Celsius)> Samples, FanCalibrationRunResult? Abort)>
            RecordLevelAsync(TimeSpan dwell, CancellationToken cancellationToken)
        {
            List<(double, double)> samples = [];
            var start = _elapsed.Elapsed;
            var deadline = start + dwell;

            while (_elapsed.Elapsed < deadline)
            {
                var sample = await SampleAsync(FanCalibrationStep.MeasuringGainCurve, cancellationToken).ConfigureAwait(false);
                if (sample.Abort is { } abort)
                {
                    return ([], abort);
                }

                if (sample.Celsius is double celsius)
                {
                    samples.Add(((sample.TakenAt - start).TotalSeconds, celsius));
                }
            }

            return (samples, null);
        }

        /// <summary>
        /// Works out where a partial transient was heading, given the plant's own time constant.
        /// </summary>
        /// <remarks>
        /// First order says <c>T(t) = T∞ + (T₀ − T∞)·e^(−(t−L)/τ)</c>, which rearranges for T∞. Only samples
        /// at least one time constant past the dead time are used: before that the exponential is still close
        /// to one, the rearrangement divides by nearly nothing, and ordinary sensor noise turns into wild
        /// asymptotes. The estimates are then averaged, so no single sample decides the answer.
        /// </remarks>
        private static double? ExtrapolateSettled(
            IReadOnlyList<(double Seconds, double Celsius)> samples,
            double startCelsius,
            double timeConstantSeconds,
            double deadTimeSeconds)
        {
            if (timeConstantSeconds <= 0d)
            {
                return null;
            }

            List<double> estimates = [];

            foreach (var (seconds, celsius) in samples)
            {
                var since = seconds - deadTimeSeconds;
                if (since < timeConstantSeconds)
                {
                    continue;
                }

                // Well conditioned by construction: the filter above admits only samples at least one time
                // constant past the dead time, so the exponential is at most e⁻¹ and the divisor at least
                // 0.63. No separate guard is needed — one here would be unreachable.
                var decayed = Math.Exp(-since / timeConstantSeconds);
                estimates.Add((celsius - (startCelsius * decayed)) / (1d - decayed));
            }

            return estimates.Count > 0 ? estimates.Average() : null;
        }

        private async Task<(IReadOnlyList<(double Seconds, double Celsius)> Samples, double MaximumRpm, double? SettledCelsius, FanCalibrationRunResult? Abort)>
            RecordResponseAsync(CancellationToken cancellationToken)
        {
            List<(double, double)> samples = [];
            var maximumRpm = 0d;
            var start = _elapsed.Elapsed;
            var deadline = start + owner._timings.Response;

            while (_elapsed.Elapsed < deadline)
            {
                var sample = await SampleAsync(FanCalibrationStep.MeasuringResponse, cancellationToken).ConfigureAwait(false);
                if (sample.Abort is { } abort)
                {
                    return ([], maximumRpm, null, abort);
                }

                // Filed under the instant it was READ, not the instant control returned here. The two differ
                // by a whole sample interval, and on a step response that interval is where the curve is
                // steepest — mis-stamping it shifts the entire fit.
                if (sample.Celsius is double celsius)
                {
                    samples.Add(((sample.TakenAt - start).TotalSeconds, celsius));
                }

                if (sample.SpeedRpm is double rpm)
                {
                    maximumRpm = Math.Max(maximumRpm, rpm);
                }
            }

            // The tail mean rather than the final sample, for the same reason the fit uses one: a single noisy
            // reading must not become the operating point the whole gain curve is anchored to.
            var settled = samples.Count > 0
                ? samples.Skip(Math.Max(0, samples.Count - Math.Max(1, samples.Count / 5))).Average(static sample => sample.Item2)
                : (double?)null;

            return (samples, maximumRpm, settled, null);
        }

        private async Task<MinimumSpinResult> FindMinimumSpinAsync(CancellationToken cancellationToken)
        {
            _fanWasDriven = true;

            // Walk down in steps. The lowest duty at which the fan is still measurably turning is the answer;
            // the first duty where it stops is one step too far.
            //
            // Seeded with the bootstrap guess rather than 100 %, because this value becomes the controller's
            // duty FLOOR. A machine whose tachometer never reads — no reading at any duty — would otherwise be
            // pinned at full speed permanently, turning an unreadable sensor into a fan that never slows down.
            var lastTurningDuty = FanCalibrationSnapshot.Bootstrap.MinimumSpinDutyPercent;
            var lastTurningRpm = 0d;

            for (var duty = 40d; duty >= 5d; duty -= 5d)
            {
                await SetDutyAsync(duty, cancellationToken).ConfigureAwait(false);

                // Let the fan actually reach the commanded speed before believing the tachometer. Read too
                // soon and the reading still shows the PREVIOUS, higher duty — so the walk would sail past
                // the real stall point and report a floor the fan cannot hold.
                if (await RunStepAsync(FanCalibrationStep.FindingMinimumSpin, owner._timings.MinimumSpinDwell, cancellationToken).ConfigureAwait(false) is { } abort)
                {
                    return new MinimumSpinResult(lastTurningRpm, lastTurningDuty, abort);
                }

                var rpm = ReadSpeedRpm();
                if (rpm is not double speed || speed < StalledRpmThreshold)
                {
                    break;
                }

                lastTurningDuty = duty;
                lastTurningRpm = speed;
            }

            return new MinimumSpinResult(lastTurningRpm, lastTurningDuty, null);
        }

        private async Task<FanSpeedTrackingMode> VerifySpeedTrackingAsync(double maximumRpm, CancellationToken cancellationToken)
        {
            // Ask for a speed the fan can definitely reach, then see whether it gets there. A fan that lands
            // far from the request does not track, and commanding RPM to it would silently do nothing.
            if (maximumRpm <= 0d)
            {
                return FanSpeedTrackingMode.Duty;
            }

            var target = maximumRpm * 0.6d;

            try
            {
                await owner._frameworkDataProvider.SetFanRpmAsync(fanIndex, (int)Math.Round(target), cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                owner._logger.LogInformation(exception, "Fan {FanIndex} rejected a speed command; falling back to duty control.", fanIndex);
                return FanSpeedTrackingMode.Duty;
            }

            // An abort here downgrades to duty rather than propagating. The model is already fitted and worth
            // keeping; only the question of HOW to drive the fan is unresolved, and duty is the answer that
            // works on every fan.
            if (await RunStepAsync(FanCalibrationStep.VerifyingSpeedTracking, owner._timings.TrackingSettle, cancellationToken).ConfigureAwait(false) is not null)
            {
                return FanSpeedTrackingMode.Duty;
            }

            var reached = ReadSpeedRpm();
            var tracked = reached is double speed && Math.Abs(speed - target) <= target * TrackingToleranceFraction;

            return tracked ? FanSpeedTrackingMode.Cascade : FanSpeedTrackingMode.Duty;
        }

        /// <summary>Below this the fan is not meaningfully turning.</summary>
        private const double StalledRpmThreshold = 200d;

        /// <summary>How far from the requested speed still counts as tracking it.</summary>
        private const double TrackingToleranceFraction = 0.15d;

        private readonly record struct MinimumSpinResult(double Rpm, double DutyPercent, FanCalibrationRunResult? Abort);

        /// <summary>
        /// One sample: what was read, when it was read, and whether the run must stop.
        /// </summary>
        /// <param name="Abort">Non-null when the run must end; every other field is then meaningless.</param>
        /// <param name="TakenAt">The instant the readings were taken — BEFORE the interval delay.</param>
        /// <param name="Celsius">The driving temperature, or null if nothing could be read.</param>
        /// <param name="SpeedRpm">Measured fan speed, or null if the tachometer did not report.</param>
        private readonly record struct SampleResult(
            FanCalibrationRunResult? Abort,
            TimeSpan TakenAt,
            double? Celsius,
            double? SpeedRpm)
        {
            public static SampleResult Aborted(FanCalibrationRunResult result) => new(result, TimeSpan.Zero, null, null);
        }
    }
}
