namespace SubZeroFramework.Presentation;

public static class PresentationDefaults
{
    public static readonly TimeSpan RecentTelemetryHistoryWindow = TimeSpan.FromSeconds(30);

    public const double WarningUsagePercent = 75d;
    public const double ErrorUsagePercent = 90d;

    public static ImmutableArray<TimeSpan> ThermalHistoryWindowValues { get; } =
        ImmutableArray.Create(
            TimeSpan.FromMinutes(5),
            TimeSpan.FromMinutes(15),
            TelemetryHistoryLimits.MaximumHistoryWindow);

    public static ImmutableArray<string> ThermalHistoryWindowLabels { get; } =
        ImmutableArray.Create(
            "Last 5 minutes",
            "Last 15 minutes",
            "Last hour");
}
