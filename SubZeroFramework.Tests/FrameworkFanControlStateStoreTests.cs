using DynamicData;

using Microsoft.Extensions.Logging.Abstractions;

using NUnit.Framework;

using SubZeroFramework.Models;
using SubZeroFramework.Service.Models;
using SubZeroFramework.Service.Services;
using SubZeroFramework.Services;

namespace SubZeroFramework.Tests;

[TestFixture]
public class FrameworkFanControlStateStoreTests
{
    [Test]
    public void GetState_ForUnknownFan_ReturnsNull()
    {
        using var store = CreateStore();

        Assert.That(store.GetState(0), Is.Null);
    }

    [Test]
    public void GetState_AfterMarkMax_ReturnsMaxSnapshot()
    {
        using var store = CreateStore();

        store.MarkMax(2);

        var state = store.GetState(2);
        Assert.That(state, Is.Not.Null);
        Assert.That(state!.FanIndex, Is.EqualTo(2));
        Assert.That(state.Mode, Is.EqualTo(FanControlMode.Max));
    }

    [Test]
    public void RestoreState_RepublishesTheSnapshotForReads()
    {
        using var store = CreateStore();

        // Start the fan on Max, then "revert" it back to a captured Manual pre-preview snapshot.
        store.MarkMax(1);
        var prePreview = new FanControlStateSnapshot
        {
            FanIndex = 1,
            DisplayName = "Fan 1",
            Mode = FanControlMode.Manual,
            LastDutyPercent = 35d,
            ObservedAt = DateTimeOffset.UtcNow,
            IsAvailable = true,
        };

        store.RestoreState(prePreview);

        var state = store.GetState(1);
        Assert.That(state, Is.Not.Null);
        Assert.That(state!.Mode, Is.EqualTo(FanControlMode.Manual));
        Assert.That(state.LastDutyPercent, Is.EqualTo(35d));
    }

    [Test]
    public void RestoreState_NormalizesProfilesToFiveSlots()
    {
        using var store = CreateStore();

        // A captured snapshot may carry a sparse profile array; republishing must normalize to five slots so
        // the curve worker and clients always see a well-formed state.
        var snapshot = new FanControlStateSnapshot
        {
            FanIndex = 3,
            DisplayName = "Fan 3",
            Mode = FanControlMode.Auto,
            CurveProfiles = [new FanCurveProfileSnapshot { Slot = 0, IsConfigured = false }],
            ObservedAt = DateTimeOffset.UtcNow,
            IsAvailable = true,
        };

        store.RestoreState(snapshot);

        var state = store.GetState(3);
        Assert.That(state, Is.Not.Null);
        Assert.That(state!.CurveProfiles, Has.Length.EqualTo(FrameworkFanControlStateStore.MaxCurveProfileSlots));
    }

    [Test]
    public void RestoreState_WithNull_Throws()
    {
        using var store = CreateStore();

        Assert.Throws<ArgumentNullException>(() => store.RestoreState(null!));
    }

    [Test]
    public void TelemetryTick_DoesNotClobberCommandedMode_WhenConfigDiffers()
    {
        // The persisted config seeds fan 1 as Auto.
        var provider = new StubFrameworkDataProvider();
        var options = new TestOptionsMonitor<FrameworkServiceOptions>(new FrameworkServiceOptions
        {
            FanControlStates = [new FanControlStateOptions { FanIndex = 1, Mode = FanControlMode.Auto }],
        });

        using var store = new FrameworkFanControlStateStore(
            provider,
            new FrameworkFanControlSafetyTracker(),
            options,
            NullLogger<FrameworkFanControlStateStore>.Instance);

        // Fan discovery seeds the fan from the persisted config (Auto).
        provider.FanStateSource.AddOrUpdate(NewFanState(1, DateTimeOffset.UtcNow));
        Assert.That(store.GetState(1)!.Mode, Is.EqualTo(FanControlMode.Auto));

        // A live command sets Max.
        store.MarkMax(1);
        Assert.That(store.GetState(1)!.Mode, Is.EqualTo(FanControlMode.Max));

        // A later telemetry tick must NOT re-apply the persisted Auto overlay over the commanded Max.
        // Regression: the overlay used to be applied on every tick, clobbering live commands (and the
        // clobbered Auto then got persisted, so an applied Max never survived a restart).
        provider.FanStateSource.AddOrUpdate(NewFanState(1, DateTimeOffset.UtcNow.AddSeconds(1)));

        Assert.That(store.GetState(1)!.Mode, Is.EqualTo(FanControlMode.Max));
    }

    [Test]
    public void SetCpuUsageModifier_ForUnknownFan_ReturnsFalse()
    {
        using var store = CreateStore();

        Assert.That(store.SetCpuUsageModifier(0, 25d), Is.False);
    }

    [Test]
    public void SetCpuUsageModifier_RoundTripsThroughStateAndOptions()
    {
        using var store = CreateStore();

        store.MarkMax(1);
        Assert.That(store.SetCpuUsageModifier(1, 25d), Is.True);

        Assert.Multiple(() =>
        {
            Assert.That(store.GetState(1)!.CpuUsageModifierStrength, Is.EqualTo(25d));
            Assert.That(store.BuildFanControlOptions(1)!.CpuUsageModifierStrength, Is.EqualTo(25d));
        });
    }

    [Test]
    public void SetCpuUsageModifier_WithNullOrNaN_ClearsTheModifier()
    {
        using var store = CreateStore();

        store.MarkMax(1);
        store.SetCpuUsageModifier(1, 25d);

        store.SetCpuUsageModifier(1, double.NaN);
        Assert.That(store.GetState(1)!.CpuUsageModifierStrength, Is.Null);

        store.SetCpuUsageModifier(1, 25d);
        store.SetCpuUsageModifier(1, null);
        Assert.That(store.GetState(1)!.CpuUsageModifierStrength, Is.Null);
    }

    [Test]
    public void SetCpuUsageModifier_ClampsIntoDutyRange()
    {
        using var store = CreateStore();

        store.MarkMax(1);
        store.SetCpuUsageModifier(1, 250d);

        Assert.That(store.GetState(1)!.CpuUsageModifierStrength, Is.EqualTo(100d));
    }

    [Test]
    public void SetCpuUsageModifier_SurvivesModeSwitches()
    {
        using var store = CreateStore();

        // The modifier is per-fan: switching Auto -> Max -> Auto must not drop it, so re-activating a
        // curve later still boosts on CPU load without the user re-entering the strength.
        store.MarkAuto(1);
        store.SetCpuUsageModifier(1, 30d);

        store.MarkMax(1);
        store.MarkAuto(1);

        Assert.That(store.GetState(1)!.CpuUsageModifierStrength, Is.EqualTo(30d));
    }

    [Test]
    public void ConfiguredState_SeedsTheCpuUsageModifier()
    {
        var provider = new StubFrameworkDataProvider();
        var options = new TestOptionsMonitor<FrameworkServiceOptions>(new FrameworkServiceOptions
        {
            FanControlStates = [new FanControlStateOptions { FanIndex = 1, Mode = FanControlMode.Auto, CpuUsageModifierStrength = 42d }],
        });

        using var store = new FrameworkFanControlStateStore(
            provider,
            new FrameworkFanControlSafetyTracker(),
            options,
            NullLogger<FrameworkFanControlStateStore>.Instance);

        provider.FanStateSource.AddOrUpdate(NewFanState(1, DateTimeOffset.UtcNow));

        Assert.That(store.GetState(1)!.CpuUsageModifierStrength, Is.EqualTo(42d));
    }

    private static FanStateSnapshot NewFanState(int fanIndex, DateTimeOffset observedAt) => new()
    {
        FanIndex = fanIndex,
        DisplayName = $"Fan {fanIndex}",
        FanState = default,
        ObservedAt = observedAt,
        IsAvailable = true,
    };

    private static FrameworkFanControlStateStore CreateStore()
        => new(
            new StubFrameworkDataProvider(),
            new FrameworkFanControlSafetyTracker(),
            new TestOptionsMonitor<FrameworkServiceOptions>(new FrameworkServiceOptions()),
            NullLogger<FrameworkFanControlStateStore>.Instance);
}
