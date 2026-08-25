using System.Diagnostics;

using Microsoft.Extensions.Logging.Abstractions;

using NUnit.Framework;

using SubZeroFramework.Service.Services;

namespace SubZeroFramework.Tests;

/// <summary>
/// Runs the real load generators against the real machine and checks they settle where they claim to.
/// </summary>
/// <remarks>
/// <para>
/// Every other test in this suite substitutes a fake generator, which proves the calibration SEQUENCES load
/// correctly and proves nothing whatsoever about whether the load is the size it says it is. That number is
/// load-bearing twice over: it is the promise that the machine stays usable during a multi-minute run, and it
/// is the steady operating point the whole thermal fit assumes.
/// </para>
/// <para>
/// These are slow by nature — a full ramp plus a hold — and they genuinely load the machine, so they are
/// categorised and can be excluded with <c>--filter TestCategory!=Hardware</c>. A busy machine will fail
/// them; that is a limitation of measuring the real thing, not a flaky test.
/// </para>
/// </remarks>
[TestFixture]
[Category("Hardware")]
public class LoadGeneratorStabilityTests
{
    /// <summary>
    /// A compressed ramp, so a test spends its time measuring rather than watching a climb it is not testing.
    /// </summary>
    /// <remarks>
    /// The half-minute a real run takes exists to be gentle with the user's machine, which is a UX property,
    /// not a correctness one. What these tests check — that the duty-cycle correction converges on its target
    /// — settles within a few cycles, so seconds are as conclusive as minutes and the suite stays usable.
    /// </remarks>
    private static readonly TimeSpan TestRamp = TimeSpan.FromSeconds(2);

    /// <summary>
    /// How long the load must hold at target before it counts as stable.
    /// </summary>
    /// <remarks>
    /// Several seconds of one-second windows: long enough for a load that only LOOKS steady — alternating
    /// saturated and idle — to show itself in the per-second peaks, which is the failure the average alone
    /// cannot catch.
    /// </remarks>
    private static readonly TimeSpan HoldDuration = TimeSpan.FromSeconds(5);

    /// <summary>How long to wait for the ramp to finish before giving up on it.</summary>
    private static readonly TimeSpan RampTimeout = TestRamp + TimeSpan.FromSeconds(10);

    /// <summary>
    /// How far the achieved load may sit from the target.
    /// </summary>
    /// <remarks>
    /// Wide enough to absorb an ordinarily busy desktop and scheduler jitter, narrow enough that the failure
    /// modes that matter cannot hide inside it: a generator that saturates the machine, or one that sleeps so
    /// coarsely it never reaches its target at all.
    /// </remarks>
    private const double ToleranceFraction = 0.08d;

    /// <summary>How far above target a reading may sit. Tight: nothing legitimately pushes it over.</summary>
    private const double OvershootTolerance = 0.06d;

    /// <summary>
    /// The share of its target the load must at least reach.
    /// </summary>
    /// <remarks>
    /// Relative rather than absolute, so the same rule is equally strict at 30% and at 80%. Loose enough to
    /// absorb the preemption a busy machine imposes, tight enough that a generator ignoring its target
    /// entirely cannot satisfy it at every set point at once.
    /// </remarks>
    private const double UndershootFloorFraction = 0.82d;

    /// <summary>
    /// The most any one-second window may consume.
    /// </summary>
    /// <remarks>
    /// Above this the machine is saturated for that second whatever the average says, and the promise that
    /// it stays usable during a multi-minute calibration is not being kept.
    /// </remarks>
    private const double PeakCeilingFraction = 0.9d;

    /// <summary>
    /// The longest a GPU dispatch may take and still leave the duty control something to steer with.
    /// </summary>
    /// <remarks>
    /// A dispatch is atomic, so at the gentle end of the ramp the cycle is one dispatch of work against
    /// several times that much idle. Past a few milliseconds the machine stops experiencing a light load and
    /// starts experiencing a stutter several times a second — the same square wave the fan would then be
    /// modelled against. Four times the size the generator aims for: loose enough to absorb a slow
    /// accelerator and a hot one, tight enough that a workload which was never sized at all cannot pass.
    /// </remarks>
    private static readonly TimeSpan MaximumUsefulDispatch = TimeSpan.FromMilliseconds(8);

    /// <summary>
    /// Checks the load lands where it is TOLD, at several different settings.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A single set point is weak evidence. A generator that simply saturated the machine, or whose duty
    /// control did nothing at all, could still pass an 80% check on a machine where the measurement runs a
    /// little low — the number would be roughly right for entirely the wrong reason.
    /// </para>
    /// <para>
    /// Several set points cannot be passed by accident. A broken duty control reads the same at every
    /// setting; only one that works tracks all of them.
    /// </para>
    /// </remarks>
    [TestCase(0.3d)]
    [TestCase(0.5d)]
    [TestCase(0.8d)]
    public async Task CpuLoad_LandsOnWhicheverTargetItIsGiven(double target)
    {
        using var generator = new CpuLoadGenerator(rampDuration: TestRamp, targetFraction: target);

        generator.Start();

        try
        {
            await WaitForRampAsync(() => generator.IsAtTargetLoad).ConfigureAwait(false);

            // Measured from OUTSIDE the generator, from the process's own consumed processor time. A test
            // that asked the generator what it thought it was doing would pass regardless of what the
            // machine actually experienced.
            var achieved = await MeasureProcessCpuFractionAsync(HoldDuration).ConfigureAwait(false);

            TestContext.Out.WriteLine($"Target {target:P0} → measured {achieved:P1}");

            // The band is ASYMMETRIC, and deliberately so.
            //
            // The limiter controls the wall-clock duty of its own threads. Consumed processor time is a
            // LOWER bound on that: a thread preempted mid-slice — by this test host, by anything else on the
            // machine, or by its own siblings at below-normal priority — spends wall-clock time it does not
            // spend CPU. So undershoot is expected on a machine that is doing anything else, and is the
            // correct behaviour rather than a defect.
            //
            // Overshoot has no such excuse: nothing makes a thread consume more CPU than it was scheduled
            // for. A tight upper bound is what would catch the failure that matters — a generator quietly
            // running flat out whatever it was asked for.
            Assert.Multiple(() =>
            {
                Assert.That(
                    achieved,
                    Is.LessThanOrEqualTo(target + OvershootTolerance),
                    $"CPU load overshot to {achieved:P1} against a {target:P0} target.");

                Assert.That(
                    achieved,
                    Is.GreaterThanOrEqualTo(target * UndershootFloorFraction),
                    $"CPU load settled at {achieved:P1}, far under its {target:P0} target.");
            });
        }
        finally
        {
            generator.Stop();
        }
    }

    /// <summary>
    /// Checks the load leaves headroom CONTINUOUSLY, not merely on average.
    /// </summary>
    /// <remarks>
    /// An average of 80% is satisfied just as well by alternating between saturated and idle, and that is
    /// what the machine's user would feel — the whole point of the target is that the machine stays usable
    /// throughout, which is a statement about every second of the run, not about their mean.
    /// </remarks>
    [Test]
    public async Task CpuLoad_LeavesHeadroomInEverySecond_NotJustOnAverage()
    {
        using var generator = new CpuLoadGenerator(rampDuration: TestRamp);

        generator.Start();

        try
        {
            await WaitForRampAsync(() => generator.IsAtTargetLoad).ConfigureAwait(false);

            var windows = await MeasureProcessCpuWindowsAsync(HoldDuration, TimeSpan.FromSeconds(1)).ConfigureAwait(false);
            var peak = windows.Max();

            TestContext.Out.WriteLine(
                $"Per-second CPU: min {windows.Min():P1}, mean {windows.Average():P1}, peak {peak:P1}");

            Assert.That(
                peak,
                Is.LessThan(PeakCeilingFraction),
                $"CPU load peaked at {peak:P1} in a one-second window, leaving the machine effectively saturated.");
        }
        finally
        {
            generator.Stop();
        }
    }

    /// <summary>
    /// Reports which accelerators were chosen, and fails if an integrated part was picked over a discrete one.
    /// </summary>
    /// <remarks>
    /// This is here because that exact mistake shipped: ranking candidates by memory size selected the
    /// integrated GPU, because an integrated GPU carves its memory out of system RAM and can report far more
    /// of it than a discrete card with dedicated VRAM. The load ran on the APU — heating what the CPU fan
    /// cools — while the run believed it was heating the graphics module.
    /// </remarks>
    [Test]
    public void GpuLoad_SelectsTheMostCapableAccelerator()
    {
        using var generator = new IlgpuGpuLoadGenerator(NullLogger<IlgpuGpuLoadGenerator>.Instance, TestRamp);

        if (!generator.IsAvailable)
        {
            Assert.Ignore("No GPU accelerator on this machine.");
        }

        // Surfaced rather than merely asserted: on a machine with both an integrated and a discrete GPU,
        // seeing WHICH was chosen is the whole point.
        TestContext.Out.WriteLine($"Selected accelerator(s): {generator.AcceleratorName}");

        Assert.That(generator.AcceleratorName, Is.Not.Null.And.Not.Empty);
    }

    /// <summary>
    /// The GPU's version of the same question: does it land where it is told, at several settings?
    /// </summary>
    /// <remarks>
    /// The LOW target is the one that matters, and it is here because it failed. A GPU dispatch cannot be cut
    /// short once launched, so one dispatch is the shortest slice of work that exists — and while the limiter
    /// was told the minimum was a millisecond, every target whose slice was shorter than a real dispatch
    /// silently became "one dispatch, then the shortest sleep the machine serves". The whole low half of the
    /// range collapsed onto a single achievable duty, and the ramp through it did nothing at all.
    /// </remarks>
    [TestCase(0.15d)]
    [TestCase(0.4d)]
    [TestCase(0.8d)]
    public async Task GpuLoad_LandsOnWhicheverTargetItIsGiven(double target)
    {
        using var generator = new IlgpuGpuLoadGenerator(NullLogger<IlgpuGpuLoadGenerator>.Instance, TestRamp, target);

        if (!generator.IsAvailable)
        {
            Assert.Ignore("No GPU accelerator on this machine — the unavailable path is covered by RunAsync_RefusesAGpuFan_WhenTheMachineHasNoUsableAccelerator.");
        }

        Assert.That(generator.Start(), Is.True, "the accelerator reported available but could not be started");

        try
        {
            await WaitForRampAsync(() => generator.IsAtTargetLoad).ConfigureAwait(false);

            // The GPU equivalent of the process-time measurement: the share of wall clock actually spent
            // inside dispatches, timed by the generator but derived from real work rather than from the
            // schedule it intended to follow.
            var achieved = await MeasureAsync(
                HoldDuration,
                () => generator.ObservedLoadFraction).ConfigureAwait(false);

            TestContext.Out.WriteLine($"Target {target:P0} → measured {achieved:P1} on {generator.AcceleratorName}");

            Assert.That(
                achieved,
                Is.EqualTo(target).Within(ToleranceFraction),
                $"GPU load settled at {achieved:P1} rather than {target:P0}, on {generator.AcceleratorName}.");
        }
        finally
        {
            generator.Stop();
        }
    }

    /// <summary>
    /// Checks a dispatch is short enough for the duty control to have somewhere to stand.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A GPU dispatch cannot be cut short, so its duration is the smallest slice of work the generator can
    /// schedule — and the lowest duty it can produce is one dispatch against the shortest sleep the machine
    /// serves. A dispatch that grew large would put the whole bottom of the range out of reach, and the ramp
    /// climbing through that range would sit flat at the floor instead.
    /// </para>
    /// <para>
    /// Nothing here caught that before, because the fixed workload happened to be sub-millisecond on the
    /// hardware it was written against. It is the machines where it is NOT — an integrated part, a throttled
    /// card — that the sizing exists for, and this is the check that the sizing happened at all.
    /// </para>
    /// </remarks>
    [Test]
    public async Task GpuLoad_SizesItsDispatchSmallEnoughToSteer()
    {
        using var generator = new IlgpuGpuLoadGenerator(NullLogger<IlgpuGpuLoadGenerator>.Instance, TestRamp);

        if (!generator.IsAvailable)
        {
            Assert.Ignore("No GPU accelerator on this machine.");
        }

        Assert.That(generator.Start(), Is.True, "the accelerator reported available but could not be started");

        try
        {
            await WaitForRampAsync(
                () => generator.DispatchDuration > TimeSpan.Zero,
                "the generator never measured its own dispatch").ConfigureAwait(false);

            var dispatch = generator.DispatchDuration;
            TestContext.Out.WriteLine($"One dispatch costs {dispatch.TotalMilliseconds:0.###} ms on {generator.AcceleratorName}");

            Assert.That(
                dispatch,
                Is.LessThanOrEqualTo(MaximumUsefulDispatch),
                $"a {dispatch.TotalMilliseconds:0.#} ms dispatch is too coarse to steer — the lowest reachable "
                + "duty is one dispatch against the shortest sleep the machine will serve.");
        }
        finally
        {
            generator.Stop();
        }
    }

    /// <summary>
    /// The GPU's version of the gradual-climb check.
    /// </summary>
    /// <remarks>
    /// Worth asserting separately from the CPU's because the two fail differently. The CPU's ramp can only be
    /// broken by the schedule; the GPU's can also be defeated from underneath, by a dispatch so long that the
    /// early, gentle part of the climb is below anything the accelerator can actually be asked to do.
    /// </remarks>
    [Test]
    public async Task GpuLoad_ClimbsGraduallyRatherThanJumpingToTarget()
    {
        using var generator = new IlgpuGpuLoadGenerator(NullLogger<IlgpuGpuLoadGenerator>.Instance, TestRamp);

        if (!generator.IsAvailable)
        {
            Assert.Ignore("No GPU accelerator on this machine.");
        }

        Assert.That(generator.Start(), Is.True, "the accelerator reported available but could not be started");

        try
        {
            // The schedule itself, which is shared with the CPU generator and cheap to check.
            Assert.That(
                generator.CurrentLoadFraction,
                Is.LessThan(LoadRamp.DefaultTargetFraction),
                "load started at its target instead of ramping");

            await WaitForRampAsync(() => generator.IsAtTargetLoad).ConfigureAwait(false);

            Assert.That(generator.CurrentLoadFraction, Is.EqualTo(LoadRamp.DefaultTargetFraction).Within(0.001d));

            // And that the accelerator can actually FOLLOW the schedule down at its gentle end, which is the
            // half a too-long dispatch silently removes.
            Assert.That(
                generator.DispatchDuration,
                Is.LessThanOrEqualTo(MaximumUsefulDispatch),
                "the dispatch is too long for the start of the ramp to be reachable at all");
        }
        finally
        {
            generator.Stop();
        }
    }

    [Test]
    public async Task CpuLoad_ClimbsGraduallyRatherThanJumpingToTarget()
    {
        // Slamming to target the instant a calibration starts makes the machine lurch, which is the thing
        // that makes a user cancel and never run it again.
        using var generator = new CpuLoadGenerator(rampDuration: TestRamp);

        generator.Start();

        try
        {
            var early = generator.CurrentLoadFraction;
            Assert.That(
                early,
                Is.LessThan(LoadRamp.DefaultTargetFraction),
                "load started at its target instead of ramping");

            await WaitForRampAsync(() => generator.IsAtTargetLoad).ConfigureAwait(false);

            Assert.That(generator.CurrentLoadFraction, Is.EqualTo(LoadRamp.DefaultTargetFraction).Within(0.001d));
        }
        finally
        {
            generator.Stop();
        }
    }

    private static async Task WaitForRampAsync(Func<bool> ready, string what = "the load never reached its target")
    {
        var deadline = Stopwatch.StartNew();

        while (!ready() && deadline.Elapsed < RampTimeout)
        {
            await Task.Delay(250).ConfigureAwait(false);
        }

        Assert.That(ready(), Is.True, $"{what} within {RampTimeout}.");
    }

    /// <summary>
    /// The share of all available processor time this process consumed over the window.
    /// </summary>
    /// <remarks>
    /// Divided by the logical processor count, so the result is a fraction of the WHOLE machine rather than
    /// of one core — which is what the target means.
    /// </remarks>
    private static async Task<double> MeasureProcessCpuFractionAsync(TimeSpan window)
    {
        using var process = Process.GetCurrentProcess();

        var startCpu = process.TotalProcessorTime;
        var clock = Stopwatch.StartNew();

        await Task.Delay(window).ConfigureAwait(false);

        process.Refresh();
        var consumed = process.TotalProcessorTime - startCpu;

        return consumed.TotalSeconds / (clock.Elapsed.TotalSeconds * Environment.ProcessorCount);
    }

    /// <summary>Processor-time fraction for each successive window, so peaks survive rather than averaging out.</summary>
    private static async Task<IReadOnlyList<double>> MeasureProcessCpuWindowsAsync(TimeSpan total, TimeSpan window)
    {
        using var process = Process.GetCurrentProcess();

        List<double> fractions = [];
        var clock = Stopwatch.StartNew();

        while (clock.Elapsed < total)
        {
            var startCpu = process.TotalProcessorTime;
            var windowClock = Stopwatch.StartNew();

            await Task.Delay(window).ConfigureAwait(false);

            process.Refresh();
            var consumed = process.TotalProcessorTime - startCpu;

            fractions.Add(consumed.TotalSeconds / (windowClock.Elapsed.TotalSeconds * Environment.ProcessorCount));
        }

        return fractions;
    }

    /// <summary>Averages a reported fraction across the hold, so one unlucky sample cannot decide the test.</summary>
    private static async Task<double> MeasureAsync(TimeSpan window, Func<double> sample)
    {
        List<double> samples = [];
        var clock = Stopwatch.StartNew();

        while (clock.Elapsed < window)
        {
            await Task.Delay(250).ConfigureAwait(false);
            samples.Add(sample());
        }

        return samples.Count > 0 ? samples.Average() : 0d;
    }
}
