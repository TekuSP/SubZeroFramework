using System.Diagnostics;
using System.Globalization;

using Microsoft.Extensions.Logging.Abstractions;

using NUnit.Framework;

using SubZeroFramework.Service.Services;
using SubZeroFramework.Services.Control;

namespace SubZeroFramework.Tests.Windows;

/// <summary>
/// Runs the governed CPU load for MINUTES on the real machine, watching for slow strangulation.
/// </summary>
/// <remarks>
/// <para>
/// The short stability tests hold their target for twenty-five seconds on a cool machine. A real
/// calibration holds it for fifteen minutes on a hot one — and a full run's telemetry showed CPU usage
/// sagging to 30-40% while the generator's target sat above 90%, which is exactly the shape of the
/// governor's foreign-load estimate inflating over time and eating the throttle. This soak reproduces the
/// conditions: full target, real probe, the machine heating itself under its own firmware-driven fans.
/// </para>
/// <para>
/// Hardware category, and the heaviest test in the suite: it loads the machine hard for about seven
/// minutes. Run it deliberately, not incidentally.
/// </para>
/// </remarks>
[TestFixture]
[Category(HardwareTestCategories.Machine)]
[Platform("Win", Reason = "Uses the PDH-backed system load probe the Windows service runs with.")]
public class GovernedLoadSoakTests
{
    private static readonly TimeSpan SoakDuration = TimeSpan.FromMinutes(7);

    private static readonly TimeSpan ReportInterval = TimeSpan.FromSeconds(15);

    /// <summary>
    /// Over a long, hot soak the governed load must not decay — the strangulation a real run showed.
    /// </summary>
    [Test]
    public void GovernedLoad_DoesNotDecay_OverALongHotSoak()
    {
        // The generator's own probe, as in production.
        using var governorProbe = new ControlTelemetrySystemLoadProbe(
            new WindowsPdhControlTelemetryReader(NullLogger<WindowsPdhControlTelemetryReader>.Instance));

        // An INDEPENDENT reader for ground truth, so the measurement cannot inherit the governor's error.
        using var observer = new WindowsPdhControlTelemetryReader(NullLogger<WindowsPdhControlTelemetryReader>.Instance);

        using var generator = new CpuLoadGenerator(governorProbe, rampDuration: TimeSpan.FromSeconds(5), targetFraction: 1d);

        generator.Start();

        List<(double Minutes, double Busy, double EffectiveTarget)> trajectory = [];

        try
        {
            Thread.Sleep(TimeSpan.FromSeconds(10));
            _ = observer.Sample();

            var clock = Stopwatch.StartNew();

            while (clock.Elapsed < SoakDuration)
            {
                Thread.Sleep(ReportInterval);

                var busy = observer.Sample().CpuUtilizationFraction ?? double.NaN;
                trajectory.Add((clock.Elapsed.TotalMinutes, busy, generator.EffectiveTargetFraction));

                TestContext.Out.WriteLine(string.Format(
                    CultureInfo.InvariantCulture,
                    "{0,5:N1} min   busy {1,6:P1}   governed target {2,6:P1}",
                    clock.Elapsed.TotalMinutes,
                    busy,
                    generator.EffectiveTargetFraction));
            }
        }
        finally
        {
            generator.Stop();
        }

        var firstMinute = trajectory.Where(static point => point.Minutes <= 1.5d).Average(static point => point.Busy);
        var lastMinute = trajectory.Where(static point => point.Minutes >= SoakDuration.TotalMinutes - 1.5d).Average(static point => point.Busy);

        TestContext.Out.WriteLine(string.Format(
            CultureInfo.InvariantCulture,
            "First-minute busy {0:P1}; last-minute busy {1:P1}.",
            firstMinute,
            lastMinute));

        Assert.Multiple(() =>
        {
            Assert.That(
                lastMinute,
                Is.GreaterThanOrEqualTo(0.70d),
                "The governed load decayed under sustained heat — the strangulation a real calibration showed.");

            Assert.That(
                trajectory.Min(static point => point.EffectiveTarget),
                Is.GreaterThanOrEqualTo(0.60d),
                "The governor's throttle collapsed at some point during the soak.");
        });
    }
}
