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

    /// <summary>
    /// An applied Adaptive survives a service restart: the persisted options carry the driving sensors the
    /// loop holds, and a store seeded from them restores the mode WITH those sensors.
    /// </summary>
    /// <remarks>
    /// Regression: the options carried the mode but never the top-level driving sensors, so a restart
    /// restored "Adaptive with zero sensors" — a state the worker cannot drive. The user applied Adaptive,
    /// restarted, and found the fan behaving as Auto.
    /// </remarks>
    [Test]
    public void BuildFanControlOptions_ThenSeedANewStore_KeepsAdaptiveAndItsSensors()
    {
        var provider = new StubFrameworkDataProvider();
        using var store = new FrameworkFanControlStateStore(
            provider,
            new FrameworkFanControlSafetyTracker(),
            new TestOptionsMonitor<FrameworkServiceOptions>(new FrameworkServiceOptions()),
            NullLogger<FrameworkFanControlStateStore>.Instance);

        provider.FanStateSource.AddOrUpdate(NewFanState(1, DateTimeOffset.UtcNow));
        store.SetCalibration(1, MeasuredCalibration());

        var armed = store.SetAdaptiveMode(1, [2, 3], TemperatureAggregationMode.Maximum, new AdaptiveFanSettings
        {
            TargetTemperatureCelsius = 78d,
            SafetyFloorEnabled = true,
            SafetyFloorPercent = 30d,
            LambdaSeconds = 12d, // inside λ's [2, 16] valid range, away from the default 8 so a reset is visible
        });
        Assert.That(armed.Succeeded, Is.True, armed.Message);

        var options = store.BuildFanControlOptions(1);
        Assert.That(options, Is.Not.Null);
        Assert.That(options!.Mode, Is.EqualTo(FanControlMode.Adaptive));
        Assert.That(options.DrivingSensorIndices, Is.EquivalentTo(new[] { 2, 3 }), "the sensors never reached the persisted options");

        // "Restart": a fresh store seeded from exactly what the first one persisted.
        var restartedProvider = new StubFrameworkDataProvider();
        using var restartedStore = new FrameworkFanControlStateStore(
            restartedProvider,
            new FrameworkFanControlSafetyTracker(),
            new TestOptionsMonitor<FrameworkServiceOptions>(new FrameworkServiceOptions { FanControlStates = [options] }),
            NullLogger<FrameworkFanControlStateStore>.Instance);
        restartedProvider.FanStateSource.AddOrUpdate(NewFanState(1, DateTimeOffset.UtcNow));

        var restored = restartedStore.GetState(1);
        Assert.That(restored, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(restored!.Mode, Is.EqualTo(FanControlMode.Adaptive), "the applied mode did not survive the restart");
            Assert.That(restored.DrivingSensorIndices, Is.EquivalentTo(new[] { 2, 3 }), "Adaptive came back without the sensors the loop holds — the worker cannot drive that");
            Assert.That(restored.AdaptiveSettings.TargetTemperatureCelsius, Is.EqualTo(78d), "the target temperature was lost");
            Assert.That(restored.AdaptiveSettings.SafetyFloorEnabled, Is.True, "the safety floor toggle was lost");
            Assert.That(restored.AdaptiveSettings.SafetyFloorPercent, Is.EqualTo(30d), "the safety floor level was lost");
            Assert.That(restored.AdaptiveSettings.LambdaSeconds, Is.EqualTo(12d), "λ (the Quick↔Calm response pace) was lost");
        });
    }

    /// <summary>
    /// A config persisted before the sensors were saved restores as Auto, not as an undrivable Adaptive.
    /// </summary>
    [Test]
    public void SeedingAdaptiveWithoutSensors_RestoresAuto()
    {
        var provider = new StubFrameworkDataProvider();
        using var store = new FrameworkFanControlStateStore(
            provider,
            new FrameworkFanControlSafetyTracker(),
            new TestOptionsMonitor<FrameworkServiceOptions>(new FrameworkServiceOptions
            {
                FanControlStates = [new FanControlStateOptions { FanIndex = 1, Mode = FanControlMode.Adaptive }],
            }),
            NullLogger<FrameworkFanControlStateStore>.Instance);
        provider.FanStateSource.AddOrUpdate(NewFanState(1, DateTimeOffset.UtcNow));

        Assert.That(store.GetState(1)!.Mode, Is.EqualTo(FanControlMode.Auto), "an Adaptive nothing can drive should restore as honest Auto");
    }

    private static FanCalibrationSnapshot MeasuredCalibration() => new()
    {
        State = FanCalibrationState.Ok,
        CalibratedAt = DateTimeOffset.UtcNow,
        ProcessGainCelsiusPerPercent = 0.42d,
        TimeConstantSeconds = 26d,
        DeadTimeSeconds = 4d,
        MinimumSpinRpm = 1_180d,
        MinimumSpinDutyPercent = 17d,
        MaximumRpm = 7_000d,
        FeedForwardDutyPerWatt = 0.9d,
        TrackingMode = FanSpeedTrackingMode.Duty,
    };

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
