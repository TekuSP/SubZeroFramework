using DynamicData;

using FrameworkDotnet.Enums;

using Microsoft.Extensions.Logging.Abstractions;

using NUnit.Framework;

using SubZeroFramework.Models;
using SubZeroFramework.Service.Models;
using SubZeroFramework.Service.Services;
using SubZeroFramework.Services;
using SubZeroFramework.Services.Control;

namespace SubZeroFramework.Tests;

/// <summary>
/// Cover for the hot test — the one operation that deliberately heats the machine and drives a fan to
/// extremes.
/// </summary>
/// <remarks>
/// Two things are being protected here. The first is the model: a run that produces a wrong K silently
/// mis-tunes every subsequent second of fan control. The second, and more important, is the promise that the
/// fan always comes back: whatever goes wrong — cancellation, a safety abort, a client that vanishes, a
/// progress callback that throws — the load must stop and the fan must be handed back to automatic control.
/// Every failure path below asserts that, not just the interesting ones.
/// </remarks>
[TestFixture]
public class FanCalibrationRunnerTests
{
    private const int FanIndex = 0;
    private static readonly int[] DrivingSensors = [0];

    /// <summary>
    /// The same sequence as production, compressed so a suite can run it.
    /// </summary>
    /// <remarks>
    /// Scaled against the simulated plant's 80 ms time constant exactly as the production values are scaled
    /// against a real chassis's — the response window spans several time constants so the fit reads a settled
    /// asymptote, not a temperature still falling.
    /// </remarks>
    private static FanCalibrationTimings FastTimings => new()
    {
        SampleInterval = TimeSpan.FromMilliseconds(5),
        IdleSettle = TimeSpan.FromMilliseconds(20),
        // Several plant ticks, mirroring the production requirement that a dwell outlast the telemetry
        // interval — a dwell of the same order reads the tachometer before the new duty has taken effect.
        MinimumSpinDwell = TimeSpan.FromMilliseconds(80),
        MinimumLoad = TimeSpan.FromMilliseconds(100),
        SettleWindow = TimeSpan.FromMilliseconds(40),
        LoadSettleTimeout = TimeSpan.FromMilliseconds(800),
        Response = TimeSpan.FromMilliseconds(600),
        TrackingSettle = TimeSpan.FromMilliseconds(60),

        // Two of the plant's 80 ms time constants per sweep level, mirroring what production derives from a
        // real chassis — enough transient for the asymptote to be extrapolatable.
        GainCurveDwell = TimeSpan.FromMilliseconds(160),

        // A few plant time constants, mirroring how production's cooldown spans a chassis's.
        CooldownTimeout = TimeSpan.FromMilliseconds(400),

        // Two sample intervals, mirroring production's five: long enough that a single tick cannot trip a
        // retry, short enough that a sustained excursion still trips before reaching the ceiling.
        CeilingRetryPersistence = TimeSpan.FromMilliseconds(10),
    };

    [Test]
    public async Task RunAsync_PinsEveryOtherFan_AndHandsThemBackAfterwards()
    {
        using var plant = new SimulatedThermalPlant();
        using var harness = new Harness(plant);

        // A second fan on the machine, exactly as discovery would report it. On the real chassis it shares
        // the heatpipe with the fan under test, and left under firmware control it regulates AGAINST the
        // measurement — spinning up as the load heats the assembly and down as the step cools it. A real run
        // measured a 2 °C swing that way, a quarter of what the fit needs.
        const int siblingIndex = FanIndex + 1;
        plant.FanStateSource.AddOrUpdate(new FanStateSnapshot
        {
            FanIndex = siblingIndex,
            DisplayName = "Sibling",
            CoolingRole = FanCoolingRole.Cpu,
            FanState = default,
            ObservedAt = DateTimeOffset.UtcNow,
            IsAvailable = true,
        });

        var result = await harness.RunAsync();

        Assert.Multiple(() =>
        {
            Assert.That(result.Succeeded, Is.True, "pinning the sibling must not break the run itself");
            Assert.That(
                plant.SetFanDutyCalls,
                Does.Contain((siblingIndex, FanCalibrationRunner.SiblingHoldDutyPercent)),
                "the sibling was never pinned, so its firmware loop was free to cancel the step response");
            Assert.That(
                plant.RestoreAutoCalls,
                Does.Contain(siblingIndex),
                "the sibling was left overridden after the run");
        });
    }

    /// <summary>
    /// A machine whose loaded hold rides the retry margin gets retried with more sibling airflow — and even
    /// when that never helps, the run converges and completes rather than aborting or looping.
    /// </summary>
    /// <remarks>
    /// The plant deliberately ignores sibling cooling entirely, which is the WORST case for the retry loop:
    /// every attempt trips the margin again, the sibling hold must escalate all the way to full, and the
    /// final attempt — trigger disarmed for want of headroom — has to run on into the margin and finish
    /// there. A real chassis, where sibling airflow actually cools, converges in fewer passes.
    /// </remarks>
    [Test]
    public async Task RunAsync_RetriesWithMoreSiblingAirflow_WhenTheHoldRidesTheCeilingMargin()
    {
        // Hold asymptote = ambient 40 + rise − gain×22 ≈ 93.5 °C: sustained past the 92 °C retry line long
        // enough to outlast the persistence, below the 95 °C abort. The warm phase at full fan is unaffected.
        using var plant = new SimulatedThermalPlant { LoadRiseCelsius = 62.7d };
        using var harness = new Harness(plant);

        const int siblingIndex = FanIndex + 1;
        plant.FanStateSource.AddOrUpdate(new FanStateSnapshot
        {
            FanIndex = siblingIndex,
            DisplayName = "Sibling",
            CoolingRole = FanCoolingRole.Cpu,
            FanState = default,
            ObservedAt = DateTimeOffset.UtcNow,
            IsAvailable = true,
        });

        var result = await harness.RunAsync();

        Assert.Multiple(() =>
        {
            Assert.That(result.Succeeded, Is.True, "with the trigger disarmed at full sibling duty, the final attempt must complete inside the margin");
            Assert.That(
                plant.SetFanDutyCalls,
                Does.Contain((siblingIndex, FanCalibrationRunner.SiblingSpinFloorDutyPercent)),
                "the first retry never raised the sibling hold");
            Assert.That(
                plant.SetFanDutyCalls,
                Does.Contain((siblingIndex, FanCalibrationRunner.MaximumSiblingHoldDutyPercent)),
                "the escalation never reached full duty, so the trigger could not have disarmed");
            Assert.That(
                plant.SetFanDutyCalls.Count(call => call == (siblingIndex, 100d)),
                Is.GreaterThanOrEqualTo(2),
                "cooldowns run EVERY fan at full — the sibling saw full duty only once, which is the final escalated pin, not a cooldown");
            Assert.That(
                plant.RestoreAutoCalls,
                Does.Contain(siblingIndex),
                "the sibling was never handed back at the end");
        });
    }

    /// <summary>
    /// The minimum-spin walk is inside the retry's protection, not in front of it.
    /// </summary>
    /// <remarks>
    /// The regression this pins: the retry loop originally wrapped only the measurement, and the walk — a
    /// minute of every fan held near-dead — ran before it, unprotected. On a machine that is never as idle
    /// as "idle" suggests (the app's charts, the IDE, the service), that was enough to cook to the ceiling
    /// ninety seconds into a real run, before the retry machinery was listening. The idle heat here is
    /// strong enough that the walk can never finish — climbing through the retry margin during the walk's
    /// first dwells on every attempt — so the proof is the ESCALATION: the siblings must be raised from
    /// within the walk, all the way to full, before the honest abort ends it.
    /// </remarks>
    [Test]
    public async Task RunAsync_RetriesFromTheMinimumSpinWalk_WhenIdleHeatRidesTheMargin()
    {
        // The idle asymptote (ambient 40 + 70) is far past the ceiling, but the plant's time constant keeps
        // the SAMPLED temperature under 95 through the short idle settle; it crosses 90 during the walk.
        using var plant = new SimulatedThermalPlant { IdleRiseCelsius = 70d };
        using var harness = new Harness(plant);

        const int siblingIndex = FanIndex + 1;
        plant.FanStateSource.AddOrUpdate(new FanStateSnapshot
        {
            FanIndex = siblingIndex,
            DisplayName = "Sibling",
            CoolingRole = FanCoolingRole.Cpu,
            FanState = default,
            ObservedAt = DateTimeOffset.UtcNow,
            IsAvailable = true,
        });

        var result = await harness.RunAsync();

        Assert.Multiple(() =>
        {
            Assert.That(result.Succeeded, Is.False, "an unsurvivably hot machine must still end in failure");
            Assert.That(result.Failure, Is.EqualTo(FanCalibrationFailure.TemperatureCeiling));
            Assert.That(
                plant.SetFanDutyCalls,
                Does.Contain((siblingIndex, FanCalibrationRunner.SiblingSpinFloorDutyPercent)),
                "the walk crossed the margin but no retry raised the siblings — the walk is outside the armed region again");
            Assert.That(
                plant.SetFanDutyCalls,
                Does.Contain((siblingIndex, FanCalibrationRunner.MaximumSiblingHoldDutyPercent)),
                "escalation stopped early instead of running to full before the abort");
        });

        harness.AssertMachineWasHandedBack();
    }

    [Test]
    public async Task RunAsync_RecoversThePlantAndStoresTheCalibration()
    {
        using var plant = new SimulatedThermalPlant();
        using var harness = new Harness(plant);

        var result = await harness.RunAsync();

        Assert.Multiple(() =>
        {
            Assert.That(result.Succeeded, Is.True, "the run should have produced a model");
            Assert.That(result.Failure, Is.EqualTo(FanCalibrationFailure.None));
            Assert.That(result.StoppedAt, Is.EqualTo(FanCalibrationStep.Completed));

            // The number the whole run exists to produce. Held to a tight tolerance on purpose: the band is
            // narrow enough that a systematic bias — a sample filed under the wrong timestamp, a response
            // window that closes early — shows up here rather than hiding inside a generous margin.
            Assert.That(
                result.Calibration!.ProcessGainCelsiusPerPercent,
                Is.EqualTo(SimulatedThermalPlant.ProcessGainCelsiusPerPercent).Within(0.03d));

            // Stored, not merely returned — the controller reads it from the store.
            Assert.That(harness.Store.GetState(FanIndex)!.Calibration.State, Is.EqualTo(FanCalibrationState.Ok));
        });
    }

    [Test]
    public async Task RunAsync_RecordsWhatTheExtraFanBoughtInSustainedSpeed()
    {
        using var plant = new SimulatedThermalPlant();
        using var harness = new Harness(plant);

        var result = await harness.RunAsync();

        var performance = result.Calibration!.PerformanceResponse;

        Assert.Multiple(() =>
        {
            Assert.That(performance.HasMeasurement, Is.True, "no speed comparison was recorded");

            // The low-duty figure must come from the THROTTLED step and the full-duty one from after the fan
            // was stepped. Filing them under the wrong operating point would collapse the difference to zero.
            Assert.That(
                performance.CpuPerformanceRatioAtLowDuty,
                Is.EqualTo(plant.CpuPerformanceRatioWhenHot).Within(0.02d));
            Assert.That(
                performance.CpuPerformanceRatioAtFullDuty,
                Is.EqualTo(plant.CpuPerformanceRatioWhenCool).Within(0.02d));

            // 0.72 → 0.98 is a touch over a third more sustained clock.
            Assert.That(performance.SustainedSpeedGainFraction, Is.EqualTo(0.361d).Within(0.05d));
        });
    }

    [Test]
    public async Task RunAsync_RecordsNoSpeedComparison_WhenTheMachineReportsNoClock()
    {
        // A machine reporting no clock must yield "not measured", never a fabricated pair of zeroes that the
        // UI would render as a fan which stops the processor dead.
        using var plant = new SimulatedThermalPlant { ReportsClock = false };
        using var harness = new Harness(plant);

        var result = await harness.RunAsync();

        Assert.Multiple(() =>
        {
            Assert.That(result.Succeeded, Is.True, "a missing clock reading must not fail the calibration");
            Assert.That(result.Calibration!.PerformanceResponse.HasMeasurement, Is.False);
            Assert.That(result.Calibration.PerformanceResponse.SustainedSpeedGainFraction, Is.Null);
        });
    }

    [Test]
    public async Task RunAsync_MeasuresCoolingAcrossTheDutyRange()
    {
        using var plant = new SimulatedThermalPlant();
        using var harness = new Harness(plant);

        var result = await harness.RunAsync();

        var curve = result.Calibration!.GainCurve;

        Assert.Multiple(() =>
        {
            // The two ends the run already settles at, plus the three it sweeps.
            Assert.That(curve.IsUsable, Is.True, "no gain curve was measured");
            Assert.That(curve.Points, Has.Length.EqualTo(5));

            // Ordered by duty, and hotter at lower duty — a curve that came out the other way round would
            // schedule the controller backwards.
            Assert.That(curve.Points.Select(static point => point.DutyPercent), Is.Ordered);
            Assert.That(curve.Points[0].SettledCelsius, Is.GreaterThan(curve.Points[^1].SettledCelsius));
        });
    }

    [Test]
    public async Task RunAsync_ReportsProgressThatOnlyMovesForward()
    {
        using var plant = new SimulatedThermalPlant();
        using var harness = new Harness(plant);

        await harness.RunAsync();

        var progress = harness.Progress.Select(static update => update.OverallProgress).ToList();

        Assert.Multiple(() =>
        {
            Assert.That(progress, Is.Not.Empty);

            // A bar that goes backwards reads as a run that has gone wrong, whatever the underlying estimate
            // is doing. Clamping each step to its own share is what guarantees this.
            Assert.That(progress, Is.Ordered, "progress went backwards");

            Assert.That(progress[0], Is.LessThan(0.5d), "the run claimed to start half done");
            Assert.That(progress[^1], Is.GreaterThan(0.5d), "progress never got past halfway");

            // And it must actually be useful for a bar — not pinned at one value the whole way.
            Assert.That(progress.Distinct().Count(), Is.GreaterThan(5));
        });
    }

    [Test]
    public async Task RunAsync_ReportsATimeRemaining()
    {
        using var plant = new SimulatedThermalPlant();
        using var harness = new Harness(plant);

        await harness.RunAsync();

        var remaining = harness.Progress
            .Select(static update => update.EstimatedRemaining)
            .OfType<TimeSpan>()
            .ToList();

        Assert.Multiple(() =>
        {
            Assert.That(remaining, Is.Not.Empty, "no time estimate was ever reported");
            Assert.That(remaining[^1], Is.LessThan(remaining[0]), "the estimate never came down");
        });
    }

    [Test]
    public async Task RunAsync_DerivesGainsFromTheFittedModel()
    {
        using var plant = new SimulatedThermalPlant();
        using var harness = new Harness(plant);

        var result = await harness.RunAsync();

        // Gains are carried for display, but a stored zero would show the user a controller that does nothing.
        Assert.Multiple(() =>
        {
            Assert.That(result.Calibration!.ProportionalGain, Is.GreaterThan(0d));
            Assert.That(result.Calibration.IntegralGain, Is.GreaterThan(0d));
        });
    }

    [Test]
    public async Task RunAsync_FindsTheDutyAtWhichTheFanStopsTurning()
    {
        using var plant = new SimulatedThermalPlant { StallDutyPercent = 22d };
        using var harness = new Harness(plant);

        var result = await harness.RunAsync();

        // The walk steps in fives, so the answer is the first multiple of five at or above the stall point.
        Assert.That(result.Calibration!.MinimumSpinDutyPercent, Is.EqualTo(25d));
    }

    [Test]
    public async Task RunAsync_FallsBackToABootstrapFloor_WhenTheTachometerNeverReads()
    {
        // Every duty reads as stalled, which is what a fan with no working tachometer looks like.
        using var plant = new SimulatedThermalPlant { StallDutyPercent = 200d };
        using var harness = new Harness(plant);

        var result = await harness.RunAsync();

        // This value becomes the controller's duty FLOOR. Taking "never turned" literally would pin the fan
        // at full speed forever — an unreadable sensor turned into a permanently loud machine.
        Assert.That(
            result.Calibration!.MinimumSpinDutyPercent,
            Is.EqualTo(FanCalibrationSnapshot.Bootstrap.MinimumSpinDutyPercent));
    }

    [Test]
    public async Task RunAsync_ReportsCascade_WhenTheFanHoldsACommandedSpeed()
    {
        using var plant = new SimulatedThermalPlant { HonoursSpeedCommands = true };
        using var harness = new Harness(plant);

        var result = await harness.RunAsync();

        Assert.That(result.Calibration!.TrackingMode, Is.EqualTo(FanSpeedTrackingMode.Cascade));
    }

    [Test]
    public async Task RunAsync_FallsBackToDuty_WhenTheFanIgnoresSpeedCommands()
    {
        using var plant = new SimulatedThermalPlant { HonoursSpeedCommands = false };
        using var harness = new Harness(plant);

        var result = await harness.RunAsync();

        // Commanding RPM to a fan that ignores it would leave the controller open-loop without ever saying so.
        Assert.Multiple(() =>
        {
            Assert.That(result.Succeeded, Is.True);
            Assert.That(result.Calibration!.TrackingMode, Is.EqualTo(FanSpeedTrackingMode.Duty));
        });
    }

    [Test]
    public async Task RunAsync_FailsWhenTheMachineNeverGetsBusy()
    {
        using var plant = new SimulatedThermalPlant { LoadedWatts = 8d, LoadRiseCelsius = 6d };
        using var harness = new Harness(plant);

        var result = await harness.RunAsync();

        Assert.Multiple(() =>
        {
            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.Failure, Is.EqualTo(FanCalibrationFailure.InsufficientLoad));

            // The failure has to say how busy the machine actually got, or the user has nothing to act on.
            Assert.That(result.AveragePackagePowerWatts, Is.EqualTo(8d).Within(0.5d));

            // And no model may be stored from a run that failed.
            Assert.That(harness.Store.GetState(FanIndex)!.Calibration.State, Is.Not.EqualTo(FanCalibrationState.Ok));
        });
    }

    [Test]
    public async Task RunAsync_LoadsTheGpu_ForAGpuCooledFan()
    {
        // A Framework 16 right fan: it cools the discrete GPU, and only GPU work heats what it controls.
        using var plant = new SimulatedThermalPlant
        {
            DrivingSensorName = FrameworkSensorName.DgpuTemp,
            HeatedBy = ThermalLoadTarget.Gpu,
        };

        using var harness = new Harness(plant, FanCoolingRole.Gpu);

        var result = await harness.RunAsync();

        Assert.Multiple(() =>
        {
            Assert.That(result.Succeeded, Is.True, "the GPU fan could not be calibrated");

            // And the CPU was never loaded: the two share a power budget and a chassis, so running both
            // would have each measuring the other rather than the fan.
            Assert.That(plant.CpuLoadWasStarted, Is.False, "CPU load ran during a GPU calibration");
        });
    }

    [Test]
    public async Task RunAsync_NeverRunsCpuAndGpuLoadTogether()
    {
        // The CPU fan's run must leave the GPU alone for the same reason, in the other direction.
        using var plant = new SimulatedThermalPlant();
        using var harness = new Harness(plant, FanCoolingRole.Cpu);

        var result = await harness.RunAsync();

        Assert.Multiple(() =>
        {
            Assert.That(result.Succeeded, Is.True);
            Assert.That(plant.GpuLoadWasStarted, Is.False, "GPU load ran during a CPU calibration");
        });
    }

    [Test]
    public async Task RunAsync_RefusesAGpuFan_WhenTheMachineHasNoUsableAccelerator()
    {
        using var plant = new SimulatedThermalPlant
        {
            DrivingSensorName = FrameworkSensorName.DgpuTemp,
            HeatedBy = ThermalLoadTarget.Gpu,
            GpuLoadAvailable = false,
        };

        using var harness = new Harness(plant, FanCoolingRole.Gpu);

        var result = await harness.RunAsync();

        Assert.Multiple(() =>
        {
            Assert.That(result.Failure, Is.EqualTo(FanCalibrationFailure.GpuLoadUnavailable));

            // Substituting CPU load would heat something this fan does not cool, and the run would fit a
            // confident model of the wrong component rather than admitting it cannot do the job.
            Assert.That(plant.CpuLoadWasStarted, Is.False, "CPU load was substituted for unavailable GPU load");
        });

        harness.AssertMachineWasHandedBack();
    }

    [Test]
    public async Task RunAsync_RefusesToRunOnBattery()
    {
        using var plant = new SimulatedThermalPlant
        {
            PowerSourceState = FrameworkPowerSourceState.BatteryOnly,
            BatteryState = FrameworkBatteryState.Discharging,
        };

        using var harness = new Harness(plant);

        var result = await harness.RunAsync();

        // On battery the processor runs to different power limits, so the model would describe a machine that
        // only exists while unplugged — and the controller would then use it plugged in.
        Assert.Multiple(() =>
        {
            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.Failure, Is.EqualTo(FanCalibrationFailure.OnBattery));

            // Refused before anything was heated or any fan was touched.
            Assert.That(plant.SetFanDutyCalls, Is.Empty);
            Assert.That(plant.IsRunning, Is.False, "the CPU load was started despite running on battery");
        });
    }

    [Test]
    public async Task RunAsync_AbortsWhenTheChargerIsUnplugged()
    {
        using var plant = new SimulatedThermalPlant();
        using var harness = new Harness(plant);
        using var cancellation = new CancellationTokenSource();

        var run = harness.RunAsync(cancellation.Token);
        await harness.WaitUntilLoadedAsync();

        plant.PowerSourceState = FrameworkPowerSourceState.BatteryOnly;
        plant.BatteryState = FrameworkBatteryState.Discharging;

        var result = await run;

        // Everything measured before the unplug and everything after describe two different machines, so a
        // fit across the two describes neither.
        Assert.That(result.Failure, Is.EqualTo(FanCalibrationFailure.OnBattery));
        harness.AssertMachineWasHandedBack();
    }

    [Test]
    public async Task RunAsync_ProceedsWhenTheChargerIsAttachedButTheBatteryIsDischarging()
    {
        // A plugged-in laptop under heavy load: the adapter cannot carry the peak, so the battery tops it up
        // and reports discharging. The charger IS attached — refusing here would block calibration on exactly
        // the machines that most need it, and it is the trap a discharge-only check falls straight into.
        using var plant = new SimulatedThermalPlant
        {
            PowerSourceState = FrameworkPowerSourceState.AcAndBattery,
            BatteryState = FrameworkBatteryState.Discharging,
        };

        using var harness = new Harness(plant);

        var result = await harness.RunAsync();

        Assert.Multiple(() =>
        {
            Assert.That(result.Failure, Is.Not.EqualTo(FanCalibrationFailure.OnBattery));
            Assert.That(result.Succeeded, Is.True);
        });
    }

    [Test]
    public async Task RunAsync_ProceedsWhenTheMachineReportsNoPowerSourceAtAll()
    {
        // Fails open on missing information. Refusing here would permanently lock calibration out of any
        // machine that does not populate these fields, and the cost of wrongly allowing a run is a few wasted
        // minutes rather than damage.
        using var plant = new SimulatedThermalPlant { ReportsPowerSource = false };
        using var harness = new Harness(plant);

        var result = await harness.RunAsync();

        Assert.That(result.Succeeded, Is.True);
    }

    [Test]
    public async Task RunAsync_AbortsAtTheSafetyCeiling()
    {
        // Hot enough that holding the pre-step duty walks it past the ceiling.
        using var plant = new SimulatedThermalPlant { LoadRiseCelsius = 75d };
        using var harness = new Harness(plant);

        var result = await harness.RunAsync();

        Assert.Multiple(() =>
        {
            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.Failure, Is.EqualTo(FanCalibrationFailure.TemperatureCeiling));
            Assert.That(result.PeakTemperatureCelsius, Is.GreaterThanOrEqualTo(FanCalibrationRunner.SafetyCeilingCelsius));
        });

        harness.AssertMachineWasHandedBack();
    }

    [Test]
    public async Task RunAsync_AbortsWhenASensorItIsNotFittingAgainstOverheats()
    {
        // The run measures against sensor 0 but sensor 3 is the one that cooks. Watching only the driving
        // sensors would miss this entirely — which is precisely the shape of calibrating the GPU fan against
        // GPU sensors while the CPU is the thing under load.
        using var plant = new SimulatedThermalPlant { RunawaySensorIndex = 3 };
        using var harness = new Harness(plant);

        var result = await harness.RunAsync();

        Assert.Multiple(() =>
        {
            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.Failure, Is.EqualTo(FanCalibrationFailure.TemperatureCeiling));
        });

        harness.AssertMachineWasHandedBack();
    }

    /// <summary>
    /// A cooldown survives readings past the ceiling — that is the overshoot it exists to absorb.
    /// </summary>
    /// <remarks>
    /// The regression this pins: a real run tripped the retry at ~90 °C, and the sensor kept climbing for a
    /// few seconds afterwards — heat already in flight, load winding down — reaching 96 °C during the very
    /// first cooldown, where a live ceiling abort killed the run before the sibling ever got its raise. With
    /// a runaway sensor the readings NEVER leave the ceiling, so every cooldown here sits above 95 °C: the
    /// escalation must still walk all the way to full sibling duty, and only the final, disarmed attempt may
    /// meet the honest abort.
    /// </remarks>
    [Test]
    public async Task RunAsync_CooldownSurvivesTheOvershoot_AndEscalationRunsItsCourse()
    {
        using var plant = new SimulatedThermalPlant { RunawaySensorIndex = 3 };
        using var harness = new Harness(plant);

        const int siblingIndex = FanIndex + 1;
        plant.FanStateSource.AddOrUpdate(new FanStateSnapshot
        {
            FanIndex = siblingIndex,
            DisplayName = "Sibling",
            CoolingRole = FanCoolingRole.Cpu,
            FanState = default,
            ObservedAt = DateTimeOffset.UtcNow,
            IsAvailable = true,
        });

        var result = await harness.RunAsync();

        Assert.Multiple(() =>
        {
            Assert.That(result.Succeeded, Is.False, "a genuinely overheating machine must still end in failure");
            Assert.That(result.Failure, Is.EqualTo(FanCalibrationFailure.TemperatureCeiling));
            Assert.That(
                plant.SetFanDutyCalls,
                Does.Contain((siblingIndex, FanCalibrationRunner.MaximumSiblingHoldDutyPercent)),
                "escalation died early — a ceiling reading during a cooldown aborted the retry it exists to allow");
        });

        harness.AssertMachineWasHandedBack();
    }

    [Test]
    public async Task RunAsync_AbortsWhenTheDrivingSensorStopsReporting()
    {
        // A failed sensor leaves the run heating the machine with a deliberately low fan and nothing watching
        // the temperature it is supposed to be controlling. Giving up quickly is the only safe response.
        using var plant = new SimulatedThermalPlant { DrivingSensorFails = true };
        using var harness = new Harness(plant);

        var result = await harness.RunAsync();

        Assert.Multiple(() =>
        {
            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.Failure, Is.EqualTo(FanCalibrationFailure.InsufficientData));
            Assert.That(plant.IsRunning, Is.False, "the CPU load was left running with no temperature to watch");
        });
    }

    [Test]
    public async Task RunAsync_StopsTheLoadAndRestoresTheFan_WhenCancelled()
    {
        using var plant = new SimulatedThermalPlant();
        using var harness = new Harness(plant);
        using var cancellation = new CancellationTokenSource();

        var run = harness.RunAsync(cancellation.Token);
        await harness.WaitUntilLoadedAsync();
        await cancellation.CancelAsync();

        var result = await run;

        Assert.That(result.Failure, Is.EqualTo(FanCalibrationFailure.Cancelled));
        harness.AssertMachineWasHandedBack();
    }

    [Test]
    public async Task RunAsync_StopsTheLoadAndRestoresTheFan_WhenProgressReportingThrows()
    {
        // Stands in for the client vanishing mid-run: the stream write fails on every update.
        using var plant = new SimulatedThermalPlant();
        using var harness = new Harness(plant) { ProgressThrows = true };

        var result = await harness.RunAsync();

        // A dead client must not corrupt the run, and must not strand the machine either.
        Assert.That(result.Succeeded, Is.True);
        harness.AssertMachineWasHandedBack();
    }

    [Test]
    public async Task RunAsync_RefusesASecondConcurrentRun()
    {
        using var plant = new SimulatedThermalPlant();
        using var harness = new Harness(plant);
        using var cancellation = new CancellationTokenSource();

        var run = harness.RunAsync(cancellation.Token);
        await harness.WaitUntilLoadedAsync();

        // Two runs would heat the same chassis while each assumed it owned the thermal conditions.
        Assert.ThrowsAsync<InvalidOperationException>(() => harness.RunAsync());

        await cancellation.CancelAsync();
        await run;
    }

    [Test]
    public async Task RunAsync_ClaimsTheFanForTheWholeRunAndReleasesItAfterwards()
    {
        using var plant = new SimulatedThermalPlant();
        using var harness = new Harness(plant);
        using var cancellation = new CancellationTokenSource();

        var run = harness.RunAsync(cancellation.Token);
        await harness.WaitUntilLoadedAsync();

        // While the run owns the fan, nothing else may drive it — this claim is what the curve worker reads.
        Assert.That(harness.Arbiter.IsCalibrating(FanIndex), Is.True, "the run did not claim the fan");

        await cancellation.CancelAsync();
        await run;

        // Released only after the fan is back under automatic control, never before.
        Assert.Multiple(() =>
        {
            Assert.That(harness.Arbiter.IsCalibrating(FanIndex), Is.False, "the claim outlived the run");
            Assert.That(plant.RestoreAutoCalls, Does.Contain(FanIndex));
        });
    }

    [Test]
    public async Task RunAsync_FailsWithoutDrivingSensors()
    {
        using var plant = new SimulatedThermalPlant();
        using var harness = new Harness(plant);

        var result = await harness.Runner.RunAsync(FanIndex, [], _ => Task.CompletedTask, CancellationToken.None);

        // Nothing to control against, so nothing to measure — and crucially, the fan is never touched.
        Assert.Multiple(() =>
        {
            Assert.That(result.Failure, Is.EqualTo(FanCalibrationFailure.InsufficientData));
            Assert.That(plant.SetFanDutyCalls, Is.Empty);
        });
    }

    [Test]
    public async Task RunAsync_StepsTheFanOnlyAfterTheLoadHasSettled()
    {
        using var plant = new SimulatedThermalPlant();
        using var harness = new Harness(plant);

        await harness.RunAsync();

        var steps = harness.Progress.Select(update => update.Step).Distinct().ToList();

        // Identifying K needs duty to vary while load holds still. Stepping the fan before the load settles
        // would confound the two and produce a number describing neither.
        Assert.That(
            steps.IndexOf(FanCalibrationStep.LoadingAndSettling),
            Is.LessThan(steps.IndexOf(FanCalibrationStep.SteppingFan)));
    }

    [Test]
    public async Task RunAsync_MarksTheSampleWhereTheFanWasStepped()
    {
        using var plant = new SimulatedThermalPlant();
        using var harness = new Harness(plant);

        await harness.RunAsync();

        // The plot needs to know where the step was, or the response it draws is uninterpretable.
        Assert.That(harness.Progress.Count(update => update.IsStepMarker), Is.EqualTo(1));
    }

    private sealed class Harness : IDisposable
    {
        private readonly SimulatedThermalPlant _plant;

        public Harness(SimulatedThermalPlant plant, FanCoolingRole coolingRole = FanCoolingRole.Cpu)
        {
            _plant = plant;

            Store = new FrameworkFanControlStateStore(
                plant,
                new FrameworkFanControlSafetyTracker(),
                new TestOptionsMonitor<FrameworkServiceOptions>(new FrameworkServiceOptions()),
                NullLogger<FrameworkFanControlStateStore>.Instance);

            // Seeded through discovery rather than injected, so the store holds the fan the same way it would
            // at runtime.
            plant.FanStateSource.AddOrUpdate(new FanStateSnapshot
            {
                FanIndex = FanIndex,
                DisplayName = $"Fan {FanIndex}",

                // What the fan cools is what decides which component the run heats.
                CoolingRole = coolingRole,
                FanState = default,
                ObservedAt = DateTimeOffset.UtcNow,
                IsAvailable = true,
            });

            Runner = new FanCalibrationRunner(
                plant,
                Store,
                plant,
                plant,
                Arbiter,
                NullLogger<FanCalibrationRunner>.Instance,
                FastTimings);
        }

        public FrameworkFanControlStateStore Store { get; }

        /// <summary>What tells the curve worker to keep its hands off the fan under test.</summary>
        public FanCalibrationArbiter Arbiter { get; } = new();

        public FanCalibrationRunner Runner { get; }

        public List<FanCalibrationProgress> Progress { get; } = [];

        public bool ProgressThrows { get; init; }

        public Task<FanCalibrationRunResult> RunAsync(CancellationToken cancellationToken = default)
            => Runner.RunAsync(FanIndex, DrivingSensors, OnProgressAsync, cancellationToken);

        /// <summary>Waits until the run has actually started heating, so a test cancels mid-run rather than before it.</summary>
        public async Task WaitUntilLoadedAsync()
        {
            var deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(10);

            while (!_plant.IsRunning && DateTimeOffset.UtcNow < deadline)
            {
                await Task.Delay(5).ConfigureAwait(false);
            }

            Assert.That(_plant.IsRunning, Is.True, "the run never reached the loaded step");
        }

        /// <summary>The promise every failure path owes the user: no heat left running, no fan left overridden.</summary>
        public void AssertMachineWasHandedBack()
        {
            Assert.Multiple(() =>
            {
                Assert.That(_plant.IsRunning, Is.False, "the CPU load was left running");
                Assert.That(_plant.RestoreAutoCalls, Does.Contain(FanIndex), "the fan was left under calibration's control");
            });
        }

        public void Dispose()
        {
            Runner.Dispose();
            Store.Dispose();
        }

        private Task OnProgressAsync(FanCalibrationProgress progress)
        {
            if (ProgressThrows)
            {
                throw new InvalidOperationException("Simulated client disconnect.");
            }

            Progress.Add(progress);
            return Task.CompletedTask;
        }
    }
}
