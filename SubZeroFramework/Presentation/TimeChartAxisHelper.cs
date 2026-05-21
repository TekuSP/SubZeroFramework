namespace SubZeroFramework.Presentation;

public static class TimeChartAxisHelper
{
    public static (DateTime AxisStart, DateTime AxisEnd, double[] Separators) BuildAxis(
        IReadOnlyList<DateTime> historyPoints,
        TimeSpan historyWindow,
        TimeSpan longSpanSeparatorStep)
    {
        var axisEnd = historyPoints.Count == 0
            ? DateTime.Now
            : historyPoints[^1] > DateTime.Now ? historyPoints[^1] : DateTime.Now;

        var earliestPoint = historyPoints.Count == 0
            ? axisEnd - historyWindow
            : historyPoints[0];

        var axisStart = earliestPoint < axisEnd - historyWindow
            ? axisEnd - historyWindow
            : earliestPoint;

        var separatorStep = GetAdaptiveSeparatorStep(axisEnd - axisStart, longSpanSeparatorStep);
        return (axisStart, axisEnd, BuildSeparators(axisStart, axisEnd, separatorStep));
    }

    public static double[] BuildSeparators(DateTime axisStart, DateTime axisEnd, TimeSpan separatorStep)
    {
        List<double> separators = [axisStart.Ticks];
        for (var tick = axisStart + separatorStep; tick < axisEnd; tick += separatorStep)
        {
            separators.Add(tick.Ticks);
        }

        if (separators.Count == 0 || separators[^1] != axisEnd.Ticks)
        {
            separators.Add(axisEnd.Ticks);
        }

        return [.. separators];
    }

    public static TimeSpan GetAdaptiveSeparatorStep(TimeSpan visibleSpan, TimeSpan longSpanSeparatorStep)
    {
        if (visibleSpan <= TimeSpan.FromMinutes(1))
        {
            return PresentationDefaults.RecentTelemetrySeparatorStep;
        }

        if (visibleSpan <= TimeSpan.FromMinutes(5))
        {
            return PresentationDefaults.ShortTelemetryHistorySeparatorStep;
        }

        if (visibleSpan <= TimeSpan.FromMinutes(15))
        {
            return PresentationDefaults.MediumTelemetryHistorySeparatorStep;
        }

        if (visibleSpan <= TimeSpan.FromMinutes(30))
        {
            return PresentationDefaults.LongTelemetryHistorySeparatorStep;
        }

        return longSpanSeparatorStep;
    }
}
