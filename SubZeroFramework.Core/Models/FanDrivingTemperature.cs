namespace SubZeroFramework.Models;

/// <summary>
/// Reduces a curve's selected driving sensors to the single temperature that drives it.
/// <para>
/// THE single implementation: the client's predicted duty / operating-point readout and the service worker
/// that actually writes duty both call this. Two copies let the preview disagree with the fan — which is
/// exactly what happened before it existed (the client skipped unreadable sensors while the service silently
/// folded their 0 °C into the average).
/// </para>
/// <para>
/// A sensor with no reading — powered down, erroring, uncalibrated, or gone from the fleet — is either
/// skipped or counted as 0 °C, per the profile's <c>treatMissingAsZero</c> choice. Skipping keeps the
/// remaining sensors honest but leaves a fan blind when they are all dark; counting as 0 °C suits sensors
/// that are absent precisely because the thing they measure is switched off (a sleeping GPU), where "no
/// reading" genuinely means "no heat".
/// </para>
/// </summary>
public static class FanDrivingTemperature
{
    /// <summary>The temperature a curve should be evaluated at, or null when nothing usable contributed.</summary>
    /// <param name="readings">
    /// One entry per SELECTED sensor, in selection order; null where that sensor currently has no reading.
    /// Callers pass null rather than dropping the entry so <paramref name="treatMissingAsZero"/> can see it.
    /// </param>
    /// <param name="aggregation">How multiple readings combine.</param>
    /// <param name="treatMissingAsZero">
    /// When true a missing reading contributes 0 °C instead of being skipped. With Maximum aggregation that
    /// makes a dark sensor harmless (0 never wins); with Average or Minimum it actively drags the driving
    /// temperature down, so the UI warns about that combination.
    /// </param>
    public static double? Aggregate(
        IEnumerable<double?> readings,
        TemperatureAggregationMode aggregation,
        bool treatMissingAsZero)
    {
        ArgumentNullException.ThrowIfNull(readings);

        List<double> contributing = [];
        foreach (var reading in readings)
        {
            if (reading is double celsius)
            {
                contributing.Add(celsius);
            }
            else if (treatMissingAsZero)
            {
                contributing.Add(0d);
            }
        }

        if (contributing.Count == 0)
        {
            return null;
        }

        return aggregation switch
        {
            TemperatureAggregationMode.Average => contributing.Average(),
            TemperatureAggregationMode.Minimum => contributing.Min(),
            TemperatureAggregationMode.Median => Median(contributing),
            _ => contributing.Max(),
        };
    }

    /// <summary>Median of the readings. Mutates (sorts) the supplied list, which must be non-empty.</summary>
    public static double Median(List<double> readings)
    {
        ArgumentNullException.ThrowIfNull(readings);

        readings.Sort();
        var middle = readings.Count / 2;
        return readings.Count % 2 == 0 ? (readings[middle - 1] + readings[middle]) / 2d : readings[middle];
    }
}
