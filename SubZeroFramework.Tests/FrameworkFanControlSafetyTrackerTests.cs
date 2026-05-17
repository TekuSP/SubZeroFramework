using NUnit.Framework;
using SubZeroFramework.Models;
using SubZeroFramework.Services;

namespace SubZeroFramework.Tests;

[TestFixture]
public class FrameworkFanControlSafetyTrackerTests
{
    [Test]
    public void BeginRestoreBatch_WhenOverridesAreActive_ReturnsEachFanOnceInOrder()
    {
        FrameworkFanControlSafetyTracker tracker = new();

        tracker.MarkOverrideActive(2);
        tracker.MarkOverrideActive(0);
        tracker.MarkOverrideActive(1);

        Assert.That(tracker.BeginRestoreBatch(), Is.EqualTo(new[] { 0, 1, 2 }));
        Assert.That(tracker.BeginRestoreBatch(), Is.Empty);
    }

    [Test]
    public void CompleteRestore_WhenRestoreFails_LeavesFanTrackedForRetry()
    {
        FrameworkFanControlSafetyTracker tracker = new();

        tracker.MarkOverrideActive(3);

        Assert.That(tracker.BeginRestoreBatch(), Is.EqualTo(new[] { 3 }));

        tracker.CompleteRestore(3, restored: false);

        Assert.That(tracker.HasActiveOverrides, Is.True);
        Assert.That(tracker.BeginRestoreBatch(), Is.EqualTo(new[] { 3 }));
    }

    [Test]
    public void CompleteRestore_WhenRestoreFails_TracksFailureDetails()
    {
        FrameworkFanControlSafetyTracker tracker = new();

        tracker.MarkOverrideActive(4);
        tracker.BeginRestoreBatch();
        tracker.CompleteRestore(4, restored: false, errorMessage: "restore failed");

        FanControlSafetyStateSnapshot state = tracker.GetState(4);
        Assert.That(state.HasActiveOverride, Is.True);
        Assert.That(state.LastAutoRestoreAttemptFailed, Is.True);
        Assert.That(state.LastAutoRestoreAttemptAt, Is.Not.Null);
        Assert.That(state.LastAutoRestoreError, Is.EqualTo("restore failed"));
    }

    [Test]
    public void MarkAutoRestored_WhenFanWasOverridden_ClearsActiveOverride()
    {
        FrameworkFanControlSafetyTracker tracker = new();

        tracker.MarkOverrideActive(1);
        tracker.MarkAutoRestored(1);

        Assert.That(tracker.HasActiveOverrides, Is.False);
        Assert.That(tracker.BeginRestoreBatch(), Is.Empty);
        Assert.That(tracker.GetState(1).LastAutoRestoreAttemptFailed, Is.False);
    }
}
