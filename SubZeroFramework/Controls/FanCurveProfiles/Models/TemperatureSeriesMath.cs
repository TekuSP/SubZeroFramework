using SubZeroFramework.Models;

namespace SubZeroFramework.Controls.FanCurveProfiles.Models;

/// <summary>
/// Pure helpers shared by the Fan Control temperature visuals: nearest-sample lookup and median. Used by the
/// per-fan driving-temperature sparkline, the predicted-duty readout, and the driving-temperature chart, so
/// they live in one place instead of being copied across those code paths.
/// </summary>
internal static class TemperatureSeriesMath
{
    /// <summary>
    /// Returns the value of the sample nearest <paramref name="timestamp"/>, or null when the series is empty.
    /// Linear scan — telemetry history windows are short, so this is cheap and avoids a BCL bisect.
    /// </summary>
    public static double? FindNearestValue(TelemetryPoint[] series, DateTimeOffset timestamp)
    {
        double? best = null;
        var bestDelta = TimeSpan.MaxValue;
        foreach (var point in series)
        {
            var delta = (point.ObservedAt - timestamp).Duration();
            if (delta < bestDelta)
            {
                bestDelta = delta;
                best = point.NumericValue;
            }
        }

        return best;
    }

    /// <summary>Median of the readings. Mutates (sorts) the supplied list. The list must be non-empty.</summary>
    public static double Median(List<double> readings)
    {
        readings.Sort();
        var mid = readings.Count / 2;
        return readings.Count % 2 == 0 ? (readings[mid - 1] + readings[mid]) / 2d : readings[mid];
    }
}
