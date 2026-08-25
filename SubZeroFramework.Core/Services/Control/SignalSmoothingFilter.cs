namespace SubZeroFramework.Services.Control;

/// <summary>
/// An exponential smoothing filter with an optional fast-attack path: rising values can be taken instantly
/// while falling values decay with a half-life.
/// </summary>
/// <remarks>
/// <para>
/// The asymmetry is the point for control inputs. A CPU power reading that jumps when a workload starts
/// should reach the fan controller immediately — that head start is the entire value of feed-forward — while
/// the same reading dropping for one sample must not drop the fan, because the heat that power already
/// produced is still in the heatsink. Symmetric smoothing forces one bad trade for both directions.
/// </para>
/// <para>
/// Decay is computed from elapsed time rather than a per-sample factor, so the filter behaves identically
/// whether the caller ticks every 150 ms or every 2 s. That matters here: the primary polling tier is
/// user-configurable, and a filter tuned in samples would change character when the user changed the rate.
/// </para>
/// </remarks>
public sealed class SignalSmoothingFilter
{
    private readonly double _halfLifeSeconds;
    private readonly bool _fastAttack;
    private double? _value;

    /// <summary>Creates a filter.</summary>
    /// <param name="halfLife">How long a value takes to decay halfway toward a new lower sample.</param>
    /// <param name="fastAttack">
    /// When true (the default) a higher sample is taken immediately; when false the filter smooths in both
    /// directions, which is what a noisy derivative needs.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="halfLife"/> is not positive.</exception>
    public SignalSmoothingFilter(TimeSpan halfLife, bool fastAttack = true)
    {
        if (halfLife <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(halfLife), halfLife, "The half-life must be positive.");
        }

        _halfLifeSeconds = halfLife.TotalSeconds;
        _fastAttack = fastAttack;
    }

    /// <summary>The current filtered value, or null when nothing has been sampled yet.</summary>
    public double? Current => _value;

    /// <summary>
    /// Folds one sample in and returns the filtered value.
    /// </summary>
    /// <param name="sample">
    /// The new reading, or null when the source could not be read. A null sample DECAYS the held value
    /// rather than clearing it, so a source that blinks does not produce a step change downstream.
    /// </param>
    /// <param name="elapsed">Time since the previous sample.</param>
    /// <returns>The filtered value, or null when nothing has ever been sampled.</returns>
    public double? Sample(double? sample, TimeSpan elapsed)
    {
        if (sample is double value && (!double.IsFinite(value)))
        {
            sample = null;
        }

        if (_value is not double current)
        {
            // Nothing held yet: adopt the first real reading verbatim rather than easing up from zero, which
            // would under-report a machine that was already busy when the controller started.
            _value = sample;
            return _value;
        }

        var decay = elapsed > TimeSpan.Zero
            ? Math.Pow(0.5d, elapsed.TotalSeconds / _halfLifeSeconds)
            : 1d;

        if (sample is not double next)
        {
            _value = current * decay;
            return _value;
        }

        if (_fastAttack && next >= current)
        {
            _value = next;
            return _value;
        }

        _value = next + ((current - next) * decay);
        return _value;
    }

    /// <summary>Drops the held value, so the next sample is adopted verbatim.</summary>
    public void Reset() => _value = null;
}
