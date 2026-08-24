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

        var token = watchdog.Begin(0, snapshot);

        Assert.That(watchdog.TryTakeForRevert(0, token, out var taken), Is.True);
        Assert.That(taken, Is.SameAs(snapshot));
    }

    [Test]
    public void TryTakeForRevert_RemovesTheHold_SecondCallReturnsFalse()
    {
        FanPreviewWatchdog watchdog = new();
        var token = watchdog.Begin(0, Snapshot(0, FanControlMode.Max));

        Assert.That(watchdog.TryTakeForRevert(0, token, out _), Is.True);
        Assert.That(watchdog.TryTakeForRevert(0, token, out _), Is.False, "Taking a hold must consume it so it cannot be reverted twice.");
    }

    [Test]
    public void Release_AfterBegin_PreventsRevert()
    {
        // The core no-double-revert invariant: a committed Apply releases the hold so the subsequent stream
        // close (TryTakeForRevert) must not revert.
        FanPreviewWatchdog watchdog = new();
        var token = watchdog.Begin(0, Snapshot(0, FanControlMode.Manual));

        watchdog.Release(0);

        Assert.That(watchdog.TryTakeForRevert(0, token, out _), Is.False);
    }

    [Test]
    public void Begin_WhenAlreadyHeld_KeepsTheFirstSnapshot()
    {
        // The first hold captures the true pre-preview state; a later Begin (e.g. a re-preview) must not
        // overwrite it with an already-previewed state.
        FanPreviewWatchdog watchdog = new();
        var first = Snapshot(0, FanControlMode.Auto);
        var second = Snapshot(0, FanControlMode.Max);

        var firstToken = watchdog.Begin(0, first);
        watchdog.Begin(0, second);

        Assert.That(watchdog.TryTakeForRevert(0, firstToken, out var taken), Is.True);
        Assert.That(taken, Is.SameAs(first));
    }

    [Test]
    public void Begin_WhenAlreadyHeld_ReturnsNoTokenForTheSecondCaller()
    {
        // Ownership, not just precedence. The second caller must be told it does not hold the fan, otherwise
        // its stream closing would revert a preview that belongs to the first caller.
        FanPreviewWatchdog watchdog = new();

        var firstToken = watchdog.Begin(0, Snapshot(0, FanControlMode.Auto));
        var secondToken = watchdog.Begin(0, Snapshot(0, FanControlMode.Max));

        Assert.Multiple(() =>
        {
            Assert.That(firstToken, Is.Not.Null);
            Assert.That(secondToken, Is.Null, "A second hold on the same fan must not claim ownership.");
        });
    }

    [Test]
    public void TryTakeForRevert_WithForeignToken_DoesNotStealTheHold()
    {
        // The defect this guards: two clients previewing the same fan. The second one disconnecting must not
        // revert the fan out from under the first, which is still previewing it.
        FanPreviewWatchdog watchdog = new();
        var owned = Snapshot(0, FanControlMode.Manual, lastDutyPercent: 30d);

        var ownerToken = watchdog.Begin(0, owned);
        var interloperToken = watchdog.Begin(0, Snapshot(0, FanControlMode.Max));

        Assert.Multiple(() =>
        {
            Assert.That(watchdog.TryTakeForRevert(0, interloperToken, out _), Is.False, "A non-owner must not be able to revert.");
            Assert.That(watchdog.TryTakeForRevert(0, Guid.NewGuid(), out _), Is.False, "An unrelated token must not be able to revert.");
            Assert.That(watchdog.HasOpenHold(0), Is.True, "The owner's hold must survive the interloper's close.");
        });

        // The real owner can still revert afterwards.
        Assert.That(watchdog.TryTakeForRevert(0, ownerToken, out var taken), Is.True);
        Assert.That(taken, Is.SameAs(owned));
    }

    [Test]
    public void TryTakeForRevert_OnUnknownFan_ReturnsFalse()
    {
        FanPreviewWatchdog watchdog = new();

        Assert.That(watchdog.TryTakeForRevert(7, Guid.NewGuid(), out var taken), Is.False);
        Assert.That(taken, Is.Null);
    }

    [Test]
    public void Release_OnUnknownFan_IsNoOp()
    {
        FanPreviewWatchdog watchdog = new();

        Assert.DoesNotThrow(() => watchdog.Release(7));
        Assert.That(watchdog.TryTakeForRevert(7, Guid.NewGuid(), out _), Is.False);
    }

    [Test]
    public void Holds_AreTrackedPerFanIndependently()
    {
        FanPreviewWatchdog watchdog = new();
        var fanZero = Snapshot(0, FanControlMode.Manual, lastDutyPercent: 30d);
        var fanOne = Snapshot(1, FanControlMode.Max);

        var zeroToken = watchdog.Begin(0, fanZero);
        var oneToken = watchdog.Begin(1, fanOne);

        // Committing fan 0 must not disturb fan 1's still-open hold.
        watchdog.Release(0);

        Assert.Multiple(() =>
        {
            Assert.That(watchdog.TryTakeForRevert(0, zeroToken, out _), Is.False);
            Assert.That(watchdog.TryTakeForRevert(1, oneToken, out var takenOne), Is.True);
            Assert.That(takenOne, Is.SameAs(fanOne));
        });
    }

    [Test]
    public void Begin_AfterTake_CanReopenAFreshHold()
    {
        // After a revert consumes the hold, a new preview on the same fan must be able to open a fresh hold.
        FanPreviewWatchdog watchdog = new();
        var firstToken = watchdog.Begin(0, Snapshot(0, FanControlMode.Auto));
        watchdog.TryTakeForRevert(0, firstToken, out _);

        var reopened = Snapshot(0, FanControlMode.Manual, lastDutyPercent: 80d);
        var reopenedToken = watchdog.Begin(0, reopened);

        Assert.Multiple(() =>
        {
            Assert.That(reopenedToken, Is.Not.Null, "A fan with no open hold must be claimable again.");
            Assert.That(reopenedToken, Is.Not.EqualTo(firstToken), "A fresh hold must not reuse the consumed token.");
        });

        Assert.That(watchdog.TryTakeForRevert(0, reopenedToken, out var taken), Is.True);
        Assert.That(taken, Is.SameAs(reopened));
    }

    [Test]
    public void TryTakeForRevert_WithStaleTokenFromAConsumedHold_DoesNotTakeTheNewHold()
    {
        // A stream that already reverted must not be able to revert a later, unrelated preview on the same fan.
        FanPreviewWatchdog watchdog = new();
        var staleToken = watchdog.Begin(0, Snapshot(0, FanControlMode.Auto));
        watchdog.TryTakeForRevert(0, staleToken, out _);

        watchdog.Begin(0, Snapshot(0, FanControlMode.Manual, lastDutyPercent: 55d));

        Assert.Multiple(() =>
        {
            Assert.That(watchdog.TryTakeForRevert(0, staleToken, out _), Is.False);
            Assert.That(watchdog.HasOpenHold(0), Is.True, "The new hold must survive a stale token's close.");
        });
    }

    [Test]
    public void Begin_WithNullSnapshot_Throws()
    {
        FanPreviewWatchdog watchdog = new();

        Assert.Throws<ArgumentNullException>(() => watchdog.Begin(0, null!));
    }

    [Test]
    public void TryTakeForRevert_WithNullToken_ReturnsFalse()
    {
        // A caller whose Begin returned null never owned the hold; passing that null through must be inert.
        FanPreviewWatchdog watchdog = new();
        watchdog.Begin(0, Snapshot(0, FanControlMode.Manual));

        Assert.Multiple(() =>
        {
            Assert.That(watchdog.TryTakeForRevert(0, null, out _), Is.False);
            Assert.That(watchdog.HasOpenHold(0), Is.True);
        });
    }

    [Test]
    public void HasOpenHold_TracksTheHoldLifecycle()
    {
        // Commands that persist a fan's live state (e.g. SetFanLink) check this to avoid
        // committing an uncommitted preview and disarming the revert watchdog.
        FanPreviewWatchdog watchdog = new();

        Assert.That(watchdog.HasOpenHold(0), Is.False);

        watchdog.Begin(0, Snapshot(0, FanControlMode.Auto));
        Assert.That(watchdog.HasOpenHold(0), Is.True);
        Assert.That(watchdog.HasOpenHold(1), Is.False, "A hold must be tracked per fan.");

        watchdog.Release(0);
        Assert.That(watchdog.HasOpenHold(0), Is.False);

        var token = watchdog.Begin(0, Snapshot(0, FanControlMode.Auto));
        watchdog.TryTakeForRevert(0, token, out _);
        Assert.That(watchdog.HasOpenHold(0), Is.False, "Taking the hold for revert must also close it.");
    }

    [Test]
    public void ReleaseAll_DropsEveryHoldSoNothingReverts()
    {
        // The factory reset's safety requirement: a hold left open would revert its fan to the captured
        // pre-preview state when the stream closes, resurrecting exactly the settings the reset just wiped.
        FanPreviewWatchdog watchdog = new();
        var zeroToken = watchdog.Begin(0, Snapshot(0, FanControlMode.Manual, lastDutyPercent: 30d));
        var twoToken = watchdog.Begin(2, Snapshot(2, FanControlMode.Max));

        var released = watchdog.ReleaseAll();

        Assert.Multiple(() =>
        {
            Assert.That(released, Is.EquivalentTo(new[] { 0, 2 }));
            Assert.That(watchdog.TryTakeForRevert(0, zeroToken, out _), Is.False);
            Assert.That(watchdog.TryTakeForRevert(2, twoToken, out _), Is.False);
            Assert.That(watchdog.HasOpenHold(0), Is.False);
            Assert.That(watchdog.HasOpenHold(2), Is.False);
        });
    }

    [Test]
    public void ReleaseAll_WithNoHolds_ReturnsEmpty()
    {
        FanPreviewWatchdog watchdog = new();

        Assert.That(watchdog.ReleaseAll(), Is.Empty);
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
