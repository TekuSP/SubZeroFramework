using NUnit.Framework;

using SubZeroFramework.Models;

namespace SubZeroFramework.Tests;

/// <summary>
/// Covers fixed-RATE pacing for the polling tiers: a tick every interval, not an interval between ticks.
/// </summary>
/// <remarks>
/// The failure this guards against is silent. Sleeping the full interval after each tick makes the real
/// period <c>interval + work</c>, so a tier drifts by however long its work took — and the adaptive
/// controller differentiates temperature against an assumed interval, so the drift lands directly in
/// <c>dT/dt</c>, worst when the machine is busiest.
/// </remarks>
[TestFixture]
public class PollingScheduleTests
{
    [Test]
    public void ComputeDelay_GivesBackOnlyTheUnusedRemainder()
    {
        // The reported case: a 1 s secondary tier where NVML stalled for 500 ms should sleep 500 ms, not 1 s.
        var delay = PollingSchedule.ComputeDelay(TimeSpan.FromSeconds(1), TimeSpan.FromMilliseconds(500));

        Assert.That(delay, Is.EqualTo(TimeSpan.FromMilliseconds(500)));
    }

    [Test]
    public void ComputeDelay_SleepsTheFullIntervalWhenWorkWasInstant()
    {
        var delay = PollingSchedule.ComputeDelay(TimeSpan.FromSeconds(1), TimeSpan.Zero);

        Assert.That(delay, Is.EqualTo(TimeSpan.FromSeconds(1)));
    }

    [Test]
    public void ComputeDelay_DoesNotSleepWhenTheWorkAteTheWholeInterval()
    {
        // Never negative: the next tick starts immediately, and the tier simply runs without idle time.
        Assert.Multiple(() =>
        {
            Assert.That(PollingSchedule.ComputeDelay(TimeSpan.FromMilliseconds(150), TimeSpan.FromMilliseconds(150)), Is.EqualTo(TimeSpan.Zero));
            Assert.That(PollingSchedule.ComputeDelay(TimeSpan.FromMilliseconds(150), TimeSpan.FromSeconds(2)), Is.EqualTo(TimeSpan.Zero));
        });
    }

    [Test]
    public void ComputeDelay_TreatsANonPositiveIntervalAsNoSleep()
    {
        Assert.Multiple(() =>
        {
            Assert.That(PollingSchedule.ComputeDelay(TimeSpan.Zero, TimeSpan.Zero), Is.EqualTo(TimeSpan.Zero));
            Assert.That(PollingSchedule.ComputeDelay(TimeSpan.FromSeconds(-1), TimeSpan.Zero), Is.EqualTo(TimeSpan.Zero));
        });
    }

    [Test]
    public void ComputeDelay_NeverExceedsTheInterval()
    {
        // A negative elapsed cannot come from a monotonic clock, but the answer must stay bounded regardless —
        // oversleeping is the very thing this exists to prevent.
        var delay = PollingSchedule.ComputeDelay(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(-5));

        Assert.That(delay, Is.EqualTo(TimeSpan.FromSeconds(1)));
    }

    [Test]
    public void ComputeDelay_KeepsTheAveragePeriodAtTheInterval()
    {
        // The property that matters, stated directly: work time plus sleep time equals one interval, whatever
        // the work cost. A fixed-delay loop fails this by exactly the work time on every tick.
        var interval = TimeSpan.FromSeconds(1);

        foreach (var workMilliseconds in new[] { 0, 1, 250, 500, 999 })
        {
            var work = TimeSpan.FromMilliseconds(workMilliseconds);
            Assert.That(work + PollingSchedule.ComputeDelay(interval, work), Is.EqualTo(interval), $"work of {workMilliseconds} ms");
        }
    }

    [Test]
    public void NextDeadline_AnchorsToTheScheduleRatherThanToWhenTheWorkFinished()
    {
        var start = new DateTimeOffset(2026, 8, 24, 12, 0, 0, TimeSpan.Zero);
        var interval = TimeSpan.FromSeconds(1);

        // The tier was due at 12:00:00 but the primary tick carrying it did not land until 200 ms late. The
        // next run is still due at 12:00:01, not at 12:00:01.200 — otherwise the lateness compounds forever.
        var next = PollingSchedule.NextDeadline(start, interval, start + TimeSpan.FromMilliseconds(200));

        Assert.That(next, Is.EqualTo(start + interval));
    }

    [Test]
    public void NextDeadline_DoesNotQueueUpRunsItMissed()
    {
        var start = new DateTimeOffset(2026, 8, 24, 12, 0, 0, TimeSpan.Zero);
        var interval = TimeSpan.FromSeconds(1);

        // Ten seconds went by in one stall. Advancing by a single interval would leave the deadline in the
        // past and fire ten times back-to-back to "catch up" — a burst of EC reads is a worse answer to being
        // late than being late.
        var next = PollingSchedule.NextDeadline(start, interval, start + TimeSpan.FromSeconds(10));

        Assert.That(next, Is.EqualTo(start + TimeSpan.FromSeconds(11)));
    }

    [Test]
    public void NextDeadline_FromAnUnsetDeadlineRunsImmediatelyThenSchedules()
    {
        var now = new DateTimeOffset(2026, 8, 24, 12, 0, 0, TimeSpan.Zero);

        // First ever tick: the field starts at MinValue so the gate opens at once, and the schedule then
        // anchors from now rather than from the year 1.
        var next = PollingSchedule.NextDeadline(DateTimeOffset.MinValue, TimeSpan.FromSeconds(1), now);

        Assert.That(next, Is.EqualTo(now + TimeSpan.FromSeconds(1)));
    }

    [Test]
    public void NextDeadline_HoldsCadenceAcrossManyTicks()
    {
        var start = new DateTimeOffset(2026, 8, 24, 12, 0, 0, TimeSpan.Zero);
        var interval = TimeSpan.FromSeconds(1);
        var deadline = start;

        // Each tick arrives a little late, as it would when gated by a coarser primary tick. The schedule must
        // not accumulate that lateness.
        for (var tick = 1; tick <= 10; tick++)
        {
            var observedAt = deadline + TimeSpan.FromMilliseconds(30);
            deadline = PollingSchedule.NextDeadline(deadline, interval, observedAt);
        }

        Assert.That(deadline, Is.EqualTo(start + TimeSpan.FromSeconds(10)), "Ten ticks must span ten seconds, not ten plus the accumulated lateness.");
    }
}
