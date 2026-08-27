using System.Diagnostics;

using Microsoft.Extensions.Logging.Abstractions;

using NUnit.Framework;

using SubZeroFramework.Service.Services;
using SubZeroFramework.Services.Control;

namespace SubZeroFramework.Tests.Windows;

/// <summary>
/// Runs the CPU load generator WITH a real system-load probe — the configuration production runs and the
/// shared test suite never covers.
/// </summary>
/// <remarks>
/// <para>
/// The shared suite's stability tests construct the generator without a probe, so the governor's foreign-load
/// branch never executes there. That gap shipped an oscillator: the probe's total and own figures come from
/// different clocks, and around the generator's own transients their difference swings by the full transient
/// amplitude — fed back at governor speed, the load self-excited between 20% and 90% on this machine and put
/// ±4 °C of noise on a calibration that then failed for an unmeasurable temperature swing.
/// </para>
/// <para>
/// Hardware category: this genuinely loads the machine for about half a minute, and a busy machine can fail
/// it — a limitation of measuring the real thing, not a flaky test.
/// </para>
/// </remarks>
[TestFixture]
[Category(HardwareTestCategories.Machine)]
[Platform("Win", Reason = "Uses the PDH-backed system load probe the Windows service runs with.")]
public class GovernedLoadStabilityTests
{
    private static readonly TimeSpan Ramp = TimeSpan.FromSeconds(2);

    /// <summary>Long enough for several periods of the ~10 s oscillation this guards against.</summary>
    private static readonly TimeSpan ObservationWindow = TimeSpan.FromSeconds(25);

    private static readonly TimeSpan SampleInterval = TimeSpan.FromMilliseconds(250);

    /// <summary>
    /// At full target with the real probe, the governed throttle must neither collapse nor oscillate.
    /// </summary>
    /// <remarks>
    /// Both symptoms were real. Against the pre-fix saturated utilisation counter the governor decayed to
    /// its floor (a calibration produced 25 W on a 54 W machine); against the corrected counter it
    /// oscillated. The bands here are generous — they catch those failure modes, not scheduler jitter.
    /// </remarks>
    [Test]
    public void EffectiveTarget_HoldsSteadyNearFull_WithTheRealProbe()
    {
        using var probe = new ControlTelemetrySystemLoadProbe(
            new WindowsPdhControlTelemetryReader(NullLogger<WindowsPdhControlTelemetryReader>.Instance));

        using var generator = new CpuLoadGenerator(probe, rampDuration: Ramp, targetFraction: 1d);

        generator.Start();

        try
        {
            // Let the ramp finish and the governor take its first few samples before judging it.
            Thread.Sleep(Ramp + TimeSpan.FromSeconds(3));

            List<double> observed = [];
            var clock = Stopwatch.StartNew();

            while (clock.Elapsed < ObservationWindow)
            {
                observed.Add(generator.EffectiveTargetFraction);
                Thread.Sleep(SampleInterval);
            }

            var minimum = observed.Min();
            var maximum = observed.Max();

            TestContext.Out.WriteLine(
                $"Governed throttle over {ObservationWindow.TotalSeconds:N0}s: " +
                $"min {minimum:P0}, max {maximum:P0}, swing {maximum - minimum:P0}.");

            Assert.Multiple(() =>
            {
                Assert.That(
                    minimum,
                    Is.GreaterThanOrEqualTo(0.65d),
                    "The governed throttle collapsed — the foreign-load estimate is eating the target again.");

                Assert.That(
                    maximum - minimum,
                    Is.LessThanOrEqualTo(0.25d),
                    "The governed throttle is oscillating — the foreign-load feedback has gone unstable again.");
            });
        }
        finally
        {
            generator.Stop();
        }
    }
}
