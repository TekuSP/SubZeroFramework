namespace SubZeroFramework.Models;

/// <summary>
/// Immutable capture of a custom fan-curve draft (or the applied baseline it is compared against): the driving
/// sensors + how they aggregate, the curve points, or a follow target. A pure value type so the editor's
/// dirty / can-apply gating is decoupled from the ViewModel and independently testable.
/// </summary>
public sealed record CustomCurveSnapshot(
    TemperatureAggregationMode Aggregation,
    int[] SensorIndices,
    (int Temperature, double Duty)[] CurvePoints,
    int? FollowFanIndex,
    bool TreatMissingSensorsAsZero = false)
{
    /// <summary>
    /// Editor-equality: a follow slot is defined solely by its target (its points/sensors are irrelevant); a
    /// self-driven slot matches when the aggregation, the order-independent sensor set, and the curve points
    /// (duty within 0.01%) all agree. Used to compute IsDirty / IsTestDraftChanged and to suppress no-op
    /// service-state reconciliations.
    /// </summary>
    public bool Matches(CustomCurveSnapshot other)
    {
        if (FollowFanIndex != other.FollowFanIndex)
        {
            return false;
        }

        // Follow slots are defined purely by their target; their curve points/sensors are irrelevant.
        if (FollowFanIndex is not null)
        {
            return true;
        }

        if (Aggregation != other.Aggregation)
        {
            return false;
        }

        // Part of how the curve reads its sensors, exactly like the aggregation mode — toggling it is an edit.
        if (TreatMissingSensorsAsZero != other.TreatMissingSensorsAsZero)
        {
            return false;
        }

        if (!SensorIndices.OrderBy(static i => i).SequenceEqual(other.SensorIndices.OrderBy(static i => i)))
        {
            return false;
        }

        if (CurvePoints.Length != other.CurvePoints.Length)
        {
            return false;
        }

        var left = CurvePoints.OrderBy(static p => p.Temperature).ToArray();
        var right = other.CurvePoints.OrderBy(static p => p.Temperature).ToArray();
        for (var i = 0; i < left.Length; i++)
        {
            if (left[i].Temperature != right[i].Temperature)
            {
                return false;
            }

            if (Math.Abs(left[i].Duty - right[i].Duty) > 0.01d)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Predicted duty (0–100%) this curve targets at <paramref name="temperatureCelsius"/>, matching exactly
    /// what the chart draws and what the service actuates: the points are anchored at minimum speed when cold
    /// and maximum speed at the top of the domain, so temperatures below the first point ramp up from idle and
    /// temperatures above the last point keep climbing to full speed (see <see cref="FanCurveDomain"/>).
    /// </summary>
    public double InterpolateDuty(double temperatureCelsius) =>
        FanCurveDomain.InterpolateDuty(CurvePoints, temperatureCelsius);
}
