using NUnit.Framework;

using SubZeroFramework.Service.Services;

namespace SubZeroFramework.Tests;

/// <summary>
/// Cover for the load generator aiming at a share of the MACHINE rather than a share of itself.
/// </summary>
/// <remarks>
/// The distinction only shows up on a busy machine, which is exactly where it matters: holding a private 80%
/// while something else takes another 20% leaves the machine saturated, and the target was chosen precisely
/// to prevent that. These use a scripted probe so the behaviour can be tested without arranging real
/// background load — <see cref="LoadGeneratorStabilityTests"/> covers the real thing on an idle machine.
/// </remarks>
[TestFixture]
public class CpuLoadGovernorTests
{
    /// <summary>
    /// A fast governor tick, so these spend their time on the decision rather than on the smoothing.
    /// </summary>
    /// <remarks>
    /// The governor moves a quarter of the way to its new target per tick, which takes roughly nine ticks to
    /// converge. At the production half-second that is four and a half seconds of pure waiting per test, for
    /// behaviour that is identical at any tick rate — the smoothing is a filter on noisy readings, and these
    /// probes are scripted.
    /// </remarks>
    private static readonly TimeSpan GovernorInterval = TimeSpan.FromMilliseconds(20);

    /// <summary>Short, because these tests are about the governor's target and not about the climb.</summary>
    private static readonly TimeSpan TestRamp = TimeSpan.FromMilliseconds(500);

    /// <summary>Generous relative to the tick rate above, so a slow machine does not fail on timing.</summary>
    private static readonly TimeSpan SettleTimeout = TimeSpan.FromSeconds(3);

    [Test]
    public async Task Generator_GivesAwayHeadroom_WhenSomethingElseIsUsingTheMachine()
    {
        // The machine is at 50%, of which 10 points are ours: 40% belongs to something else.
        var probe = new ScriptedSystemLoadProbe { Total = 0.5d, Own = 0.1d };
        using var generator = new CpuLoadGenerator(probe, TestRamp, GovernorInterval);

        generator.Start();

        try
        {
            // 80% target less 40% foreign leaves 40% for us.
            var settled = await WaitForTargetAsync(generator, 0.4d).ConfigureAwait(false);

            Assert.That(
                settled,
                Is.EqualTo(0.4d).Within(0.05d),
                $"the generator aimed at {settled:P0} while something else already had 40% of the machine.");
        }
        finally
        {
            generator.Stop();
        }
    }

    [Test]
    public async Task Generator_TakesTheHeadroomBack_WhenTheOtherWorkStops()
    {
        var probe = new ScriptedSystemLoadProbe { Total = 0.5d, Own = 0.1d };
        using var generator = new CpuLoadGenerator(probe, TestRamp, GovernorInterval);

        generator.Start();

        try
        {
            await WaitForTargetAsync(generator, 0.4d).ConfigureAwait(false);

            // The other work finishes; everything busy is now ours.
            probe.Total = 0.4d;
            probe.Own = 0.4d;

            var settled = await WaitForTargetAsync(generator, LoadRamp.DefaultTargetFraction).ConfigureAwait(false);

            // Yielding is only half of it — a generator that never took the room back would spend the rest of
            // a multi-minute run under-loading a machine that had gone idle again.
            Assert.That(settled, Is.EqualTo(LoadRamp.DefaultTargetFraction).Within(0.05d));
        }
        finally
        {
            generator.Stop();
        }
    }

    [Test]
    public async Task Generator_TakesTheFullTarget_WhenTheMachineIsOtherwiseIdle()
    {
        // Everything busy is ours, so there is nothing to give away.
        var probe = new ScriptedSystemLoadProbe { Total = 0.8d, Own = 0.8d };
        using var generator = new CpuLoadGenerator(probe, TestRamp, GovernorInterval);

        generator.Start();

        try
        {
            var settled = await WaitForTargetAsync(generator, LoadRamp.DefaultTargetFraction).ConfigureAwait(false);

            Assert.That(settled, Is.EqualTo(LoadRamp.DefaultTargetFraction).Within(0.05d));
        }
        finally
        {
            generator.Stop();
        }
    }

    [Test]
    public async Task Generator_KeepsAFloor_WhenTheMachineIsAlreadySaturated()
    {
        // Something else is using the whole machine.
        var probe = new ScriptedSystemLoadProbe { Total = 1.0d, Own = 0.0d };
        using var generator = new CpuLoadGenerator(probe, TestRamp, GovernorInterval);

        generator.Start();

        try
        {
            var settled = await WaitForTargetAsync(generator, 0.2d).ConfigureAwait(false);

            // Backing off to nothing would make the run a measurement of whatever the user happened to be
            // doing. Refusing the run is the calibration's job, via its minimum-power check — not silent
            // starvation here.
            Assert.That(
                settled,
                Is.GreaterThan(0.1d),
                "the generator gave up all of its load on a busy machine instead of holding its floor.");
        }
        finally
        {
            generator.Stop();
        }
    }

    [Test]
    public void Generator_HoldsItsOwnTarget_WhenTheMachineCannotReportLoad()
    {
        // No probe at all: a platform that cannot report system load degrades to holding its own fixed share,
        // which is correct on an idle machine and too much on a busy one — the honest limit of no information.
        using var generator = new CpuLoadGenerator(rampDuration: TestRamp);

        generator.Start();

        try
        {
            Assert.That(generator.EffectiveTargetFraction, Is.EqualTo(LoadRamp.DefaultTargetFraction));
        }
        finally
        {
            generator.Stop();
        }
    }

    /// <summary>
    /// Waits for the governor's target to converge.
    /// </summary>
    /// <remarks>
    /// Reads <see cref="CpuLoadGenerator.EffectiveTargetFraction"/> rather than CurrentLoadFraction, which is
    /// that value capped by the ramp — over a thirty-second climb the cap would hide what the governor
    /// decided for most of the test.
    /// </remarks>
    private static async Task<double> WaitForTargetAsync(CpuLoadGenerator generator, double expected)
    {
        var deadline = DateTimeOffset.UtcNow + SettleTimeout;
        var settled = generator.EffectiveTargetFraction;

        while (DateTimeOffset.UtcNow < deadline)
        {
            await Task.Delay(20).ConfigureAwait(false);
            settled = generator.EffectiveTargetFraction;

            if (Math.Abs(settled - expected) < 0.03d)
            {
                return settled;
            }
        }

        return settled;
    }

    /// <summary>A probe that reports whatever a test tells it to.</summary>
    private sealed class ScriptedSystemLoadProbe : ISystemLoadProbe
    {
        public double? Total { get; set; }

        public double Own { get; set; }

        public double? TotalCpuUtilizationFraction => Total;

        public double OwnCpuUtilizationFraction => Own;
    }
}
