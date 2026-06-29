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
    int? FollowFanIndex)
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
    /// what the chart draws: the points are anchored at (0, 0) and (130, last-duty) so temperatures below the
    /// first / above the last point ramp from 0 / hold flat the same way the rendered series does.
    /// </summary>
    public double InterpolateDuty(double temperatureCelsius)
    {
        var series = BuildAnchoredSeries();

        var first = series[0];
        if (temperatureCelsius <= first.Temperature)
        {
            return Math.Clamp(first.Duty, 0d, 100d);
        }

        var last = series[^1];
        if (temperatureCelsius >= last.Temperature)
        {
            return Math.Clamp(last.Duty, 0d, 100d);
        }

        for (var i = 1; i < series.Count; i++)
        {
            var lower = series[i - 1];
            var upper = series[i];
            if (temperatureCelsius <= upper.Temperature)
            {
                if (upper.Temperature <= lower.Temperature)
                {
                    return Math.Clamp(upper.Duty, 0d, 100d);
                }

                var ratio = (temperatureCelsius - lower.Temperature) / (upper.Temperature - lower.Temperature);
                return Math.Clamp(lower.Duty + (ratio * (upper.Duty - lower.Duty)), 0d, 100d);
            }
        }

        return Math.Clamp(last.Duty, 0d, 100d);
    }

    // The rendered curve series: the raw points anchored at 0°C / 130°C exactly as the chart builds them.
    private List<(double Temperature, double Duty)> BuildAnchoredSeries()
    {
        var ordered = CurvePoints
            .OrderBy(static p => p.Temperature)
            .Select(static p => ((double)p.Temperature, p.Duty))
            .ToList();

        if (ordered.Count == 0)
        {
            return [(0d, 0d), (130d, 100d)];
        }

        List<(double Temperature, double Duty)> series = [];
        if (ordered[0].Item1 > 0d)
        {
            series.Add((0d, 0d));
        }
        series.AddRange(ordered);
        if (ordered[^1].Item1 < 130d)
        {
            series.Add((130d, ordered[^1].Item2));
        }

        return series;
    }
}
