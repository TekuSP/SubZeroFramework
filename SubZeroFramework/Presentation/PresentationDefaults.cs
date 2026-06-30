namespace SubZeroFramework.Presentation;

public static class PresentationDefaults
{
    public static readonly TimeSpan RecentTelemetryHistoryWindow = TimeSpan.FromSeconds(30);

    public static readonly TimeSpan RecentTelemetrySeparatorStep = TimeSpan.FromSeconds(5);

    public static readonly TimeSpan ShortTelemetryHistorySeparatorStep = TimeSpan.FromSeconds(30);

    public static readonly TimeSpan MediumTelemetryHistorySeparatorStep = TimeSpan.FromMinutes(1);

    public static readonly TimeSpan LongTelemetryHistorySeparatorStep = TimeSpan.FromMinutes(5);

    public static readonly TimeSpan StandardTelemetryHistorySeparatorStep = TimeSpan.FromMinutes(15);

    public static string RecentTelemetryHistoryWindowLabel => FormatHistoryWindowLabel(RecentTelemetryHistoryWindow);

    public const double WarningUsagePercent = 75d;
    public const double ErrorUsagePercent = 90d;

    public static ImmutableArray<TimeSpan> ThermalHistoryWindowValues { get; } =
        ImmutableArray.Create(
            TimeSpan.FromMinutes(1),
            TimeSpan.FromMinutes(5),
            TimeSpan.FromMinutes(15),
            TelemetryHistoryLimits.MaximumHistoryWindow);

    public static ImmutableArray<string> ThermalHistoryWindowLabels { get; } =
        ImmutableArray.Create(
            "1 min",
            "5 min",
            "15 min",
            "1 hour");

    /// <summary>Default selected thermal history window — "5 min" (index 1).</summary>
    public const int DefaultThermalHistoryWindowIndex = 1;

    public static string FormatHistoryWindowLabel(TimeSpan window)
    {
        if (window.TotalSeconds < 1d)
        {
            return "Current sample";
        }

        if (window.TotalMinutes < 1d)
        {
            var seconds = Math.Max(1, (int)Math.Round(window.TotalSeconds, MidpointRounding.AwayFromZero));
            return $"Last {seconds} second{(seconds == 1 ? string.Empty : "s")}";
        }

        if (window.TotalHours < 1d)
        {
            var minutes = Math.Max(1, (int)Math.Round(window.TotalMinutes, MidpointRounding.AwayFromZero));
            return $"Last {minutes} minute{(minutes == 1 ? string.Empty : "s")}";
        }

        if (window.TotalDays < 1d)
        {
            var hours = Math.Max(1, (int)Math.Round(window.TotalHours, MidpointRounding.AwayFromZero));
            return $"Last {hours} hour{(hours == 1 ? string.Empty : "s")}";
        }

        var days = Math.Max(1, (int)Math.Round(window.TotalDays, MidpointRounding.AwayFromZero));
        return $"Last {days} day{(days == 1 ? string.Empty : "s")}";
    }
}
