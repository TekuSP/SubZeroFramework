using NUnit.Framework;

using SubZeroFramework.Tests.Simulation;

namespace SubZeroFramework.Tests;

/// <summary>
/// Verifies the simulator IS the first-order-plus-dead-time plant it claims to be, before anything is tuned
/// against it. Every downstream control and fitting test inherits its credibility from these — a controller
/// that behaves beautifully against a plant with the wrong dead time has proven nothing.
/// </summary>
[TestFixture]
public class ThermalPlantSimulatorTests
{
    private const double HeatPowerWatts = 50d;
    private const double InitialDutyPercent = 30d;
    private const double SteppedDutyPercent = 70d;

    private static readonly TimeSpan TimeStep = TimeSpan.FromSeconds(1);

    private static ThermalPlantSimulator CreateSettledPlant(ThermalPlantParameters? parameters = null, TimeSpan? timeStep = null)
    {
        var simulator = new ThermalPlantSimulator(parameters ?? new ThermalPlantParameters(), timeStep ?? TimeStep);
        simulator.Settle(InitialDutyPercent, HeatPowerWatts);
        return simulator;
    }

    [Test]
    public void Settle_LeavesThePlantAtRest()
    {
        var simulator = CreateSettledPlant();
        var settled = simulator.CoreTemperatureCelsius;

        for (var step = 0; step < 200; step++)
        {
            simulator.Advance(InitialDutyPercent, HeatPowerWatts);
        }

        Assert.Multiple(() =>
        {
            Assert.That(simulator.CoreTemperatureCelsius, Is.EqualTo(settled).Within(1e-9), "A settled plant with unchanged inputs must not drift.");
            Assert.That(simulator.IsAtAmbientFloor, Is.False, "The scenario must stay in the linear region or it is testing the clamp.");
        });
    }

    [Test]
    public void Advance_HoldsStillForExactlyTheDeadTime()
    {
        var simulator = CreateSettledPlant();
        var settled = simulator.CoreTemperatureCelsius;

        // The step is commanded now, but the plant must not react until the transport delay has passed.
        for (var step = 0; step < simulator.DeadTimeSteps; step++)
        {
            simulator.Advance(SteppedDutyPercent, HeatPowerWatts);
            Assert.That(
                simulator.CoreTemperatureCelsius,
                Is.EqualTo(settled).Within(1e-9),
                $"The plant moved {step + 1} step(s) in, inside its {simulator.DeadTimeSteps}-step dead time.");
        }

        simulator.Advance(SteppedDutyPercent, HeatPowerWatts);
        Assert.That(simulator.CoreTemperatureCelsius, Is.LessThan(settled - 1e-6), "The plant must start cooling on the first step after the dead time.");
    }

    [Test]
    public void Advance_CoversSixtyThreePercentOfTheStepOneTimeConstantAfterItBeginsMoving()
    {
        var parameters = new ThermalPlantParameters();
        var simulator = CreateSettledPlant(parameters);
        var settled = simulator.CoreTemperatureCelsius;
        var target = simulator.ComputeEquilibrium(SteppedDutyPercent, HeatPowerWatts);

        // Walk out the dead time, then exactly one time constant of actual response. This 63.2% property is
        // what the two-point FOPDT fit keys off, so if it is not exact the fitter cannot be exact either.
        var stepsToRun = simulator.DeadTimeSteps + (int)(parameters.TimeConstant / TimeStep);
        for (var step = 0; step < stepsToRun; step++)
        {
            simulator.Advance(SteppedDutyPercent, HeatPowerWatts);
        }

        var completedFraction = (settled - simulator.CoreTemperatureCelsius) / (settled - target);
        Assert.That(completedFraction, Is.EqualTo(1d - Math.Exp(-1d)).Within(1e-9));
    }

    [Test]
    public void Advance_SettlesAtTheGainTheParametersDeclare()
    {
        var parameters = new ThermalPlantParameters();
        var simulator = CreateSettledPlant(parameters);
        var settled = simulator.CoreTemperatureCelsius;

        for (var step = 0; step < 2000; step++)
        {
            simulator.Advance(SteppedDutyPercent, HeatPowerWatts);
        }

        var expectedDrop = (SteppedDutyPercent - InitialDutyPercent) * parameters.CoolingDegreesPerDutyPercent;

        Assert.Multiple(() =>
        {
            Assert.That(settled - simulator.CoreTemperatureCelsius, Is.EqualTo(expectedDrop).Within(1e-6), "Steady-state drop must equal K × Δduty.");
            Assert.That(simulator.IsAtAmbientFloor, Is.False);
        });
    }

    [Test]
    public void Advance_ProducesTheSameTrajectoryRegardlessOfTimeStep()
    {
        // Exact discretisation should make the result a function of elapsed plant time alone. A forward-Euler
        // integrator would fail this, and every tuning result would quietly carry the integrator's error.
        var parameters = new ThermalPlantParameters();
        var coarse = CreateSettledPlant(parameters, TimeSpan.FromSeconds(1));
        var fine = CreateSettledPlant(parameters, TimeSpan.FromSeconds(0.25));

        for (var step = 0; step < 120; step++)
        {
            coarse.Advance(SteppedDutyPercent, HeatPowerWatts);
        }

        for (var step = 0; step < 480; step++)
        {
            fine.Advance(SteppedDutyPercent, HeatPowerWatts);
        }

        Assert.Multiple(() =>
        {
            Assert.That(fine.Elapsed, Is.EqualTo(coarse.Elapsed));
            Assert.That(fine.CoreTemperatureCelsius, Is.EqualTo(coarse.CoreTemperatureCelsius).Within(1e-9));
        });
    }

    [Test]
    public void ComputeEquilibrium_CannotFallBelowAmbient()
    {
        var parameters = new ThermalPlantParameters();
        var simulator = new ThermalPlantSimulator(parameters, TimeStep);

        // Full airflow against almost no heat: the linear model would predict a sub-ambient temperature, which
        // no fan can produce. The clamp is deliberate, and the flag exists so a fitting test can prove it never
        // silently entered this region and fitted the clamp instead of the plant.
        simulator.Settle(dutyPercent: 100d, heatPowerWatts: 1d);

        Assert.Multiple(() =>
        {
            Assert.That(simulator.CoreTemperatureCelsius, Is.EqualTo(parameters.AmbientCelsius).Within(1e-9));
            Assert.That(simulator.IsAtAmbientFloor, Is.True);
        });
    }

    [Test]
    public void Advance_WithSensorNoise_StaysWithinTheBandAndRepeatsForTheSameSeed()
    {
        var parameters = new ThermalPlantParameters { SensorNoiseCelsius = 0.8d };
        var first = CreateSettledPlant(parameters);
        var second = CreateSettledPlant(parameters);

        for (var step = 0; step < 500; step++)
        {
            var firstReading = first.Advance(InitialDutyPercent, HeatPowerWatts);
            var secondReading = second.Advance(InitialDutyPercent, HeatPowerWatts);

            Assert.That(secondReading, Is.EqualTo(firstReading), "The same seed must reproduce the same run — a flaky control test is worse than none.");
            Assert.That(
                Math.Abs(firstReading - first.CoreTemperatureCelsius),
                Is.LessThanOrEqualTo(parameters.SensorNoiseCelsius / 2d),
                "Noise must stay inside the declared peak-to-peak band.");
        }
    }

    [Test]
    public void Advance_WithoutSensorNoise_ReportsTheCoreTemperatureExactly()
    {
        var simulator = CreateSettledPlant();

        for (var step = 0; step < 50; step++)
        {
            var reading = simulator.Advance(SteppedDutyPercent, HeatPowerWatts);
            Assert.That(reading, Is.EqualTo(simulator.CoreTemperatureCelsius));
        }
    }
}
