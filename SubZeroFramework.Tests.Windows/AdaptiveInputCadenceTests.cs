using System.Diagnostics;
using System.Globalization;

using Microsoft.Extensions.Logging.Abstractions;

using NUnit.Framework;

using SubZeroFramework.Models;
using SubZeroFramework.Service.Services;
using SubZeroFramework.Services.Compute;
using SubZeroFramework.Services.Control;

namespace SubZeroFramework.Tests.Windows;

/// <summary>
/// Measures how often each input the adaptive controller runs on actually CHANGES, against the real machine.
/// </summary>
/// <remarks>
/// <para>
/// Every other test of the adaptive path feeds the controller values from an array, which proves the maths
/// and proves nothing about whether a real machine supplies those values often enough for the maths to mean
/// anything. A signal that is read every second but only moves every thirty is not a one-second signal, and
/// nothing in the type system distinguishes the two — the field is a <c>double?</c> either way.
/// </para>
/// <para>
/// This exists because of exactly that confusion. The Device Capabilities card reports a CPU clock that comes
/// from the THIRTY-SECOND inventory tier, sitting beside package power that comes from the one-second control
/// tier, and the two look identical in the UI. The question "is the clock updating often enough for adaptive
/// mode" is not answerable by reading the code, because the code reads both at the rate its tier ticks. It is
/// answerable by watching the values.
/// </para>
/// <para>
/// What is NOT covered here, and why: the driving temperature, fan RPM and charger draw come from Framework's
/// embedded controller rather than from any counter, so probing them needs an actual Framework laptop and the
/// service's privileges — see <see cref="HardwareTestCategories.FrameworkLaptop"/>. Everything this fixture
/// touches is a counter or a GPU driver, which any Windows machine has, so it is gated on the OS alone.
/// </para>
/// </remarks>
[TestFixture]
[Category(HardwareTestCategories.Machine)]
[Platform("Win", Reason = "Probes the PDH counter sets and GPU drivers that the Windows service reads.")]
public class AdaptiveInputCadenceTests
{
    /// <summary>
    /// How often the probe asks. Deliberately FASTER than the one-second tier the service polls at.
    /// </summary>
    /// <remarks>
    /// Sampling at the tier rate could only ever report "changes about every second", whether the underlying
    /// counter updates every 100 ms or every 900. Oversampling separates the source's own resolution from the
    /// schedule laid over it, which is the distinction the whole fixture exists to make.
    /// </remarks>
    private static readonly TimeSpan ProbeInterval = TimeSpan.FromMilliseconds(250);

    /// <summary>How long to watch. Long enough that a signal updating every few seconds still shows a rate.</summary>
    private static readonly TimeSpan ProbeDuration = TimeSpan.FromSeconds(20);

    /// <summary>
    /// The longest a control input may sit unchanged before it is considered stale.
    /// </summary>
    /// <remarks>
    /// Five seconds against a one-second tier is deliberately loose. The point is not to police jitter — it is
    /// to catch a signal that is really updating on the thirty-second inventory tier, or not at all, which
    /// this separates from a healthy one by a wide margin rather than a narrow one.
    /// </remarks>
    private static readonly TimeSpan StalenessBudget = TimeSpan.FromSeconds(5);

    /// <summary>A compressed ramp: the probe is measuring cadence, not watching a climb.</summary>
    private static readonly TimeSpan LoadRampDuration = TimeSpan.FromSeconds(2);

    /// <summary>How long to time ticks on an unloaded machine, to get a cost figure with no contention in it.</summary>
    private static readonly TimeSpan IdleBaselineDuration = TimeSpan.FromSeconds(5);

    /// <summary>
    /// A moderate load rather than a saturating one, so the signals have somewhere to move.
    /// </summary>
    /// <remarks>
    /// Measured: at the generator's default target this probe reported CPU utilisation as changing ZERO times
    /// in twenty seconds. That is not a frozen counter — utilisation is a busy fraction clamped to 1, and a
    /// saturated machine really does sit at exactly 1.0 every tick. Probing at a fraction that leaves
    /// headroom is what separates "pinned against its ceiling" from "not updating", which is the entire
    /// distinction this fixture is trying to draw.
    /// </remarks>
    private const double ProbeLoadFraction = 0.6d;

    /// <summary>
    /// One input to the adaptive controller, and whether the controller would break without it.
    /// </summary>
    /// <param name="Name">How it appears in the report.</param>
    /// <param name="Read">Pulls the value out of an assembled sample.</param>
    /// <param name="Role">What the controller does with it, so a failure report explains what is at stake.</param>
    /// <param name="DrivesControl">
    /// True for the two the controller cannot run without. Everything else is either a display figure or one
    /// of several interchangeable sources for the resolved thermal load, and a machine that lacks one
    /// individually is not broken.
    /// </param>
    private sealed record AdaptiveInput(
        string Name,
        Func<ProbeReading, double?> Read,
        string Role,
        bool DrivesControl);

    /// <summary>
    /// One tick's worth of everything the controller would see.
    /// </summary>
    /// <remarks>
    /// The resolved load rides alongside the sample rather than being recomputed per input, because
    /// <see cref="ThermalLoadPolicy"/> is stateful — it latches a source after watching availability over a
    /// window. Resolving once per tick is what lets the report name the source the policy actually settled
    /// on, instead of the answer a throwaway policy gives on its first and only sample.
    /// </remarks>
    private readonly record struct ProbeReading(ControlTelemetrySample Sample, double? ResolvedLoadWatts);

    /// <summary>
    /// Accumulates when a single signal CHANGED, which is not the same as when it was read.
    /// </summary>
    /// <remarks>
    /// Change is measured by inequality against the previous reading rather than against a tolerance. A
    /// counter that moves by a thousandth every read is still a live counter, and folding that into "no
    /// change" would report a working signal as frozen — the exact error this fixture is meant to catch,
    /// inverted.
    /// </remarks>
    private sealed class SignalTrace(AdaptiveInput input)
    {
        private readonly List<TimeSpan> _gapsBetweenChanges = [];
        private double? _previous;
        private TimeSpan _lastChangeAt;
        private bool _hasPrevious;

        public AdaptiveInput Input { get; } = input;

        /// <summary>Probe iterations, whether or not anything was readable.</summary>
        public int Samples { get; private set; }

        /// <summary>Iterations that produced a value. Below <see cref="Samples"/> means intermittent.</summary>
        public int Readings { get; private set; }

        /// <summary>Times the value differed from the one before it.</summary>
        public int Changes { get; private set; }

        /// <summary>True when the machine reported this at least once.</summary>
        public bool IsAvailable => Readings > 0;

        /// <summary>
        /// The one value seen, when the signal never changed; null otherwise.
        /// </summary>
        /// <remarks>
        /// Present so the report can tell "pinned against a ceiling" from "not updating". Utilisation is a
        /// clamped fraction, so a saturated machine reports exactly 1.0 forever and looks identical to a dead
        /// counter unless the value itself is shown.
        /// </remarks>
        public double? SoleValue => Changes == 0 ? _previous : null;

        public double AvailabilityFraction => Samples == 0 ? 0d : (double)Readings / Samples;

        public TimeSpan MeanGap => _gapsBetweenChanges.Count == 0
            ? TimeSpan.Zero
            : TimeSpan.FromTicks((long)_gapsBetweenChanges.Average(static gap => gap.Ticks));

        /// <summary>
        /// The worst dead period observed, INCLUDING the stretch after the final change.
        /// </summary>
        /// <remarks>
        /// The trailing stretch is the one that matters most and the one a naive implementation drops. A
        /// signal that ticks twice in the first second and then freezes for the rest of the run has excellent
        /// gaps between its changes and is still frozen.
        /// </remarks>
        public TimeSpan LongestGap { get; private set; }

        public void Observe(TimeSpan at, double? value)
        {
            Samples++;

            if (value is not { } reading)
            {
                return;
            }

            Readings++;

            // The reader differentiates cumulative counters against its own previous call, so the first
            // reading establishes a baseline and cannot be a "change" — there is nothing behind it.
            if (!_hasPrevious)
            {
                _hasPrevious = true;
                _previous = reading;
                _lastChangeAt = at;
                return;
            }

            if (reading.Equals(_previous))
            {
                return;
            }

            Changes++;
            RecordGap(at - _lastChangeAt);
            _lastChangeAt = at;
            _previous = reading;
        }

        /// <summary>Closes the trace, charging the time since the last change as a gap.</summary>
        public void Finish(TimeSpan at)
        {
            if (_hasPrevious)
            {
                RecordGap(at - _lastChangeAt);
            }
        }

        private void RecordGap(TimeSpan gap)
        {
            _gapsBetweenChanges.Add(gap);

            if (gap > LongestGap)
            {
                LongestGap = gap;
            }
        }
    }

    /// <summary>
    /// Watches every adaptive input under sustained load and reports how often each one actually moves.
    /// </summary>
    /// <remarks>
    /// Runs under load on purpose. On an idle machine several of these legitimately sit still, and a probe
    /// that concluded "frozen" from that would fail on a healthy laptop doing nothing.
    /// </remarks>
    [Test]
    public void EveryAdaptiveInput_MovesFasterThanTheStalenessBudget()
    {
        using var reader = CreateControlTelemetryReader();

        if (!reader.IsAvailable)
        {
            Assert.Ignore("PDH control telemetry is unavailable on this machine, so there is nothing to probe.");
        }

        using var compute = CreateComputeReader();
        var loadPolicy = new ThermalLoadPolicy();
        var inputs = DescribeAdaptiveInputs();
        var traces = inputs.Select(static input => new SignalTrace(input)).ToArray();

        // Measured BEFORE the load starts, and the comparison matters more than either number alone. The
        // probe's own thread competes with the load generator for the same cores, so a slow tick under load
        // may be the scheduler withholding the CPU rather than the counters costing anything. Only the gap
        // between these two profiles separates the two explanations.
        var idleCosts = MeasureTickCost(reader, compute, IdleBaselineDuration);

        using var load = new CpuLoadGenerator(
            rampDuration: LoadRampDuration,
            targetFraction: ProbeLoadFraction);
        load.Start();

        try
        {
            WaitForTargetLoad(load);

            // Discarded deliberately: the reader has no predecessor to differentiate against on its first
            // call, so it reports no utilisation and would otherwise register as an availability gap.
            _ = AssembleSample(reader, compute);

            var clock = Stopwatch.StartNew();
            // Split apart because the two halves have wildly different cost profiles and only one of them is
            // a candidate for running faster. Aggregating them hides that.
            List<double> tickCostsMilliseconds = [];
            List<double> cpuCostsMilliseconds = [];
            List<double> gpuCostsMilliseconds = [];

            while (clock.Elapsed < ProbeDuration)
            {
                // Timed because the tier interval is only affordable if a tick costs a small fraction of it.
                // This is the whole per-tick read the service does on the primary tier, minus the EC.
                var cpuStarted = Stopwatch.GetTimestamp();
                var cpuSample = reader.Sample();
                var cpuCost = Stopwatch.GetElapsedTime(cpuStarted).TotalMilliseconds;

                var gpuStarted = Stopwatch.GetTimestamp();
                var gpuWatts = ReadTotalGpuPowerWatts(compute);
                var gpuCost = Stopwatch.GetElapsedTime(gpuStarted).TotalMilliseconds;

                var sample = cpuSample with { GpuPowerWatts = gpuWatts };
                var reading = new ProbeReading(sample, loadPolicy.Resolve(sample).Watts);

                tickCostsMilliseconds.Add(cpuCost + gpuCost);
                cpuCostsMilliseconds.Add(cpuCost);
                gpuCostsMilliseconds.Add(gpuCost);

                var at = clock.Elapsed;

                foreach (var trace in traces)
                {
                    trace.Observe(at, trace.Input.Read(reading));
                }

                Thread.Sleep(ProbeInterval);
            }

            foreach (var trace in traces)
            {
                trace.Finish(clock.Elapsed);
            }

            Report(traces, loadPolicy, idleCosts, tickCostsMilliseconds, cpuCostsMilliseconds, gpuCostsMilliseconds);
            AssertControlInputsAreLive(traces);
        }
        finally
        {
            load.Stop();
        }
    }

    /// <summary>
    /// Every input the controller REQUIRES must be present and moving; the rest are reported, not policed.
    /// </summary>
    private static void AssertControlInputsAreLive(IReadOnlyList<SignalTrace> traces)
    {
        var required = traces.Where(static trace => trace.Input.DrivesControl).ToArray();

        Assert.Multiple(() =>
        {
            foreach (var trace in required)
            {
                Assert.That(
                    trace.IsAvailable,
                    Is.True,
                    $"'{trace.Input.Name}' was never readable, and {trace.Input.Role}.");

                if (!trace.IsAvailable)
                {
                    continue;
                }

                Assert.That(
                    trace.AvailabilityFraction,
                    Is.GreaterThanOrEqualTo(0.8d),
                    $"'{trace.Input.Name}' was readable on only {trace.AvailabilityFraction:P0} of samples, and {trace.Input.Role}.");

                Assert.That(
                    trace.Changes,
                    Is.GreaterThanOrEqualTo(2),
                    $"'{trace.Input.Name}' changed {trace.Changes} time(s) in {ProbeDuration.TotalSeconds:N0}s under load, "
                        + $"so it is not a live signal even though it is read every tick — and {trace.Input.Role}.");

                Assert.That(
                    trace.LongestGap,
                    Is.LessThanOrEqualTo(StalenessBudget),
                    $"'{trace.Input.Name}' sat unchanged for {trace.LongestGap.TotalSeconds:N1}s, past the "
                        + $"{StalenessBudget.TotalSeconds:N0}s budget, and {trace.Input.Role}.");
            }
        });
    }

    /// <summary>
    /// Writes the measurement out whether or not the test passed, because the numbers ARE the deliverable.
    /// </summary>
    /// <remarks>
    /// A green tick answers "is anything frozen". It does not answer "how often does the clock update", which
    /// is the question that gets asked, so the table goes to the run log every time.
    /// </remarks>
    private static void Report(
        IReadOnlyList<SignalTrace> traces,
        ThermalLoadPolicy loadPolicy,
        IReadOnlyList<double> idleCostsMilliseconds,
        IReadOnlyList<double> tickCostsMilliseconds,
        IReadOnlyList<double> cpuCostsMilliseconds,
        IReadOnlyList<double> gpuCostsMilliseconds)
    {
        var culture = CultureInfo.InvariantCulture;

        TestContext.Out.WriteLine(
            $"Adaptive input cadence — probed every {ProbeInterval.TotalMilliseconds:N0} ms "
                + $"for {ProbeDuration.TotalSeconds:N0} s at {ProbeLoadFraction:P0} CPU load.");
        TestContext.Out.WriteLine($"Resolved thermal load source: {loadPolicy.Source} (settled: {loadPolicy.IsSettled}).");
        TestContext.Out.WriteLine(string.Empty);
        TestContext.Out.WriteLine(
            string.Format(
                culture,
                "{0,-26} {1,7} {2,8} {3,9} {4,9} {5,7}  {6}",
                "input",
                "avail",
                "changes",
                "mean gap",
                "worst gap",
                "rate",
                "role"));

        foreach (var trace in traces)
        {
            var rate = trace.Changes / ProbeDuration.TotalSeconds;

            TestContext.Out.WriteLine(
                string.Format(
                    culture,
                    "{0,-26} {1,7} {2,8} {3,9} {4,9} {5,7}  {6}",
                    trace.Input.Name,
                    trace.IsAvailable ? $"{trace.AvailabilityFraction:P0}" : "none",
                    trace.Changes,
                    trace.Changes == 0 ? "-" : $"{trace.MeanGap.TotalSeconds:N2}s",
                    trace.IsAvailable ? $"{trace.LongestGap.TotalSeconds:N2}s" : "-",
                    trace.SoleValue is { } pinned ? $"@{pinned:G4}" : trace.Changes == 0 ? "-" : $"{rate:N1}/s",
                    trace.Input.DrivesControl ? $"REQUIRED — {trace.Input.Role}" : trace.Input.Role));
        }

        TestContext.Out.WriteLine(string.Empty);

        ReportCost("whole tick, machine IDLE", idleCostsMilliseconds, culture);
        ReportCost("whole tick, under load", tickCostsMilliseconds, culture);
        ReportCost("  of which cpu counters", cpuCostsMilliseconds, culture);
        ReportCost("  of which gpu drivers", gpuCostsMilliseconds, culture);

        TestContext.Out.WriteLine(string.Empty);
        TestContext.Out.WriteLine(
            "Not probed here — these come from Framework's embedded controller, not from a counter: "
                + "driving temperature, fan RPM, charger draw. The EC read is the dominant per-tick cost and "
                + "the driving temperature's own update rate is what decides whether a faster tier buys "
                + "anything, so a tier-interval change needs a FrameworkHardware probe as well as this one.");
    }

    /// <summary>
    /// Times the full per-tick read on whatever machine state is current, discarding the values.
    /// </summary>
    /// <remarks>
    /// The first call is dropped: the reader has no previous sample to differentiate against, and PDH answers
    /// a rate counter's first collect with no interval, so it is not representative of a steady-state tick.
    /// </remarks>
    private static List<double> MeasureTickCost(
        IControlTelemetryReader reader,
        IComputeUtilizationReader compute,
        TimeSpan duration)
    {
        _ = AssembleSample(reader, compute);

        List<double> costs = [];
        var clock = Stopwatch.StartNew();

        while (clock.Elapsed < duration)
        {
            var started = Stopwatch.GetTimestamp();
            _ = AssembleSample(reader, compute);
            costs.Add(Stopwatch.GetElapsedTime(started).TotalMilliseconds);

            Thread.Sleep(ProbeInterval);
        }

        return costs;
    }

    /// <summary>
    /// Prints one cost profile as a duty cycle against each candidate tier interval.
    /// </summary>
    /// <remarks>
    /// Duty rather than milliseconds, and p95 rather than mean, because a tier interval is affordable only if
    /// the SLOW ticks fit inside it. A mean that looks cheap hides a tail that does not, and the tail is what
    /// makes a polling loop fall behind its own schedule.
    /// </remarks>
    private static void ReportCost(string label, IReadOnlyList<double> costsMilliseconds, CultureInfo culture)
    {
        if (costsMilliseconds.Count == 0)
        {
            return;
        }

        var ordered = costsMilliseconds.Order().ToArray();
        var mean = ordered.Average();
        var p95 = ordered[Math.Min(ordered.Length - 1, (int)(ordered.Length * 0.95d))];

        TestContext.Out.WriteLine(
            string.Format(
                culture,
                "{0,-26} mean {1,7:N2} ms  p95 {2,7:N2} ms  worst {3,7:N2} ms   |   duty @1000ms {4,6:P1}  @500ms {5,6:P1}  @250ms {6,6:P1}",
                label,
                mean,
                p95,
                ordered[^1],
                p95 / 1000d,
                p95 / 500d,
                p95 / 250d));
    }

    /// <summary>
    /// The inputs, in the order the controller consumes them.
    /// </summary>
    /// <remarks>
    /// Only two are marked as driving control, and the split is worth stating because it is not obvious from
    /// the field names. The controller's feed-forward term runs on the RESOLVED thermal load, which
    /// <see cref="ThermalLoadPolicy"/> composes from whichever power sources this machine has — so no single
    /// power field is individually required. Its throttle latch runs on the performance ratio, which has no
    /// substitute at all. Utilisation and clock are display figures that the controller never reads.
    /// </remarks>
    private static IReadOnlyList<AdaptiveInput> DescribeAdaptiveInputs() =>
    [
        new(
            "cpu utilisation",
            static reading => reading.Sample.CpuUtilizationFraction,
            "shown as package usage; also governs the calibration load",
            DrivesControl: false),
        new(
            "cpu performance ratio",
            static reading => reading.Sample.CpuPerformanceRatio,
            "the throttle latch reads it, and nothing else can supply it",
            DrivesControl: true),
        new(
            "cpu clock",
            static reading => reading.Sample.CpuClockMegahertz,
            "shown as the live clock on the CPU package card",
            DrivesControl: false),
        new(
            "cpu package power",
            static reading => reading.Sample.CpuPackagePowerWatts,
            "feeds the resolved thermal load",
            DrivesControl: false),
        new(
            "gpu power",
            static reading => reading.Sample.GpuPowerWatts,
            "feeds the resolved thermal load; suppressed while the GPU sleeps",
            DrivesControl: false),
        new(
            "resolved thermal load",
            static reading => reading.ResolvedLoadWatts,
            "the feed-forward term is computed from it",
            DrivesControl: true),
    ];

    /// <summary>
    /// Assembles the sample the way <c>FrameworkDataProvider</c> does, minus what needs the EC.
    /// </summary>
    /// <remarks>
    /// The reader knows CPU signals only; GPU power is folded in from the compute readers. Charger draw is
    /// the third ingredient in the real provider and is absent here, so on a machine with no package power
    /// the resolved load will report unavailable where the running service would have fallen back to system
    /// power. That is a limit of probing without a Framework laptop, not a defect in the resolution.
    /// </remarks>
    private static ControlTelemetrySample AssembleSample(
        IControlTelemetryReader reader,
        IComputeUtilizationReader compute)
        => reader.Sample() with { GpuPowerWatts = ReadTotalGpuPowerWatts(compute) };

    /// <summary>Total draw across every GPU that reported one, or null when none did.</summary>
    private static double? ReadTotalGpuPowerWatts(IComputeUtilizationReader compute)
    {
        double total = 0;
        var reported = false;

        foreach (var device in compute.Sample())
        {
            if (device.Kind is ComputeDeviceKind.Gpu && device.PowerWatts is { } watts)
            {
                total += watts;
                reported = true;
            }
        }

        return reported ? total : null;
    }

    private static IControlTelemetryReader CreateControlTelemetryReader()
        => new WindowsPdhControlTelemetryReader(NullLogger<WindowsPdhControlTelemetryReader>.Instance);

    /// <summary>
    /// The same composite the Windows service registers, so the probe sees what the service would see.
    /// </summary>
    /// <remarks>
    /// PDH first, so it owns utilisation and the device key, with the vendor readers filling in only the
    /// fields it left null — including the power figure this fixture is after.
    /// </remarks>
    private static IComputeUtilizationReader CreateComputeReader()
    {
        var resolver = new WindowsComputeDeviceIdentityResolver(
            NullLogger<WindowsComputeDeviceIdentityResolver>.Instance);

        return new CompositeComputeUtilizationReader(
            [
                new WindowsPdhComputeUtilizationReader(
                    NullLogger<WindowsPdhComputeUtilizationReader>.Instance,
                    resolver),
                new WindowsNvmlGpuUtilizationReader(
                    NullLogger<WindowsNvmlGpuUtilizationReader>.Instance,
                    resolver),
                new WindowsAdlxGpuUtilizationReader(
                    NullLogger<WindowsAdlxGpuUtilizationReader>.Instance),
                new WindowsIgclGpuUtilizationReader(
                    NullLogger<WindowsIgclGpuUtilizationReader>.Instance,
                    resolver),
            ],
            NullLogger<CompositeComputeUtilizationReader>.Instance);
    }

    /// <summary>Blocks until the load is steady, so the probe is not measuring a ramp.</summary>
    private static void WaitForTargetLoad(ICpuLoadGenerator load)
    {
        var deadline = Stopwatch.StartNew();
        var timeout = LoadRampDuration + TimeSpan.FromSeconds(5);

        while (!load.IsAtTargetLoad && deadline.Elapsed < timeout)
        {
            Thread.Sleep(100);
        }

        Assert.That(
            load.IsAtTargetLoad,
            Is.True,
            "The CPU load never reached its target, so the probe would be measuring an idle machine.");
    }
}
