namespace SubZeroFramework.Models;

/// <summary>
/// The shape of a fan curve: the temperature domain it spans, the implicit speed anchors at either end, and
/// the band a point may actually be placed in.
/// <para>
/// A curve is always evaluated across <see cref="MinTemperatureCelsius"/>–<see cref="MaxTemperatureCelsius"/>
/// with an anchor pinned at each end — <see cref="MinSpeedDutyPercent"/> when cold and
/// <see cref="MaxSpeedDutyPercent"/> when hot — so it always rises to full speed by the top of the domain no
/// matter where the user's own points stop. The anchors are not editable points and the editor's chart
/// deliberately windows them out of view.
/// </para>
/// <para>
/// Editable points therefore live in a narrower band (<see cref="EditableMinTemperatureCelsius"/>–<see
/// cref="EditableMaxTemperatureCelsius"/>) that sits inside that chart window: a point outside it is
/// invisible AND unreachable by the pointer, which is how points used to get stranded — dragging past the
/// plot edge keeps producing coordinates out in the axis margin, and the point parked there for good.
/// </para>
/// <para>
/// Every consumer of a curve reads its anchors from here: the drawn series, the client's predicted duty, and
/// the service's actuation must agree exactly, or a preview would lie about what the fan will do.
/// </para>
/// </summary>
public static class FanCurveDomain
{
    /// <summary>Coldest temperature the curve spans; the minimum-speed anchor sits here.</summary>
    public const int MinTemperatureCelsius = 0;

    /// <summary>Hottest temperature the curve spans; the maximum-speed anchor sits here.</summary>
    public const int MaxTemperatureCelsius = 130;

    /// <summary>Duty of the minimum-speed anchor: fans idle when the machine is cold.</summary>
    public const double MinSpeedDutyPercent = 0d;

    /// <summary>Duty of the maximum-speed anchor: fans reach full speed by the top of the domain.</summary>
    public const double MaxSpeedDutyPercent = 100d;

    /// <summary>Coldest editable point. Must stay above the curve chart's axis MinLimit.</summary>
    public const int EditableMinTemperatureCelsius = 15;

    /// <summary>Hottest editable point. Must stay below the curve chart's axis MaxLimit.</summary>
    public const int EditableMaxTemperatureCelsius = 120;

    /// <summary>Snaps a temperature into the editable band, rounded to the whole degree points are keyed by.</summary>
    public static int ClampTemperature(double celsius) =>
        (int)Math.Round(Math.Clamp(celsius, EditableMinTemperatureCelsius, EditableMaxTemperatureCelsius));

    /// <summary>Snaps a duty into 0–100 %.</summary>
    public static double ClampDuty(double percent) => Math.Clamp(percent, MinSpeedDutyPercent, MaxSpeedDutyPercent);

    /// <summary>
    /// Snaps every point into the editable band, ordered by temperature. Curves stored before the band was
    /// enforced (or written by another client) can hold points outside it; those are pulled to the edge so
    /// they can be seen and grabbed. The editor's applied baseline is normalized through here too, so
    /// snapping never fakes an unsaved-changes state on a curve the user has not touched.
    /// </summary>
    public static (int Temperature, double Duty)[] Normalize(IEnumerable<(int Temperature, double Duty)> points) =>
        points
            .Select(static point => (Temperature: ClampTemperature(point.Temperature), Duty: ClampDuty(point.Duty)))
            .OrderBy(static point => point.Temperature)
            .ToArray();

    /// <summary>
    /// The curve as evaluated and drawn: the user's points ordered by temperature and closed with the
    /// min/max-speed anchors. An empty curve is the bare anchor ramp, idle to full speed across the domain.
    /// </summary>
    public static List<(double Temperature, double Duty)> BuildAnchoredSeries(IEnumerable<(int Temperature, double Duty)> curvePoints)
    {
        ArgumentNullException.ThrowIfNull(curvePoints);

        var ordered = curvePoints
            .OrderBy(static point => point.Temperature)
            .Select(static point => ((double)point.Temperature, point.Duty))
            .ToList();

        if (ordered.Count == 0)
        {
            return [(MinTemperatureCelsius, MinSpeedDutyPercent), (MaxTemperatureCelsius, MaxSpeedDutyPercent)];
        }

        List<(double Temperature, double Duty)> series = [];
        if (ordered[0].Item1 > MinTemperatureCelsius)
        {
            series.Add((MinTemperatureCelsius, MinSpeedDutyPercent));
        }

        series.AddRange(ordered);

        if (ordered[^1].Item1 < MaxTemperatureCelsius)
        {
            series.Add((MaxTemperatureCelsius, MaxSpeedDutyPercent));
        }

        return series;
    }

    /// <summary>
    /// The duty (0–100 %) a curve targets at <paramref name="temperatureCelsius"/>, interpolated linearly
    /// between <see cref="BuildAnchoredSeries"/> vertices.
    /// <para>
    /// THE single implementation: the client's predicted-duty readout and the service worker that actually
    /// writes duty to the EC both call this. Two copies of this rule would let a preview promise one speed
    /// while the fan does another.
    /// </para>
    /// </summary>
    public static double InterpolateDuty(IEnumerable<(int Temperature, double Duty)> curvePoints, double temperatureCelsius)
    {
        var series = BuildAnchoredSeries(curvePoints);

        var first = series[0];
        if (temperatureCelsius <= first.Temperature)
        {
            return ClampDuty(first.Duty);
        }

        var last = series[^1];
        if (temperatureCelsius >= last.Temperature)
        {
            return ClampDuty(last.Duty);
        }

        for (var i = 1; i < series.Count; i++)
        {
            var lower = series[i - 1];
            var upper = series[i];
            if (temperatureCelsius > upper.Temperature)
            {
                continue;
            }

            // Two points on the same degree (the user dragged one onto another): the later one wins rather
            // than dividing by a zero span.
            if (upper.Temperature <= lower.Temperature)
            {
                return ClampDuty(upper.Duty);
            }

            var ratio = (temperatureCelsius - lower.Temperature) / (upper.Temperature - lower.Temperature);
            return ClampDuty(lower.Duty + (ratio * (upper.Duty - lower.Duty)));
        }

        return ClampDuty(last.Duty);
    }
}
