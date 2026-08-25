using System.Collections.Immutable;

namespace SubZeroFramework.Models;

/// <summary>One measured operating point: the temperature this fan duty settles at, under the run's load.</summary>
/// <param name="DutyPercent">The duty held.</param>
/// <param name="SettledCelsius">The temperature it settled at.</param>
public readonly record struct FanGainPoint(double DutyPercent, double SettledCelsius);

/// <summary>
/// How much cooling a duty point buys, measured at several duties rather than assumed constant.
/// </summary>
/// <remarks>
/// <para>
/// <b>Fan cooling is strongly nonlinear.</b> Heat transfer rises roughly as airflow to the power of two
/// thirds to four fifths, so a duty point is worth several times more at 20% than at 90% — the difference
/// between a fan that is barely moving air and one already close to its limit.
/// </para>
/// <para>
/// That matters because the tuning rule divides by the process gain: <c>Kc = τ / (K·(λ+L))</c>. A single
/// averaged K is wrong at both ends of the range, and being wrong LOW at low duty makes the controller
/// proportionally more aggressive exactly where the fan is quiet enough for the user to hear it hunt. Scaling
/// the gain to the operating point — gain scheduling — is the standard answer, and it is only possible if the
/// curve was measured.
/// </para>
/// <para>
/// The shape is a property of the chassis and does not need re-measuring as the machine ages; what drifts is
/// the overall level, which ongoing learning tracks by scaling the whole curve. See <see cref="Scaled"/>.
/// </para>
/// </remarks>
public sealed record FanGainCurve
{
    /// <summary>No curve was measured — an older calibration, or a run that could not complete the sweep.</summary>
    public static FanGainCurve None { get; } = new();

    /// <summary>The measured points, ordered by ascending duty.</summary>
    public ImmutableArray<FanGainPoint> Points { get; init; } = [];

    /// <summary>Whether there are enough points to say anything about the shape.</summary>
    /// <remarks>
    /// Two points describe a straight line, which is the assumption this exists to replace — so two is not
    /// enough to be worth scheduling on.
    /// </remarks>
    public bool IsUsable => Points.Length >= 3;

    /// <summary>
    /// Degrees of cooling per duty point, local to <paramref name="dutyPercent"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The magnitude of the local slope of settled temperature against duty. Returned positive, because the
    /// tuning rule wants a magnitude and more duty always means less heat.
    /// </para>
    /// <para>
    /// Outside the measured range the nearest measured segment's slope is used rather than extrapolating the
    /// curve. Extrapolating a decaying curve past its last point produces gains approaching zero, and a gain
    /// approaching zero makes the tuning rule divide by almost nothing — an enormous controller gain derived
    /// from a region nobody measured.
    /// </para>
    /// </remarks>
    /// <param name="dutyPercent">The operating point to evaluate at.</param>
    /// <param name="fallbackGain">Returned when there is no usable curve.</param>
    public double GainAt(double dutyPercent, double fallbackGain)
    {
        if (!IsUsable)
        {
            return fallbackGain;
        }

        for (var i = 0; i < Points.Length - 1; i++)
        {
            var lower = Points[i];
            var upper = Points[i + 1];

            // The last segment also serves everything above it, and the first everything below.
            var isLast = i == Points.Length - 2;
            if (dutyPercent > upper.DutyPercent && !isLast)
            {
                continue;
            }

            var span = upper.DutyPercent - lower.DutyPercent;
            if (span <= 0d)
            {
                continue;
            }

            var slope = Math.Abs(upper.SettledCelsius - lower.SettledCelsius) / span;

            // A segment where the temperature did not move is measurement noise, not a real zero gain, and
            // returning it would blow the tuning rule up.
            return slope > 0d ? slope : fallbackGain;
        }

        return fallbackGain;
    }

    /// <summary>
    /// The same shape at a different overall level.
    /// </summary>
    /// <remarks>
    /// What ongoing identification learns is how effective the cooling is NOW — dust, a dried-out paste, a
    /// blocked vent — which moves the whole curve rather than reshaping it. Scaling preserves the measured
    /// nonlinearity while tracking the drift, so a machine keeps the benefit of its calibration for as long
    /// as its geometry is unchanged.
    /// </remarks>
    public FanGainCurve Scaled(double factor)
    {
        if (!IsUsable || !double.IsFinite(factor) || factor <= 0d)
        {
            return this;
        }

        // Scaling temperature SPANS, not absolute temperatures: the ambient the curve sits on top of does not
        // change when the cooling gets less effective, only the rise above it.
        var reference = Points[^1].SettledCelsius;

        return this with
        {
            Points = [.. Points.Select(point => point with
            {
                SettledCelsius = reference + ((point.SettledCelsius - reference) * factor),
            })],
        };
    }
}
