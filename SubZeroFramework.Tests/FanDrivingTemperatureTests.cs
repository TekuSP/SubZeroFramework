using NUnit.Framework;

using SubZeroFramework.Models;

namespace SubZeroFramework.Tests;

/// <summary>
/// The reduction from a curve's selected sensors to the one temperature that drives it. Shared by the client's
/// predicted duty and the service's actuation, so every rule here is a promise the fan actually keeps.
/// </summary>
[TestFixture]
public class FanDrivingTemperatureTests
{
    [Test]
    public void Aggregate_SkipsMissingReadings_ByDefault()
    {
        // The regression that mattered: the service used to fold a powered-down sensor's 0 °C into the
        // average, halving the driving temperature and under-cooling a hot CPU.
        var result = FanDrivingTemperature.Aggregate(
            [70d, null],
            TemperatureAggregationMode.Average,
            treatMissingAsZero: false);

        Assert.That(result, Is.EqualTo(70d).Within(0.0001), "A sensor with no reading must not vote.");
    }

    [Test]
    public void Aggregate_TreatingMissingAsZero_CountsItAsCold()
    {
        var result = FanDrivingTemperature.Aggregate(
            [70d, null],
            TemperatureAggregationMode.Average,
            treatMissingAsZero: true);

        Assert.That(result, Is.EqualTo(35d).Within(0.0001));
    }

    [Test]
    public void Aggregate_TreatingMissingAsZero_UnderMaximum_IgnoresTheDarkSensor()
    {
        // The combination the option is for: GPU sensors go dark, 0 °C never wins a Maximum, and the curve
        // keeps running off whatever is still alive.
        var result = FanDrivingTemperature.Aggregate(
            [null, null, 62d],
            TemperatureAggregationMode.Maximum,
            treatMissingAsZero: true);

        Assert.That(result, Is.EqualTo(62d).Within(0.0001));
    }

    [Test]
    public void Aggregate_TreatingMissingAsZero_WhenEverySensorIsDark_ReadsCold()
    {
        // Everything the curve watches is switched off, so the curve lands on its coldest point rather than
        // leaving the fan blind.
        var result = FanDrivingTemperature.Aggregate(
            [null, null],
            TemperatureAggregationMode.Maximum,
            treatMissingAsZero: true);

        Assert.That(result, Is.EqualTo(0d).Within(0.0001));
    }

    [Test]
    public void Aggregate_WithoutTheOption_WhenEverySensorIsDark_ReturnsNull()
    {
        // Null means "this curve cannot be evaluated" — the caller hands the fan back to firmware control
        // rather than inventing a temperature.
        var result = FanDrivingTemperature.Aggregate(
            [null, null],
            TemperatureAggregationMode.Maximum,
            treatMissingAsZero: false);

        Assert.That(result, Is.Null);
    }

    [Test]
    public void Aggregate_WithNoSensorsSelected_ReturnsNull()
    {
        Assert.That(FanDrivingTemperature.Aggregate([], TemperatureAggregationMode.Maximum, treatMissingAsZero: true), Is.Null);
    }

    [TestCase(TemperatureAggregationMode.Maximum, 80d)]
    [TestCase(TemperatureAggregationMode.Minimum, 40d)]
    [TestCase(TemperatureAggregationMode.Average, 60d)]
    [TestCase(TemperatureAggregationMode.Median, 60d)]
    public void Aggregate_HonorsEachMode_OverTheReadableSubset(TemperatureAggregationMode mode, double expected)
    {
        var result = FanDrivingTemperature.Aggregate([40d, null, 60d, 80d], mode, treatMissingAsZero: false);

        Assert.That(result, Is.EqualTo(expected).Within(0.0001));
    }

    [Test]
    public void Median_OfAnEvenCount_AveragesTheMiddlePair()
    {
        Assert.That(FanDrivingTemperature.Median([10d, 20d, 30d, 40d]), Is.EqualTo(25d).Within(0.0001));
    }

    [Test]
    public void Median_IsOrderIndependent()
    {
        Assert.That(FanDrivingTemperature.Median([80d, 40d, 60d]), Is.EqualTo(60d).Within(0.0001));
    }
}
