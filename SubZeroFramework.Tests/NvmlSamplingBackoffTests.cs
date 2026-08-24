using NUnit.Framework;

using SubZeroFramework.Services.Compute;

namespace SubZeroFramework.Tests;

/// <summary>
/// Covers the rule that stops the service pinning an idle discrete GPU awake.
/// </summary>
/// <remarks>
/// Measured on a Framework 16 with an RTX 5070: an NVML call to an awake GPU returns in 0.02 ms, while one
/// that has to wake a sleeping GPU takes 480-600 ms and the board jumps ~17.9 W to ~29 W. Sampling every
/// second regardless keeps the GPU awake for as long as the service runs, burning roughly 19 W for telemetry
/// that nobody may be reading.
/// </remarks>
[TestFixture]
public class NvmlSamplingBackoffTests
{
    private static readonly TimeSpan AwakeCall = TimeSpan.FromMilliseconds(0.02d);
    private static readonly TimeSpan WakingCall = TimeSpan.FromMilliseconds(550d);

    [Test]
    public void GetMinimumInterval_DoesNotDelayAnAwakeGpu()
    {
        // Nothing to protect: the GPU is already running, so the tier's own interval paces sampling.
        Assert.That(NvmlSamplingBackoff.GetMinimumInterval(AwakeCall), Is.EqualTo(TimeSpan.Zero));
    }

    [Test]
    public void GetMinimumInterval_BacksOffAfterACallThatWokeTheGpu()
    {
        Assert.That(NvmlSamplingBackoff.GetMinimumInterval(WakingCall), Is.EqualTo(NvmlSamplingBackoff.SleepingInterval));
    }

    [Test]
    public void ShouldSample_LetsAnAwakeGpuSampleEveryTick()
    {
        // A 1 s secondary tier must not be slowed down when the GPU is busy — that is when the readings matter.
        Assert.That(NvmlSamplingBackoff.ShouldSample(TimeSpan.FromSeconds(1), AwakeCall), Is.True);
    }

    [Test]
    public void ShouldSample_LeavesASleepingGpuAloneBetweenBackoffs()
    {
        Assert.Multiple(() =>
        {
            Assert.That(NvmlSamplingBackoff.ShouldSample(TimeSpan.FromSeconds(1), WakingCall), Is.False, "one second after waking it");
            Assert.That(NvmlSamplingBackoff.ShouldSample(TimeSpan.FromSeconds(30), WakingCall), Is.False, "halfway through the backoff");
            Assert.That(NvmlSamplingBackoff.ShouldSample(NvmlSamplingBackoff.SleepingInterval, WakingCall), Is.True, "once the backoff has elapsed");
        });
    }

    [Test]
    public void ShouldSample_RecoversImmediatelyOnceTheGpuIsBusyAgain()
    {
        // The point of keying on call duration: when a game starts, calls go fast again and full-rate
        // sampling resumes on the very next tick rather than waiting out a backoff.
        Assert.That(NvmlSamplingBackoff.ShouldSample(TimeSpan.FromMilliseconds(250), AwakeCall), Is.True);
    }

    [Test]
    public void WakeCostThreshold_SitsBetweenTheTwoMeasuredCases()
    {
        // An order of magnitude clear of each, so neither jitter on an awake call nor a merely slow-but-awake
        // call is mistaken for a wake.
        Assert.Multiple(() =>
        {
            Assert.That(NvmlSamplingBackoff.WakeCostThreshold, Is.GreaterThan(AwakeCall * 100));
            Assert.That(NvmlSamplingBackoff.WakeCostThreshold, Is.LessThan(WakingCall / 5));
        });
    }
}
