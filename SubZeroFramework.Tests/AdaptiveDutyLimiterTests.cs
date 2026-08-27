using NUnit.Framework;

using SubZeroFramework.Service.Services;

namespace SubZeroFramework.Tests;

/// <summary>
/// Cover for the load limiter converging on its target without knowing anything about the scheduler.
/// </summary>
/// <remarks>
/// <para>
/// Deterministic and instant: the "machine" below is a few lines of arithmetic that distorts sleep requests
/// the way a real scheduler does. That matters because the failure being guarded against is precisely a
/// machine whose sleeps do not cost what was asked — which cannot be arranged on demand in a hardware test,
/// and which differs between the machine a developer has and the machine a user has.
/// </para>
/// <para>
/// <see cref="LoadGeneratorStabilityTests"/> then confirms the same behaviour on real silicon. These prove it
/// converges at all, and on schedulers nobody here owns.
/// </para>
/// </remarks>
[TestFixture]
public class AdaptiveDutyLimiterTests
{
    /// <summary>The shortest slice of work the limiter may schedule.</summary>
    private static readonly TimeSpan MinimumBurn = TimeSpan.FromMilliseconds(2);

    /// <summary>An exact scheduler — the easy case, and the one every other case must not be worse than.</summary>
    [TestCase(0.3d)]
    [TestCase(0.5d)]
    [TestCase(0.8d)]
    public void Converges_OnAnExactScheduler(double target)
    {
        var achieved = Simulate(target, new Scheduler(granularity: TimeSpan.Zero, overheadRatio: 1d));

        Assert.That(achieved, Is.EqualTo(target).Within(0.03d));
    }

    /// <summary>
    /// The Windows case: a request is rounded up to the next 15.6 ms tick.
    /// </summary>
    /// <remarks>
    /// This is the scheduler that defeated every open-loop attempt. With a fixed 10 ms slice of work it is
    /// not merely inaccurate — 80% is UNREACHABLE, because a 10 ms burn against the shortest possible sleep
    /// is 39% busy and asking for less idle changes nothing. Reaching the target requires working longer,
    /// which is what this limiter does and what calculating a sleep never could.
    /// </remarks>
    [TestCase(0.3d)]
    [TestCase(0.5d)]
    [TestCase(0.8d)]
    [TestCase(0.9d)]
    public void Converges_WhenSleepsAreRoundedUpToTicks(double target)
    {
        var achieved = Simulate(target, new Scheduler(granularity: TimeSpan.FromMilliseconds(15.6d), overheadRatio: 1d));

        Assert.That(achieved, Is.EqualTo(target).Within(0.03d));
    }

    /// <summary>A machine that oversleeps everything by half — loaded, or power-managed.</summary>
    [TestCase(0.3d)]
    [TestCase(0.8d)]
    public void Converges_WhenSleepsRunLong(double target)
    {
        var achieved = Simulate(target, new Scheduler(granularity: TimeSpan.Zero, overheadRatio: 1.5d));

        Assert.That(achieved, Is.EqualTo(target).Within(0.03d));
    }

    /// <summary>Coarse AND slow at once, which is the realistic worst case.</summary>
    [TestCase(0.3d)]
    [TestCase(0.8d)]
    public void Converges_OnACoarseAndSlowScheduler(double target)
    {
        var achieved = Simulate(target, new Scheduler(granularity: TimeSpan.FromMilliseconds(20d), overheadRatio: 1.3d));

        Assert.That(achieved, Is.EqualTo(target).Within(0.04d));
    }

    /// <summary>A machine with a raised timer resolution, where sleeps are far finer than the default.</summary>
    [TestCase(0.3d)]
    [TestCase(0.8d)]
    public void Converges_OnAFineGrainedScheduler(double target)
    {
        var achieved = Simulate(target, new Scheduler(granularity: TimeSpan.FromMilliseconds(1d), overheadRatio: 1d));

        Assert.That(achieved, Is.EqualTo(target).Within(0.03d));
    }

    [Test]
    public void LearnsTheSchedulersActualQuantum_RatherThanAssumingOne()
    {
        var limiter = new AdaptiveDutyLimiter(MinimumBurn);
        var scheduler = new Scheduler(TimeSpan.FromMilliseconds(1d), 1d);

        Run(limiter, scheduler, target: 0.5d, cycles: 400, out _, out _);

        // A machine whose sleeps cost a millisecond must not be treated as one whose sleeps cost sixteen, or
        // every slice of work is sized more than an order of magnitude too long.
        Assert.That(limiter.ObservedQuantum, Is.LessThan(TimeSpan.FromMilliseconds(3d)));
    }

    [Test]
    public void ApproachesTargetFromBelow_RatherThanOvershootingFirst()
    {
        var limiter = new AdaptiveDutyLimiter(MinimumBurn);
        var scheduler = new Scheduler(TimeSpan.FromMilliseconds(15.6d), 1d);

        // The first window a user would feel. Overshooting means the machine lurches at the moment the
        // calibration starts, which is exactly when they are watching it.
        Run(limiter, scheduler, target: 0.8d, cycles: 1, out _, out _);
        while (!limiter.HasMeasurement)
        {
            Run(limiter, scheduler, target: 0.8d, cycles: 1, out _, out _);
        }

        Assert.That(limiter.ObservedFraction, Is.LessThanOrEqualTo(0.8d));
    }

    [Test]
    public void HoldsTheFloor_WhenTheTargetIsUnreachablyHigh()
    {
        var limiter = new AdaptiveDutyLimiter(MinimumBurn);
        var scheduler = new Scheduler(TimeSpan.FromMilliseconds(15.6d), 1d);

        // Asking for everything must saturate rather than growing without bound, which would turn a steady
        // background load into a machine that freezes for a second at a time.
        Run(limiter, scheduler, target: 0.999d, cycles: 2000, out _, out _);

        Assert.That(limiter.BurnFor, Is.LessThanOrEqualTo(TimeSpan.FromMilliseconds(400d)));
    }

    /// <summary>Settles the limiter, then reports the duty actually achieved over the following cycles.</summary>
    private static double Simulate(double target, Scheduler scheduler)
    {
        var limiter = new AdaptiveDutyLimiter(MinimumBurn);

        Run(limiter, scheduler, target, cycles: 3000, out _, out _);
        Run(limiter, scheduler, target, cycles: 3000, out var busy, out var idle);

        var total = busy + idle;
        return total > TimeSpan.Zero ? busy / total : 0d;
    }

    /// <summary>
    /// Drives a fixed number of cycles.
    /// </summary>
    /// <remarks>
    /// A fixed count rather than "until the window advances": once the limiter converges its measurement can
    /// repeat exactly, and a loop waiting for that value to change never returns.
    /// </remarks>
    private static void Run(
        AdaptiveDutyLimiter limiter,
        Scheduler scheduler,
        double target,
        int cycles,
        out TimeSpan busy,
        out TimeSpan idle)
    {
        busy = TimeSpan.Zero;
        idle = TimeSpan.Zero;

        for (var i = 0; i < cycles; i++)
        {
            var burn = limiter.BurnFor;
            var slept = scheduler.Sleep(limiter.SleepFor);

            busy += burn;
            idle += slept;

            limiter.Record(burn, slept, target);
        }
    }

    /// <summary>
    /// A scheduler that serves sleep requests the way real ones do — rounded up, and possibly slow.
    /// </summary>
    /// <param name="granularity">Requests are rounded up to a multiple of this. Zero means exact.</param>
    /// <param name="overheadRatio">Everything then takes this much longer than asked.</param>
    private sealed class Scheduler(TimeSpan granularity, double overheadRatio)
    {
        public TimeSpan Sleep(TimeSpan requested)
        {
            if (requested <= TimeSpan.Zero)
            {
                return TimeSpan.Zero;
            }

            var served = requested;

            if (granularity > TimeSpan.Zero)
            {
                served = granularity * Math.Ceiling(requested / granularity);
            }

            return served * overheadRatio;
        }
    }
}
