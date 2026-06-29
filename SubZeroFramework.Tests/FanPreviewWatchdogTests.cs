using NUnit.Framework;

using SubZeroFramework.Models;
using SubZeroFramework.Service.Services;

namespace SubZeroFramework.Tests;

[TestFixture]
public class FanPreviewWatchdogTests
{
    [Test]
    public void TryTakeForRevert_AfterBegin_ReturnsCapturedSnapshot()
    {
        FanPreviewWatchdog watchdog = new();
        var snapshot = Snapshot(0, FanControlMode.Manual, lastDutyPercent: 42d);

        watchdog.Begin(0, snapshot);

        Assert.That(watchdog.TryTakeForRevert(0, out var taken), Is.True);
        Assert.That(taken, Is.SameAs(snapshot));
    }

    [Test]
    public void TryTakeForRevert_RemovesTheHold_SecondCallReturnsFalse()
    {
        FanPreviewWatchdog watchdog = new();
        watchdog.Begin(0, Snapshot(0, FanControlMode.Max));

        Assert.That(watchdog.TryTakeForRevert(0, out _), Is.True);
        Assert.That(watchdog.TryTakeForRevert(0, out _), Is.False, "Taking a hold must consume it so it cannot be reverted twice.");
    }

    [Test]
    public void Release_AfterBegin_PreventsRevert()
    {
        // The core no-double-revert invariant: a committed Apply releases the hold so the subsequent stream
        // close (TryTakeForRevert) must not revert.
        FanPreviewWatchdog watchdog = new();
        watchdog.Begin(0, Snapshot(0, FanControlMode.Manual));

        watchdog.Release(0);

        Assert.That(watchdog.TryTakeForRevert(0, out _), Is.False);
    }

    [Test]
    public void Begin_WhenAlreadyHeld_KeepsTheFirstSnapshot()
    {
        // The first hold captures the true pre-preview state; a later Begin (e.g. a re-preview) must not
        // overwrite it with an already-previewed state.
        FanPreviewWatchdog watchdog = new();
        var first = Snapshot(0, FanControlMode.Auto);
        var second = Snapshot(0, FanControlMode.Max);

        watchdog.Begin(0, first);
        watchdog.Begin(0, second);

        Assert.That(watchdog.TryTakeForRevert(0, out var taken), Is.True);
        Assert.That(taken, Is.SameAs(first));
    }

    [Test]
    public void TryTakeForRevert_OnUnknownFan_ReturnsFalse()
    {
        FanPreviewWatchdog watchdog = new();

        Assert.That(watchdog.TryTakeForRevert(7, out var taken), Is.False);
        Assert.That(taken, Is.Null);
    }

    [Test]
    public void Release_OnUnknownFan_IsNoOp()
    {
        FanPreviewWatchdog watchdog = new();

        Assert.DoesNotThrow(() => watchdog.Release(7));
        Assert.That(watchdog.TryTakeForRevert(7, out _), Is.False);
    }

    [Test]
    public void Holds_AreTrackedPerFanIndependently()
    {
        FanPreviewWatchdog watchdog = new();
        var fanZero = Snapshot(0, FanControlMode.Manual, lastDutyPercent: 30d);
        var fanOne = Snapshot(1, FanControlMode.Max);

        watchdog.Begin(0, fanZero);
        watchdog.Begin(1, fanOne);

        // Committing fan 0 must not disturb fan 1's still-open hold.
        watchdog.Release(0);

        Assert.Multiple(() =>
        {
            Assert.That(watchdog.TryTakeForRevert(0, out _), Is.False);
            Assert.That(watchdog.TryTakeForRevert(1, out var takenOne), Is.True);
            Assert.That(takenOne, Is.SameAs(fanOne));
        });
    }

    [Test]
    public void Begin_AfterTake_CanReopenAFreshHold()
    {
        // After a revert consumes the hold, a new preview on the same fan must be able to open a fresh hold.
        FanPreviewWatchdog watchdog = new();
        watchdog.Begin(0, Snapshot(0, FanControlMode.Auto));
        watchdog.TryTakeForRevert(0, out _);

        var reopened = Snapshot(0, FanControlMode.Manual, lastDutyPercent: 80d);
        watchdog.Begin(0, reopened);

        Assert.That(watchdog.TryTakeForRevert(0, out var taken), Is.True);
        Assert.That(taken, Is.SameAs(reopened));
    }

    [Test]
    public void Begin_WithNullSnapshot_Throws()
    {
        FanPreviewWatchdog watchdog = new();

        Assert.Throws<ArgumentNullException>(() => watchdog.Begin(0, null!));
    }

    private static FanControlStateSnapshot Snapshot(int fanIndex, FanControlMode mode, double? lastDutyPercent = null)
        => new()
        {
            FanIndex = fanIndex,
            DisplayName = $"Fan {fanIndex}",
            Mode = mode,
            LastDutyPercent = lastDutyPercent,
            ObservedAt = DateTimeOffset.UtcNow,
            IsAvailable = true,
        };
}
