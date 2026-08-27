using NUnit.Framework;

using SubZeroFramework.Models;
using SubZeroFramework.Services;
using SubZeroFramework.Services.Units;

namespace SubZeroFramework.Tests;

/// <summary>
/// Guards the display/canonical conversion pair that the fan-curve editor's pointer path depends on.
/// </summary>
/// <remarks>
/// The curve chart plots in DISPLAY units so that a Fahrenheit user reads round Fahrenheit ticks, which means
/// <c>ScalePixelsToData</c> hands back Fahrenheit and <c>FanCurveEditorView.TryScaleToCanonical</c> has to
/// invert it before the point reaches <see cref="SubZeroFramework.Models.FanCurveDomain"/> and, ultimately, the
/// EC. That inverse is the one place in the app where a units bug is silent AND writes hardware state: a
/// point dragged to the tick reading 150 °F would be stored — and obeyed — as 150 °C, so the fan would sit
/// idle until the machine was 66 °C hotter than the user asked for. Hence a round-trip test per unit rather
/// than a spot check of one conversion direction.
/// </remarks>
public sealed class FanCurveDisplayUnitRoundTripTests
{
    // Every temperature option the catalog offers, so a newly added scale cannot skip this guard.
    private static string[] TemperatureOptionKeys =>
        [.. new UnitPreferenceCatalog()
            .Definitions
            .Single(definition => definition.Kind == UnitQuantityKind.Temperature)
            .Options
            .Select(option => option.Key)];

    private static string[] RatioOptionKeys =>
        [.. new UnitPreferenceCatalog()
            .Definitions
            .Single(definition => definition.Kind == UnitQuantityKind.Ratio)
            .Options
            .Select(option => option.Key)];

    [Test]
    public void ConvertTemperatureToCelsius_InvertsConvertTemperature_ForEveryTemperatureUnit()
    {
        // The chart window plus a couple of points outside it, since the pointer can be dragged past the edge.
        double[] celsiusSamples =
        [
            FanCurveDomain.ChartMinTemperatureCelsius,
            0d, 15d, 37.5d, 60d, 82.3d, 100d,
            FanCurveDomain.ChartMaxTemperatureCelsius,
            -40d, 200d,
        ];

        foreach (var optionKey in TemperatureOptionKeys)
        {
            var service = CreateService(UnitQuantityKind.Temperature, optionKey);

            foreach (var celsius in celsiusSamples)
            {
                var display = service.ConvertTemperature(celsius);
                var roundTripped = service.ConvertTemperatureToCelsius(display);

                Assert.That(
                    roundTripped,
                    Is.EqualTo(celsius).Within(1e-9),
                    $"{optionKey}: {celsius} °C -> {display} -> {roundTripped} °C");
            }
        }
    }

    [Test]
    public void ConvertRatioToPercent_InvertsConvertRatio_ForEveryRatioUnit()
    {
        double[] percentSamples = [0d, 0.5d, 12.5d, 33d, 50d, 87.4d, 100d];

        foreach (var optionKey in RatioOptionKeys)
        {
            var service = CreateService(UnitQuantityKind.Ratio, optionKey);

            foreach (var percent in percentSamples)
            {
                var display = service.ConvertRatio(percent);
                var roundTripped = service.ConvertRatioToPercent(display);

                Assert.That(
                    roundTripped,
                    Is.EqualTo(percent).Within(1e-9),
                    $"{optionKey}: {percent}% -> {display} -> {roundTripped}%");
            }
        }
    }

    [Test]
    public void ConvertTemperature_IsStrictlyIncreasing_SoTheAxisWindowNeverInverts()
    {
        // The axis binds MinLimit/MaxLimit to the converted domain edges. A scale that reversed order would
        // hand LiveCharts a min above its max and the chart would render empty rather than obviously wrong.
        foreach (var optionKey in TemperatureOptionKeys)
        {
            var service = CreateService(UnitQuantityKind.Temperature, optionKey);

            var min = service.ConvertTemperature(FanCurveDomain.ChartMinTemperatureCelsius);
            var max = service.ConvertTemperature(FanCurveDomain.ChartMaxTemperatureCelsius);

            Assert.That(max, Is.GreaterThan(min), $"{optionKey}: axis window [{min}, {max}] is inverted");
        }
    }

    [Test]
    public void ConvertTemperatureDelta_ScalesWidthsWithoutTheOffset()
    {
        // A hit radius or a step is a WIDTH, not a point on the scale. The absolute conversion of 10 °C is
        // 50 °F; the delta conversion is 18 °F. Using the wrong one is the classic units bug here, so it is
        // pinned to explicit numbers rather than a property.
        var fahrenheit = CreateService(UnitQuantityKind.Temperature, "fahrenheit");
        Assert.That(fahrenheit.ConvertTemperatureDelta(10d), Is.EqualTo(18d).Within(1e-9));
        Assert.That(fahrenheit.ConvertTemperature(10d), Is.EqualTo(50d).Within(1e-9));

        // Kelvin shares Celsius' scale factor, so a delta is unchanged even though absolutes shift by 273.15.
        var kelvin = CreateService(UnitQuantityKind.Temperature, "kelvin");
        Assert.That(kelvin.ConvertTemperatureDelta(10d), Is.EqualTo(10d).Within(1e-9));
        Assert.That(kelvin.ConvertTemperature(10d), Is.EqualTo(283.15d).Within(1e-9));
    }

    [Test]
    public void DraggingToTheDisplayedMaximum_StoresTheCanonicalMaximum()
    {
        // End-to-end shape of the drag inverse: the user drags to the top-right corner of the chart, which in
        // Fahrenheit reads 257 °F / 100%. What gets stored has to be the canonical 125 °C / 100%.
        var service = CreateService(UnitQuantityKind.Temperature, "fahrenheit");

        var displayedMax = service.ConvertTemperature(FanCurveDomain.ChartMaxTemperatureCelsius);
        Assert.That(displayedMax, Is.EqualTo(257d).Within(1e-9));

        var stored = service.ConvertTemperatureToCelsius(displayedMax);
        Assert.That(stored, Is.EqualTo((double)FanCurveDomain.ChartMaxTemperatureCelsius).Within(1e-9));

        // Without the inverse the raw 257 would be clamped to the editable ceiling instead — the bug this
        // whole test class exists to catch.
        Assert.That(FanCurveDomain.ClampTemperature((int)Math.Round(displayedMax)), Is.Not.EqualTo((int)Math.Round(stored)));
    }

    [Test]
    public void AxisTickFormatters_DoNotConvert_TheyOnlyFormat()
    {
        // Every chart in the app plots a series that was converted BEFORE it was plotted, so a Labeler must
        // format the tick as-is. This is the bug that shipped on the compute, CPU-core, CPU-package and
        // power cards: a converting labeler on a display-space axis, invisible on the default unit and wrong
        // on every other. Each assertion below passes the tick a Fahrenheit / fraction user actually sees and
        // requires that number back out unscaled.
        var fahrenheit = CreateService(UnitQuantityKind.Temperature, "fahrenheit");
        Assert.That(fahrenheit.FormatTemperatureAxisTick(257d), Does.StartWith("257").And.Contains("°F"));

        var fraction = CreateService(UnitQuantityKind.Ratio, "fraction");
        Assert.That(fraction.FormatRatioAxisTick(0.85d), Does.StartWith("0.8"));

        var perMille = CreateService(UnitQuantityKind.Ratio, "per-mille");
        Assert.That(perMille.FormatRatioAxisTick(850d), Does.StartWith("850"));

        var revsPerSecond = CreateService(UnitQuantityKind.FanSpeed, "rps");
        Assert.That(revsPerSecond.FormatFanSpeedAxisTick(70d), Does.StartWith("70"));

        var gigahertz = CreateService(UnitQuantityKind.ClockFrequency, "gigahertz");
        Assert.That(gigahertz.FormatClockFrequencyAxisTick(3.4d), Does.StartWith("3.4"));

        var millivolts = CreateService(UnitQuantityKind.Voltage, "millivolt");
        Assert.That(millivolts.FormatVoltageAxisTick(15_400d), Does.StartWith("15,400").Or.StartWith("15400"));

        var milliamps = CreateService(UnitQuantityKind.Current, "milliampere");
        Assert.That(milliamps.FormatCurrentAxisTick(2_500d), Does.StartWith("2,500").Or.StartWith("2500"));
    }

    private static UnitsNetUnitFormattingService CreateService(UnitQuantityKind kind, string optionKey)
        => new(new StubUserUnitPreferencesClient(new UserUnitPreferencesSnapshot
        {
            Entries = [new UserUnitPreferenceEntry(kind, optionKey)],
        }));

    private sealed class StubUserUnitPreferencesClient(UserUnitPreferencesSnapshot snapshot) : IUserUnitPreferencesClient
    {
        public string PreferencesFilePath => "stub";

        public UserUnitPreferencesSnapshot CurrentPreferences { get; } = snapshot;

        public Task<UserPreferencesOperationResult> ApplyPreferencesAsync(UserUnitPreferencesSnapshot snapshot, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<UserPreferencesOperationResult> ResetToDefaultsAsync(CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public IObservable<UserUnitPreferencesSnapshot> WatchPreferences()
            => throw new NotSupportedException();
    }
}
