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

    /// <summary>Coldest editable point. Must stay above <see cref="ChartMinTemperatureCelsius"/>.</summary>
    public const int EditableMinTemperatureCelsius = 15;

    /// <summary>Hottest editable point. Must stay below <see cref="ChartMaxTemperatureCelsius"/>.</summary>
    public const int EditableMaxTemperatureCelsius = 120;

    // ----- The curve chart's visible window -----
    //
    // Derived from the editable band rather than written out again, because the two must not drift: the
    // window has to be WIDER than the editable band (so a point clamped to the band is always grabbable)
    // and NARROWER than the domain (so the 0 °C / 130 °C anchors stay off-screen and are never mistaken
    // for draggable points). Both invariants used to rest on a "keep the two in sync" comment.
    //
    // CANONICAL Celsius and percent. The curve chart plots in canonical space and CONVERTS in its labeler,
    // so these bounds must not be converted — doing so would move the axis out from under the series.

    /// <summary>Headroom between the editable band and the visible window, in degrees.</summary>
    private const int ChartTemperatureHeadroomCelsius = 5;

    /// <summary>Left edge of the curve chart's temperature axis.</summary>
    public const int ChartMinTemperatureCelsius = EditableMinTemperatureCelsius - ChartTemperatureHeadroomCelsius;

    /// <summary>Right edge of the curve chart's temperature axis.</summary>
    public const int ChartMaxTemperatureCelsius = EditableMaxTemperatureCelsius + ChartTemperatureHeadroomCelsius;

    /// <summary>Temperature axis tick spacing, in degrees.</summary>
    public const int ChartTemperatureStepCelsius = 10;

    /// <summary>Bottom of the duty axis — below 0% so the idle anchor is not clipped to the frame.</summary>
    public const double ChartMinDutyPercent = MinSpeedDutyPercent - 10d;

    /// <summary>Top of the duty axis — above 100% so the full-speed anchor is not clipped to the frame.</summary>
    public const double ChartMaxDutyPercent = MaxSpeedDutyPercent + 12d;

    /// <summary>Duty axis tick spacing, in percent.</summary>
    public const double ChartDutyStepPercent = 20d;

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

        // Points at or beyond the top of the domain are DISCARDED rather than kept, and the full-speed anchor
        // is then appended unconditionally. The anchor used to be conditional on the last point sitting below
        // MaxTemperatureCelsius, which meant a single point at or above it removed the backstop entirely: a
        // curve of {0:0, 1000000:0} evaluated to 0% at every temperature, leaving nothing between the fan and
        // the firmware's critical-temperature shutdown. The editor cannot produce such a point (it clamps to
        // EditableMaxTemperatureCelsius) and the RPC boundary now rejects one, but this is the single
        // evaluation path for both the client readout and the worker that writes the EC, so it enforces the
        // guarantee its own summary makes — always full speed by the top of the domain — rather than assuming
        // its callers already did. A persisted or hand-edited curve from an older build reaches here too.
        var ordered = curvePoints
            .Where(static point => point.Temperature < MaxTemperatureCelsius)
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
        series.Add((MaxTemperatureCelsius, MaxSpeedDutyPercent));

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
