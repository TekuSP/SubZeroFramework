using NUnit.Framework;

using SubZeroFramework.Services.Control;

namespace SubZeroFramework.Tests;

/// <summary>
/// The conversion from Windows' frequency-scaled utility counter to a plain busy fraction.
/// </summary>
/// <remarks>
/// <para>
/// This exists because the obvious implementation is wrong in a way that looks right. Clamping
/// <c>% Processor Utility</c> to 1 produces a number in the correct range that moves in the correct
/// direction, and it is only wrong once the machine boosts — at which point it saturates and stops carrying
/// information. That shipped, and the UI reported 100% usage on a machine at 48% load.
/// </para>
/// <para>
/// The fixtures below are MEASURED, not invented: each pairs the utility and performance counters read at the
/// same instant on a Ryzen AI 9 HX 370 with what <c>% Processor Time</c> said at that instant, which is the
/// independent ground truth for busy time.
/// </para>
/// </remarks>
[TestFixture]
public class ProcessorUtilityMathTests
{
    /// <summary>
    /// Simultaneous counter readings from a real machine, with the busy fraction to reproduce.
    /// </summary>
    /// <param name="Label">The load that produced it.</param>
    /// <param name="UtilityPercent">What <c>% Processor Utility</c> read.</param>
    /// <param name="PerformancePercent">What <c>% Processor Performance</c> read.</param>
    /// <param name="ProcessorTimePercent">What <c>% Processor Time</c> read — the ground truth.</param>
    public sealed record CounterReading(
        string Label,
        double UtilityPercent,
        double PerformancePercent,
        double ProcessorTimePercent)
    {
        public override string ToString() => Label;
    }

    /// <summary>
    /// Four load levels on a Ryzen AI 9 HX 370, spanning idle to fully loaded.
    /// </summary>
    /// <remarks>
    /// The idle row is the one that would be easiest to omit and is the most diagnostic: utility reads 40%
    /// there against 19.5% real busy time, so an implementation that merely clamps is already wrong by a
    /// factor of two BEFORE any saturation, on a machine doing nothing.
    /// </remarks>
    private static readonly CounterReading[] MeasuredReadings =
    [
        new("idle", UtilityPercent: 40.0d, PerformancePercent: 207.5d, ProcessorTimePercent: 19.5d),
        new("6 busy threads", UtilityPercent: 77.9d, PerformancePercent: 206.6d, ProcessorTimePercent: 36.8d),
        new("12 busy threads", UtilityPercent: 139.2d, PerformancePercent: 167.6d, ProcessorTimePercent: 84.1d),
        new("24 busy threads", UtilityPercent: 133.1d, PerformancePercent: 167.3d, ProcessorTimePercent: 79.5d),
    ];

    /// <summary>
    /// Dividing utility by the speed ratio reproduces the independently measured busy time.
    /// </summary>
    /// <remarks>
    /// The tolerance is two points because the three counters are sampled by separate PDH calls over slightly
    /// different windows on a machine that is not perfectly steady. Tightening it further would be testing the
    /// stability of the measurement rather than the correctness of the arithmetic.
    /// </remarks>
    [Test]
    [TestCaseSource(nameof(MeasuredReadings))]
    public void ToBusyFraction_ReproducesProcessorTime(CounterReading reading)
    {
        var busy = ProcessorUtilityMath.ToBusyFraction(
            reading.UtilityPercent / 100d,
            reading.PerformancePercent / 100d);

        Assert.That(
            busy * 100d,
            Is.EqualTo(reading.ProcessorTimePercent).Within(2.0d),
            $"At {reading.Label}, utility {reading.UtilityPercent}% over performance {reading.PerformancePercent}% "
                + $"should land on the {reading.ProcessorTimePercent}% that % Processor Time reported.");
    }

    /// <summary>
    /// The bug this replaced: clamping the raw utility saturates while the machine still has headroom.
    /// </summary>
    /// <remarks>
    /// Sabotage check. If someone reverts to clamping the raw reading, the test above still passes for the
    /// idle row within its tolerance on some machines, so this pins the specific failure — two DIFFERENT real
    /// loads, 84.1% and 79.5% busy, that the old code reported as an identical 100%.
    /// </remarks>
    [Test]
    public void ToBusyFraction_DistinguishesLoadsThatClampingCollapsed()
    {
        var twelveThreads = ProcessorUtilityMath.ToBusyFraction(139.2d / 100d, 167.6d / 100d);
        var twentyFourThreads = ProcessorUtilityMath.ToBusyFraction(133.1d / 100d, 167.3d / 100d);

        Assert.Multiple(() =>
        {
            Assert.That(twelveThreads, Is.LessThan(1d), "A boosting machine at 84% busy must not report as fully loaded.");
            Assert.That(twentyFourThreads, Is.LessThan(1d), "A boosting machine at 80% busy must not report as fully loaded.");
            Assert.That(
                twelveThreads,
                Is.Not.EqualTo(twentyFourThreads).Within(0.01d),
                "Two measurably different loads must not collapse onto the same reported figure.");
        });
    }

    /// <summary>A boosting processor genuinely at full load still reports full load, not more.</summary>
    [Test]
    public void ToBusyFraction_ClampsToOne_WhenUtilityMatchesTheSpeedRatio()
    {
        // Fully busy at 2.07x nominal reads as 207% utility, which is 100% busy — not 207%.
        Assert.That(ProcessorUtilityMath.ToBusyFraction(2.07d, 2.07d), Is.EqualTo(1d).Within(1e-9d));
    }

    /// <summary>
    /// A throttled processor is scaled UP, not down, and the direction is easy to get backwards.
    /// </summary>
    /// <remarks>
    /// Below nominal the ratio is under 1, so dividing increases the figure. A core pegged at half speed and
    /// fully busy reports 50% utility and is 100% busy — multiplying instead of dividing would report 25%,
    /// which would have the fan back off exactly when the processor is thermally limited.
    /// </remarks>
    [Test]
    public void ToBusyFraction_ReportsFullyBusy_WhenAThrottledCoreIsSaturated()
    {
        Assert.That(ProcessorUtilityMath.ToBusyFraction(0.5d, 0.5d), Is.EqualTo(1d).Within(1e-9d));
    }

    /// <summary>Without a ratio the reading cannot be corrected, so it degrades to the old clamped behaviour.</summary>
    [Test]
    public void ToBusyFraction_FallsBackToClamping_WhenNoRatioIsAvailable()
    {
        Assert.Multiple(() =>
        {
            Assert.That(ProcessorUtilityMath.ToBusyFraction(0.42d, null), Is.EqualTo(0.42d).Within(1e-9d));
            Assert.That(ProcessorUtilityMath.ToBusyFraction(1.35d, null), Is.EqualTo(1d).Within(1e-9d));
        });
    }

    /// <summary>
    /// A parked core reports a ratio at or near zero, and dividing by it must not invent a busy core.
    /// </summary>
    [Test]
    [TestCase(0d)]
    [TestCase(-1d)]
    [TestCase(0.001d)]
    [TestCase(double.NaN)]
    public void ToBusyFraction_IgnoresAnUnusableRatio(double ratio)
    {
        // 2% utility against a nonsense ratio: dividing would report a nearly idle core as fully busy.
        Assert.That(ProcessorUtilityMath.ToBusyFraction(0.02d, ratio), Is.EqualTo(0.02d).Within(1e-9d));
    }

    /// <summary>A counter that returns nothing usable reports idle rather than propagating the nonsense.</summary>
    [Test]
    [TestCase(double.NaN)]
    [TestCase(double.PositiveInfinity)]
    public void ToBusyFraction_ReportsIdle_WhenUtilityIsNotFinite(double utility)
    {
        Assert.That(ProcessorUtilityMath.ToBusyFraction(utility, 2d), Is.EqualTo(0d));
    }

    /// <summary>Negative readings, which PDH can emit on a counter rollover, floor at zero.</summary>
    [Test]
    public void ToBusyFraction_FloorsAtZero_WhenUtilityIsNegative()
    {
        Assert.That(ProcessorUtilityMath.ToBusyFraction(-0.5d, 2d), Is.EqualTo(0d));
    }
}
