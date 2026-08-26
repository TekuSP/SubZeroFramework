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
    /// The fixed duty every fan NOT being calibrated holds for the whole driven phase.
    /// </summary>
    /// <remarks>
    /// The identification needs everything except the measured fan's duty held still; ANY constant satisfies
    /// that, and this one only picks the operating point. 40%, not off: a dead sibling maximises the measured
    /// swing, but the sibling's own zone then has NO airflow for the whole run — and during a GPU-fan
    /// calibration that zone is the CPU's, soaking under idle and housekeeping heat with nothing moving air
    /// over it. Real runs on the reference chassis escalated the sibling to ~50% anyway before an attempt
    /// could survive, so starting near the converged point also spares the abort-cooldown-retry cycles that
    /// rediscovered it. Comfortably above the sputter band around the ~12% stall (where a fan toggles
    /// between stalling and being kicked, modulating the plant mid-measurement), so the hold is genuinely
    /// constant. The swing this costs is the noise gate's problem: a too-small swing is refused with a
    /// message, not fitted.
    /// </remarks>
    public const double SiblingHoldDutyPercent = 40d;

    /// <summary>How close to the safety ceiling a measurement may get before it is retried, in °C.</summary>
    /// <remarks>
    /// <para>
    /// Retry rather than abort: parking the siblings low maximises the measured swing, but on a hot
    /// chassis it can put a settle's own asymptote at the ceiling — a run that then ABORTED at 95 °C every
    /// time, which one machine did. At this margin the attempt stops, the load is dropped, every fan runs
    /// at full until the machine cools, and the measurement runs again with the siblings a step higher —
    /// repeating until an attempt fits inside the margin. The run completes at the most aggressive sibling
    /// duty this chassis can actually sustain, found instead of guessed.
    /// </para>
    /// <para>
    /// Three degrees, not more: the silicon's own hard limit (Tctl on mobile Ryzen) sits near 100 °C, so
    /// the 95 °C ceiling is already conservative — but the measurement rots before the hardware does. Idle
    /// injection was measured on the reference machine from the high 80s with the fans pinned: busy
    /// collapsing to 30-40% at full clocks. A trip line above ~92 °C would let the run hold an operating
    /// point the CPU is already sabotaging.
    /// </para>
    /// </remarks>
    public const double CeilingRetryMarginCelsius = 3d;

    /// <summary>How much sibling duty each retry adds.</summary>
    public const double SiblingRetryStepPercent = 10d;

    /// <summary>
    /// Where the retries stop and the ceiling abort takes over.
    /// </summary>
    /// <remarks>
    /// Full, not partial. An artificial cap below full just converts "the siblings could have coped" into an
    /// abort. If the swing left after heavy retries is too small to fit, the noise gate refuses it with a
    /// message that says so — a better failure than 95 °C. Beyond full duty there is genuinely nothing left,
    /// and once the siblings sit here the retry trigger disarms: the attempt runs on into the margin and
    /// either completes there or meets the real abort.
    /// </remarks>
    public const double MaximumSiblingHoldDutyPercent = 100d;

    /// <summary>How far below the ceiling the machine must cool, in °C, before a retry begins.</summary>
    /// <remarks>
    /// Well below the retry margin, so the next attempt starts from a genuinely cooled machine rather than
    /// re-tripping on the residual heat of the last one.
    /// </remarks>
    public const double CooldownExitMarginCelsius = 15d;

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
    /// <param name="requestedLoadTarget">
    /// What the caller asked to heat, or <see cref="ThermalLoadTarget.None"/> to let the fan's cooling role
    /// decide. A caller's choice wins: the role is inferred, and on hardware nobody has mapped it is a guess
    /// — one that costs a five-minute run which could never have measured anything if it guesses wrong.
    /// </param>
    public async Task<FanCalibrationRunResult> RunAsync(
        int fanIndex,
        IReadOnlyCollection<int> drivingSensorIndices,
        Func<FanCalibrationProgress, Task> onProgress,
        CancellationToken cancellationToken,
        ThermalLoadTarget requestedLoadTarget = ThermalLoadTarget.None)
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

        // Every OTHER fan is listed too, because the run pins them. On this chassis the fans share the
        // heatpipe assembly, and a sibling left under closed-loop control is a feedback loop wrapped around
        // the measurement: load rises, it spins up; the measured fan steps to full and the temperature
        // starts to fall, it spins DOWN — actively cancelling the very response being identified. Measured
        // before this: a 2 °C swing on a machine that can produce far more. No extra claims are needed —
        // the arbiter's one machine-wide claim answers IsCalibrating for every fan while a run is active,
        // which is what keeps the curve worker's hands off the pinned siblings too.
        List<int> siblingFanIndices = [.. _frameworkDataProvider.GetFanIndices().Where(index => index != fanIndex)];

        var session = new RunSession(fanIndex, siblingFanIndices, drivingSensorIndices, onProgress, this, requestedLoadTarget);

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
            await SafelyAsync(session.RestoreFansAsync, "restore the fans").ConfigureAwait(false);
            Safely(_loadGenerator.Stop, "stop CPU load");
            Safely(_gpuLoadGenerator.Stop, "stop GPU load");

            // Released last, so nothing else can drive the fans until they are back under automatic control.
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
        IReadOnlyList<int> siblingFanIndices,
        IReadOnlyCollection<int> drivingSensorIndices,
        Func<FanCalibrationProgress, Task> onProgress,
        FanCalibrationRunner owner,
        ThermalLoadTarget requestedLoadTarget)
    {
        private readonly Stopwatch _elapsed = Stopwatch.StartNew();
        private readonly List<double> _powerSamples = [];

        /// <summary>Whether the last power sample came from the system reading rather than the package.</summary>
        private bool _powerIsSystemWide;
        private double _peakCelsius;
        private bool _fanWasDriven;

        /// <summary>True once any sibling accepted its hold duty, so restore knows there is work either way.</summary>
        private bool _siblingsWereDriven;

        /// <summary>
        /// The duty the siblings currently hold — the near-dead default until ceiling relief raises it.
        /// </summary>
        /// <remarks>
        /// Only ever raised BETWEEN settles, never during the response recording: the recording is a fall
        /// from a settled point already inside the ceiling margin, so it cannot need relief, and the fit's
        /// premise that everything but the measured fan holds still stays intact.
        /// </remarks>
        private double _siblingHoldDutyPercent = SiblingHoldDutyPercent;

        /// <summary>True only while a measurement attempt is running — the phases a ceiling retry can redo.</summary>
        private bool _retryArmed;

        /// <summary>Set when a sample tripped the retry margin, telling the attempt loop to go again.</summary>
        private bool _ceilingRetryPending;

        /// <summary>When the hottest reading first crossed the retry line, or null while it is below.</summary>
        private TimeSpan? _retryHotSince;

        /// <summary>
        /// True while a ceiling-retry cooldown is waiting for the machine to cool with every fan at full.
        /// </summary>
        /// <remarks>
        /// Suppresses the ceiling ABORT, and that is not a safety trade: the abort's remedy is to stop the
        /// heat and give the machine its fans back, and cooldown has already gone further — heat off and
        /// every fan at maximum. The sensor keeps climbing for seconds after a retry trips (heat already in
        /// flight from die to sensor, load threads winding down), and a live abort here killed a real run at
        /// 96 °C during its very first cooldown — before the sibling ever got its raise.
        /// </remarks>
        private bool _coolingDown;

        /// <summary>The first completed minimum-spin walk, reused by every retry after it.</summary>
        private MinimumSpinResult? _cachedMinimumSpin;

        /// <summary>
        /// The furthest the progress bar has reached, reported instead of the raw figure.
        /// </summary>
        /// <remarks>
        /// A retry re-runs earlier steps, and the raw schedule position would walk the bar BACKWARD — which
        /// reads as the run breaking. Held at its high-water mark, the bar simply pauses while the retry
        /// catches back up.
        /// </remarks>
        private double _maxReportedProgress;

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
            // An explicit request wins over everything below it. The role and the sensor-name reading are
            // both inferences, and the user is looking at the machine.
            if (requestedLoadTarget != ThermalLoadTarget.None)
            {
                return requestedLoadTarget;
            }

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

            // 2+ — everything that drives fans, as many times as it takes. An attempt that gets within the
            // retry margin of the ceiling is not a verdict about the machine, it is a verdict about the
            // SIBLING duty the attempt ran with — so the load is dropped, every fan goes back to firmware
            // control until the machine cools, and the attempt runs again with the siblings a step higher.
            // The loop wraps the WHOLE driven region, minimum-spin walk included: the walk holds every fan
            // near-dead on a machine that is never as idle as "idle" suggests, and leaving it outside the
            // armed region is exactly how a real run cooked to 95 °C ninety seconds in, before the retry
            // machinery was even listening. The loop is finite by construction: each pass raises the hold,
            // and at full duty the trigger disarms.
            while (true)
            {
                _ceilingRetryPending = false;
                _retryHotSince = null;
                _retryArmed = true;

                var attempt = await MeasureAndFitAsync(cancellationToken).ConfigureAwait(false);

                _retryArmed = false;

                if (!_ceilingRetryPending)
                {
                    return attempt;
                }

                // The first raise jumps clear across the sputter band to a duty the fan can actually hold;
                // every raise after that steps normally. 10% or 20% would leave the sibling stalling and
                // restarting mid-attempt — the exact noise pinning it exists to remove.
                _siblingHoldDutyPercent = Math.Min(MaximumSiblingHoldDutyPercent, _siblingHoldDutyPercent + SiblingRetryStepPercent);

                owner._logger.LogInformation(
                    "Calibration for fan {FanIndex}: cooling down, then retrying with the sibling hold raised to {Duty}%.",
                    fanIndex,
                    _siblingHoldDutyPercent);

                if (await CoolDownAtFullFanAsync(cancellationToken).ConfigureAwait(false) is { } cooldownAbort)
                {
                    return cooldownAbort;
                }
            }
        }

        /// <summary>
        /// Drops the heat and runs EVERY fan at full until the machine has genuinely cooled.
        /// </summary>
        /// <remarks>
        /// Full duty on every fan, not firmware auto. Auto was tried first and cooled with one fan while
        /// the rest idled — the firmware has no idea a calibration wants the heat gone NOW, it only sees
        /// temperatures it considers acceptable. Commanding all of them is deterministic, fastest, and safe:
        /// the arbiter owns every fan for the whole run, so nothing fights the commands, and the run's
        /// final restore covers every exit path out of this state. A cooldown that times out proceeds
        /// anyway — the retry margin trips again if it must, and each trip raises the siblings, so a
        /// machine that cannot cool converges on the honest abort instead of looping.
        /// </remarks>
        private async Task<FanCalibrationRunResult?> CoolDownAtFullFanAsync(CancellationToken cancellationToken)
        {
            owner._loadGenerator.Stop();
            owner._gpuLoadGenerator.Stop();

            _fanWasDriven = true;
            await SetDutyAsync(100d, cancellationToken).ConfigureAwait(false);

            foreach (var siblingFanIndex in siblingFanIndices)
            {
                try
                {
                    await owner._frameworkDataProvider.SetFanDutyAsync(siblingFanIndex, 100d, cancellationToken).ConfigureAwait(false);
                    _siblingsWereDriven = true;
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    owner._logger.LogWarning(exception, "Could not run fan {SiblingFanIndex} at full for the cooldown; continuing.", siblingFanIndex);
                }
            }

            var deadline = _elapsed.Elapsed + owner._timings.CooldownTimeout;
            var peakDuringCooldown = double.MinValue;
            _coolingDown = true;

            try
            {
                while (_elapsed.Elapsed < deadline)
                {
                    // The retry trigger is disarmed here and the ceiling abort stands down (see
                    // _coolingDown) — a still-hot machine, or the overshoot of the trip that got us here,
                    // reads as a cooldown in progress. Cancellation, battery and telemetry staleness still
                    // abort exactly as they do everywhere else.
                    var sample = await SampleAsync(FanCalibrationStep.CoolingDown, cancellationToken).ConfigureAwait(false);
                    if (sample.Abort is { } abort)
                    {
                        return abort;
                    }

                    if (ReadHottestSensor() is not { } hottest)
                    {
                        continue;
                    }

                    peakDuringCooldown = Math.Max(peakDuringCooldown, hottest.Celsius);

                    if (hottest.Celsius <= SafetyCeilingCelsius - CooldownExitMarginCelsius)
                    {
                        break;
                    }
                }
            }
            finally
            {
                _coolingDown = false;
            }

            if (peakDuringCooldown > double.MinValue)
            {
                owner._logger.LogInformation(
                    "Calibration for fan {FanIndex}: cooldown complete (peaked at {Peak:0.#} C).",
                    fanIndex,
                    peakDuringCooldown);
            }

            // The next attempt re-pins the siblings itself, at their raised hold, as its first act.
            return null;
        }

        /// <summary>
        /// One full measurement: warm, hold, step, record, fit, verify, sweep — everything a retry redoes.
        /// </summary>
        private async Task<FanCalibrationRunResult> MeasureAndFitAsync(CancellationToken cancellationToken)
        {
            // Cleared per attempt: watts sampled by an attempt that tripped the retry margin describe a
            // machine running hotter, with less airflow, than the one the surviving attempt measured — and
            // the average becomes the feed-forward gain.
            _powerSamples.Clear();

            // Every sibling is pinned before anything else is driven. A sibling left under firmware control
            // is a feedback loop wrapped around the measurement: it spins up as the load heats the shared
            // heatpipe and back down as the measured fan's step cools it, cancelling the very response the
            // fit needs. Re-asserted at the top of every attempt so a retry runs at its RAISED hold.
            await PinSiblingFansAsync(cancellationToken).ConfigureAwait(false);

            // Minimum spin, walked down rather than searched, because the interesting quantity is where the
            // fan STOPS reliably turning — only observable by going there. Cached across retries: the stall
            // point does not depend on the sibling duty, and the walk costs a minute of near-dead fans on a
            // machine the retry exists to protect, so a retry only repeats it when the first walk never got
            // to finish.
            if (_cachedMinimumSpin is not { } minimumSpin)
            {
                minimumSpin = await FindMinimumSpinAsync(cancellationToken).ConfigureAwait(false);
                if (minimumSpin.Abort is { } spinAbort)
                {
                    return spinAbort;
                }

                _cachedMinimumSpin = minimumSpin;

                // The walk ends on a chassis that just soaked for a minute with EVERY fan near-dead, and
                // the load phase would start from that stored heat — riding at the ceiling before the
                // measurement contributed a single degree of its own. Flush it first with every fan at
                // full; on a machine that stayed cool the loop exits on its first sample, so this costs
                // nothing when there is nothing to flush.
                if (await CoolDownAtFullFanAsync(cancellationToken).ConfigureAwait(false) is { } walkCooldownAbort)
                {
                    return walkCooldownAbort;
                }

                // The cooldown ran the siblings at full; put them back on their measurement hold before
                // anything warms up again.
                await PinSiblingFansAsync(cancellationToken).ConfigureAwait(false);
            }

            // 3 — load, entered under FULL fan rather than at the low hold. The measured step still goes
            // UPWARD and records the fall, because that direction is fail-safe — anything that dies
            // mid-measurement leaves the fan at maximum, not pinned low on a hot machine. What changed is
            // the approach: one unbroken climb from idle to the run's hottest point at the low hold was the
            // longest, least-forgiving stretch of the whole run, and with the siblings now parked near-dead
            // it rode straight at the ceiling. Split in two, the machine first settles at the COOL loaded
            // point under maximum airflow, and the hold is then entered from nearby — a shorter climb that
            // settles sooner, watched by the same ceiling guard throughout.
            _fanWasDriven = true;
            await SetDutyAsync(100d, cancellationToken).ConfigureAwait(false);

            if (StartLoad() is { } loadFailure)
            {
                return loadFailure;
            }

            if (await SettleUnderLoadAsync(cancellationToken).ConfigureAwait(false) is { } warmAbort)
            {
                return warmAbort;
            }

            // Then down to the hold and steady there. The fit assumes the only thing that changes at the
            // step is duty, so this asymptote has to be genuinely settled before the step fires.
            await SetDutyAsync(PreStepDutyPercent, cancellationToken).ConfigureAwait(false);

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
        /// <summary>
        /// Parks every fan this run is not measuring at a fixed duty, so nothing regulates against the test.
        /// </summary>
        /// <remarks>
        /// Best-effort per fan: a sibling that refuses the command keeps its firmware control, which merely
        /// degrades the measurement back to what it was — it must not abort a run the user consented to.
        /// </remarks>
        private async Task PinSiblingFansAsync(CancellationToken cancellationToken)
        {
            foreach (var siblingFanIndex in siblingFanIndices)
            {
                try
                {
                    await owner._frameworkDataProvider.SetFanDutyAsync(siblingFanIndex, _siblingHoldDutyPercent, cancellationToken).ConfigureAwait(false);
                    _siblingsWereDriven = true;
                    owner._logger.LogInformation(
                        "Pinned fan {SiblingFanIndex} at {Duty}% while fan {FanIndex} is calibrated.",
                        siblingFanIndex,
                        _siblingHoldDutyPercent,
                        fanIndex);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    owner._logger.LogWarning(
                        exception,
                        "Could not pin fan {SiblingFanIndex} during calibration; it stays under firmware control and may soften the measured response.",
                        siblingFanIndex);
                }
            }
        }

        public async Task RestoreFansAsync()
        {
            // Siblings first and unconditionally once any was pinned: they were commanded even on runs that
            // never got as far as driving the measured fan.
            if (_siblingsWereDriven)
            {
                foreach (var siblingFanIndex in siblingFanIndices)
                {
                    try
                    {
                        await owner._frameworkDataProvider.RestoreAutoFanControlAsync(siblingFanIndex, CancellationToken.None).ConfigureAwait(false);
                    }
                    catch (Exception exception)
                    {
                        owner._logger.LogError(exception, "Failed to restore fan {SiblingFanIndex} after calibration. The fan may still be overridden.", siblingFanIndex);
                    }
                }
            }

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

                // The retry trigger, BEFORE the abort and keyed on the same hottest-of-everything reading
                // the abort uses — an earlier version watched only the driving temperature, and a
                // non-driving sensor walked straight past it to 95 °C. Tripping here unwinds the whole
                // attempt so it can be re-run with more sibling airflow after a cooldown; the abort result
                // built here is a placeholder the attempt loop discards. Disarmed once the siblings have no
                // headroom left, so a final attempt may run on into the margin — completing there beats
                // aborting for wanting room that does not exist.
                if (_retryArmed
                    && siblingFanIndices.Count > 0
                    && _siblingHoldDutyPercent < MaximumSiblingHoldDutyPercent)
                {
                    // Sub-ceiling heat must PERSIST before it costs a cooldown cycle — the reading flickers
                    // ±2 °C, and a single spike used to burn minutes. The ceiling itself trips instantly:
                    // a genuine runaway does not get to spend the persistence budget climbing.
                    if (hottest.Celsius >= SafetyCeilingCelsius - CeilingRetryMarginCelsius)
                    {
                        _retryHotSince ??= takenAt;
                    }
                    else
                    {
                        _retryHotSince = null;
                    }

                    var sustainedPastTheLine = _retryHotSince is { } hotSince
                        && takenAt - hotSince >= owner._timings.CeilingRetryPersistence;

                    if (sustainedPastTheLine || hottest.Celsius >= SafetyCeilingCelsius)
                    {
                        _ceilingRetryPending = true;

                        owner._logger.LogInformation(
                            "Calibration for fan {FanIndex}: sensor {SensorName} at {Celsius:0.#} C ({Reason}); the attempt will be retried with more sibling airflow.",
                            fanIndex,
                            hottest.Name,
                            hottest.Celsius,
                            sustainedPastTheLine ? "sustained inside the retry margin" : "at the ceiling");

                        return SampleResult.Aborted(Failed(fanIndex, FanCalibrationFailure.TemperatureCeiling, step, _elapsed.Elapsed, restored: true, peak: _peakCelsius));
                    }
                }

                // The ceiling abort stands down during a cooldown — heat off with every fan at full EXCEEDS
                // the abort's remedy, already applied, and the post-trip overshoot would otherwise kill the
                // retry it exists to allow.
                if (hottest.Celsius >= SafetyCeilingCelsius && !_coolingDown)
                {
                    // The context rides in the WARNING because warnings are all the default EventLog level
                    // keeps — a bare abort line cost a whole diagnosis session once, when which phase died
                    // and whether the retry was even armed had to be reconstructed from timestamps.
                    owner._logger.LogWarning(
                        "Aborting calibration for fan {FanIndex}: sensor {SensorIndex} ({SensorName}) reached {Celsius:0.#} C, at or above the {Ceiling} C safety ceiling (step {Step}, retry armed: {RetryArmed}, siblings: {SiblingCount} at {SiblingDuty}%).",
                        fanIndex,
                        hottest.Index,
                        hottest.Name,
                        hottest.Celsius,
                        SafetyCeilingCelsius,
                        step,
                        _retryArmed,
                        siblingFanIndices.Count,
                        _siblingHoldDutyPercent);

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
            var preferred = ResolveLoadTarget() == ThermalLoadTarget.Gpu
                ? control.Sample.GpuPowerWatts
                : control.Sample.CpuPackagePowerWatts;

            var power = preferred ?? control.Sample.SystemPowerWatts;

            // Whether the fallback was taken, so the readout can name what it is showing. A system reading is
            // a legitimate substitute for measuring, and a badly mislabelled one when displayed: an adapter
            // drawing 240 W says nothing about whether the processor got busy.
            _powerIsSystemWide = preferred is null && control.Sample.SystemPowerWatts is not null;

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

            // Held at its high-water mark: a ceiling retry re-runs earlier steps, and the raw schedule
            // position would walk the bar BACKWARD — which reads as the run breaking. Held, it pauses
            // instead while the retry catches back up. CoolingDown never feeds it — the pause is not a step,
            // and its out-of-order enum value would read to the schedule as "past everything".
            if (step != FanCalibrationStep.CoolingDown)
            {
                _maxReportedProgress = Math.Max(_maxReportedProgress, owner._schedule.ProgressAt(step, elapsedInStep));
            }

            // Read here rather than passed in, because reports also fire on step transitions where no fresh
            // sample exists — and the cached read costs a volatile load. Clock and busy share are taken from
            // the component this run HEATS, for the same reason power is: on a GPU-load run the processor
            // sits idle, and its figures would show a machine that never got busy under a GPU at full load.
            var control = owner._frameworkDataProvider.GetLatestControlTelemetry().Sample;
            var isGpuLoad = ResolveLoadTarget() == ThermalLoadTarget.Gpu;

            try
            {
                await onProgress(new FanCalibrationProgress
                {
                    FanIndex = fanIndex,
                    Step = step,
                    OverallProgress = _maxReportedProgress,
                    // None during a cooldown: the pause's length is the machine's to decide, and the
                    // schedule would misread the out-of-order enum value as "almost done".
                    EstimatedRemaining = step == FanCalibrationStep.CoolingDown
                        ? null
                        : owner._schedule.RemainingAt(step, elapsedInStep),
                    ElapsedSeconds = _elapsed.Elapsed.TotalSeconds,
                    TemperatureCelsius = temperature ?? ReadDrivingTemperature(),
                    DutyPercent = _commandedDutyPercent,
                    SpeedRpm = speed ?? ReadSpeedRpm(),
                    PackagePowerWatts = power,
                    PowerIsSystemWide = _powerIsSystemWide,
                    ClockMegahertz = isGpuLoad ? control.GpuCoreClockMegahertz : control.CpuClockMegahertz,
                    UtilizationPercent = (isGpuLoad ? control.GpuUtilizationFraction : control.CpuUtilizationFraction) is { } busyFraction
                        ? busyFraction * 100d
                        : null,
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

            // The settle range is judged on a short trailing mean, not the raw reading. The EC quantises to
            // whole degrees and the driving value is a maximum over several sensors, so the raw trace jumps
            // 1-2 °C both ways even at a genuinely settled operating point — against a 1.5 °C settle band
            // that means the settle NEVER closes and every hold rides its full timeout. The mean's lag is
            // harmless here (a settle decision is not a timing measurement), and the safety checks in
            // SampleAsync keep reading raw. Local to this call on purpose: a new settle is a new plant.
            Queue<double> recentCelsius = new();

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

                recentCelsius.Enqueue(celsius);
                while (recentCelsius.Count > 5)
                {
                    recentCelsius.Dequeue();
                }

                var now = sample.TakenAt;

                window.Add((now, recentCelsius.Average()));
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
