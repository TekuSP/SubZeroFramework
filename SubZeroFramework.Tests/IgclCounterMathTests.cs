using NUnit.Framework;

using SubZeroFramework.Services.Compute;

namespace SubZeroFramework.Tests;

/// <summary>
/// Exercises the delta arithmetic behind IGCL's monotonic energy and busy-time counters.
/// </summary>
/// <remarks>
/// Separated from the interop precisely so it can be tested here: the reference machine has no Intel GPU, so
/// this is where the reader's actual numeric behaviour gets verified rather than assumed.
/// </remarks>
[TestFixture]
public class IgclCounterMathTests
{
    [Test]
    public void AveragePowerWatts_DividesEnergyDeltaByElapsedTime()
    {
        // 30 J over 2 s is 15 W.
        var watts = IgclCounterMath.AveragePowerWatts(
            previousJoules: 1_000d, previousSeconds: 100d,
            currentJoules: 1_030d, currentSeconds: 102d);

        Assert.That(watts, Is.EqualTo(15d).Within(1e-9));
    }

    [Test]
    public void AverageActivityPercent_DividesBusyTimeByElapsedTime()
    {
        // 0.5 s busy out of 2 s elapsed is 25%.
        var percent = IgclCounterMath.AverageActivityPercent(
            previousBusySeconds: 10d, previousSeconds: 100d,
            currentBusySeconds: 10.5d, currentSeconds: 102d);

        Assert.That(percent, Is.EqualTo(25d).Within(1e-9));
    }

    /// <summary>
    /// Busy time can marginally exceed wall time through counter granularity; 103% busy is an artefact.
    /// </summary>
    [Test]
    public void AverageActivityPercent_ClampsSlightOverrunToOneHundred()
    {
        var percent = IgclCounterMath.AverageActivityPercent(
            previousBusySeconds: 10d, previousSeconds: 100d,
            currentBusySeconds: 12.06d, currentSeconds: 102d);

        Assert.That(percent, Is.EqualTo(100d));
    }

    /// <summary>
    /// A counter going backwards means it reset (driver reload, suspend cycle). The window is unusable, and
    /// clamping to zero would publish "0 W" for a GPU that may well have been busy.
    /// </summary>
    [Test]
    public void NegativeCounterDelta_ReportsUnknownRatherThanZero()
    {
        Assert.Multiple(() =>
        {
            Assert.That(
                IgclCounterMath.AveragePowerWatts(
                    previousJoules: 5_000d, previousSeconds: 100d,
                    currentJoules: 12d, currentSeconds: 102d),
                Is.Null);

            Assert.That(
                IgclCounterMath.AverageActivityPercent(
                    previousBusySeconds: 500d, previousSeconds: 100d,
                    currentBusySeconds: 2d, currentSeconds: 102d),
                Is.Null);
        });
    }

    /// <summary>
    /// Below the minimum window the counters' ~1 ms accuracy dominates, and dividing would amplify
    /// quantisation into readings that swing wildly between ticks.
    /// </summary>
    [Test]
    public void WindowShorterThanTheMinimum_ReportsUnknown()
    {
        var barelyTooShort = IgclCounterMath.MinimumWindowSeconds / 2d;

        Assert.Multiple(() =>
        {
            Assert.That(
                IgclCounterMath.AveragePowerWatts(
                    previousJoules: 1_000d, previousSeconds: 100d,
                    currentJoules: 1_030d, currentSeconds: 100d + barelyTooShort),
                Is.Null);

            Assert.That(
                IgclCounterMath.AverageActivityPercent(
                    previousBusySeconds: 10d, previousSeconds: 100d,
                    currentBusySeconds: 10.01d, currentSeconds: 100d + barelyTooShort),
                Is.Null);
        });
    }

    /// <summary>The first tick has no previous snapshot, which must read as unknown rather than as zero.</summary>
    [Test]
    public void MissingPreviousSnapshot_ReportsUnknown()
    {
        Assert.Multiple(() =>
        {
            Assert.That(
                IgclCounterMath.AveragePowerWatts(null, null, currentJoules: 1_030d, currentSeconds: 102d),
                Is.Null);

            Assert.That(
                IgclCounterMath.AverageActivityPercent(null, null, currentBusySeconds: 10.5d, currentSeconds: 102d),
                Is.Null);
        });
    }

    /// <summary>A device that does not report one of the counters at all leaves it unsupported, hence null.</summary>
    [Test]
    public void UnsupportedCounter_ReportsUnknown()
    {
        Assert.That(
            IgclCounterMath.AveragePowerWatts(
                previousJoules: null, previousSeconds: 100d,
                currentJoules: null, currentSeconds: 102d),
            Is.Null);
    }
}
