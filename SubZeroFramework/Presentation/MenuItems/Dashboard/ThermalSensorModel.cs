using FrameworkDotnet.Enums;

using CommunityToolkit.Mvvm.ComponentModel;

using LiveChartsCore.Defaults;

using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace SubZeroFramework.Presentation.MenuItems.Dashboard;

public partial class ThermalSensorModel : ObservableObject
{
	private static readonly string[] HistoryStrokePalette =
	[
		"#FF8AB7E8",
		"#FF6CB0FF",
		"#FF9A8CFF",
		"#FF78C6A3",
		"#FFE8B86C",
		"#FFFF8A80",
	];

	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(DisplayName))]
	[NotifyPropertyChangedFor(nameof(TemperatureValueDisplay))]
	[NotifyPropertyChangedFor(nameof(TemperatureValueWithUnit))]
	[NotifyPropertyChangedFor(nameof(SelectionDisplay))]
	[NotifyPropertyChangedFor(nameof(ShouldShowByDefault))]
	[NotifyPropertyChangedFor(nameof(GaugeValue))]
	[NotifyPropertyChangedFor(nameof(CoolGaugeValue))]
	[NotifyPropertyChangedFor(nameof(CoolGaugeValues))]
	[NotifyPropertyChangedFor(nameof(NormalGaugeValue))]
	[NotifyPropertyChangedFor(nameof(NormalGaugeValues))]
	[NotifyPropertyChangedFor(nameof(WarmGaugeValue))]
	[NotifyPropertyChangedFor(nameof(WarmGaugeValues))]
	[NotifyPropertyChangedFor(nameof(HotGaugeValue))]
	[NotifyPropertyChangedFor(nameof(HotGaugeValues))]
	[NotifyPropertyChangedFor(nameof(RemainingGaugeValue))]
	[NotifyPropertyChangedFor(nameof(RemainingGaugeValues))]
	[NotifyPropertyChangedFor(nameof(HistoryStrokeHex))]
	public partial TemperatureTelemetrySnapshot Snapshot { get; set; } = default!;

	[ObservableProperty]
	public partial DateTimePoint[] TemperatureHistory { get; set; } = [];

	[ObservableProperty]
	public partial DateTimePoint[] OverviewTemperatureHistory { get; set; } = [];

	[ObservableProperty]
	public partial double[] Separators { get; set; } = [];

	[ObservableProperty]
	public partial bool IsSelected { get; set; } = true;

	[ObservableProperty]
	public partial string StatusText { get; set; } = "Status: Checking";

	[ObservableProperty]
	public partial Brush StatusBrush { get; set; } = GetBrush("StatusWarningBrush", ColorHelper.FromArgb(255, 197, 153, 78));

	[ObservableProperty]
	public partial Brush TemperatureBrush { get; set; } = GetBrush("TextPrimaryBrush", ColorHelper.FromArgb(255, 215, 216, 255));

	public Func<DateTime, string> LabelsFormatter { get; } = Formatter;

	public string DisplayName => string.IsNullOrWhiteSpace(Snapshot.DisplayName)
		? $"Sensor {Snapshot.SensorIndex + 1}"
		: Snapshot.DisplayName;

	public string TemperatureValueDisplay => ShouldDisplayMeasuredTemperature && Snapshot.TemperatureCelsius is double value
		? $"{value:N0}"
		: "--";

	public string TemperatureValueWithUnit => $"{TemperatureValueDisplay}°C";

	public string SelectionDisplay => $"{DisplayName}: {TemperatureValueWithUnit}";

	public bool ShouldShowByDefault => Snapshot.IsAvailable
		&& Snapshot.TemperatureState is not FrameworkTemperatureState.NotPresent
		&& Snapshot.TemperatureState is not FrameworkTemperatureState.NotPowered
		&& Snapshot.TemperatureState is not FrameworkTemperatureState.NotCalibrated;

	public double GaugeValue => ShouldDisplayMeasuredTemperature
		? Math.Clamp(Snapshot.TemperatureCelsius ?? 0d, 0d, 100d)
		: 0d;

	public double CoolGaugeValue => GetGaugeSegmentValue(0d, 45d);

	public double[] CoolGaugeValues => [CoolGaugeValue];

	public double NormalGaugeValue => GetGaugeSegmentValue(45d, 70d);

	public double[] NormalGaugeValues => [NormalGaugeValue];

	public double WarmGaugeValue => GetGaugeSegmentValue(70d, 85d);

	public double[] WarmGaugeValues => [WarmGaugeValue];

	public double HotGaugeValue => GetGaugeSegmentValue(85d, 100d);

	public double[] HotGaugeValues => [HotGaugeValue];

	public double RemainingGaugeValue => Math.Max(0d, 100d - GaugeValue);

	public double[] RemainingGaugeValues => [RemainingGaugeValue];

	public string HistoryStrokeHex => HistoryStrokePalette[Math.Abs(Snapshot.SensorIndex) % HistoryStrokePalette.Length];

	partial void OnSnapshotChanged(TemperatureTelemetrySnapshot value)
	{
		UpdatePresentation();
	}

	partial void OnTemperatureHistoryChanged(DateTimePoint[] value)
	{
		Separators = GetSeparators(value.LastOrDefault());
	}

	private void UpdatePresentation()
	{
		if (!Snapshot.IsAvailable)
		{
			StatusText = "Status: Unavailable";
			StatusBrush = GetBrush("StatusErrorBrush", ColorHelper.FromArgb(255, 68, 39, 38));
			TemperatureBrush = GetBrush("TextSecondaryBrush", ColorHelper.FromArgb(255, 160, 163, 186));
			return;
		}

		var temperatureState = Snapshot.TemperatureState;

		switch (temperatureState)
		{
			case FrameworkTemperatureState.NotPresent:
				StatusText = "Status: Not Present";
				StatusBrush = GetBrush("StatusErrorBrush", ColorHelper.FromArgb(255, 68, 39, 38));
				TemperatureBrush = GetBrush("TextSecondaryBrush", ColorHelper.FromArgb(255, 160, 163, 186));
				return;
			case FrameworkTemperatureState.NotPowered:
				StatusText = "Status: Not Powered";
				StatusBrush = GetBrush("StatusWarningBrush", ColorHelper.FromArgb(255, 197, 153, 78));
				TemperatureBrush = GetBrush("TextSecondaryBrush", ColorHelper.FromArgb(255, 160, 163, 186));
				return;
			case FrameworkTemperatureState.NotCalibrated:
				StatusText = "Status: Not Calibrated";
				StatusBrush = GetBrush("StatusWarningBrush", ColorHelper.FromArgb(255, 197, 153, 78));
				TemperatureBrush = GetBrush("TextSecondaryBrush", ColorHelper.FromArgb(255, 160, 163, 186));
				return;
			case FrameworkTemperatureState.Error:
				StatusText = "Status: Error";
				StatusBrush = GetBrush("StatusErrorBrush", ColorHelper.FromArgb(255, 68, 39, 38));
				TemperatureBrush = GetBrush("StatusErrorBrush", ColorHelper.FromArgb(255, 68, 39, 38));
				return;
		}

		if (Snapshot.TemperatureCelsius is not double temperature)
		{
			StatusText = "Status: Checking";
			StatusBrush = GetBrush("StatusWarningBrush", ColorHelper.FromArgb(255, 197, 153, 78));
			TemperatureBrush = GetBrush("TextPrimaryBrush", ColorHelper.FromArgb(255, 215, 216, 255));
			return;
		}

		StatusText = "Status: OK";
		StatusBrush = GetBrush("StatusSuccessBrush", ColorHelper.FromArgb(255, 108, 203, 95));

		if (temperature < 45d)
		{
			TemperatureBrush = GetBrush("BrandPrimaryBrush", ColorHelper.FromArgb(255, 138, 183, 232));
			return;
		}

		if (temperature < 70d)
		{
			TemperatureBrush = GetBrush("TextPrimaryBrush", ColorHelper.FromArgb(255, 215, 216, 255));
			return;
		}

		if (temperature < 85d)
		{
			TemperatureBrush = GetBrush("StatusWarningBrush", ColorHelper.FromArgb(255, 197, 153, 78));
			return;
		}

		TemperatureBrush = GetBrush("StatusErrorBrush", ColorHelper.FromArgb(255, 68, 39, 38));
	}

	private bool ShouldDisplayMeasuredTemperature => Snapshot.IsAvailable
		&& Snapshot.TemperatureCelsius is not null
		&& (Snapshot.TemperatureState is null || Snapshot.TemperatureState == FrameworkTemperatureState.Ok);

	private static Brush GetBrush(string resourceKey, Windows.UI.Color fallbackColor)
	{
		return Application.Current?.Resources.TryGetValue(resourceKey, out var resource) == true
			&& resource is Brush brush
				? brush
				: new SolidColorBrush(fallbackColor);
	}

	private double GetGaugeSegmentValue(double startInclusive, double endExclusive)
	{
		var value = GaugeValue;
		if (value <= startInclusive)
		{
			return 0d;
		}

		return Math.Min(value, endExclusive) - startInclusive;
	}

	private static double[] GetSeparators(DateTimePoint? lastPoint)
	{
		if (lastPoint is null)
		{
			return [];
		}

		var end = lastPoint.DateTime;

		return
		[
			end.AddMinutes(-15).Ticks,
			end.AddMinutes(-10).Ticks,
			end.AddMinutes(-5).Ticks,
			end.Ticks,
		];
	}

	public static string Formatter(DateTime date)
	{
		var minutesAgo = (DateTime.Now - date).TotalMinutes;

		return minutesAgo < 1d
			? "now"
			: $"{minutesAgo:N0}m";
	}
}
