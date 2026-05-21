using System.Collections.ObjectModel;

using FrameworkDotnet.Enums;

using CommunityToolkit.Mvvm.ComponentModel;

using LiveChartsCore.Defaults;

using Microsoft.UI.Xaml.Media;

using SubZeroFramework.Themes;

namespace SubZeroFramework.Controls.Thermal.Models;

public partial class ThermalSensorModel : ObservableObject
{
	private static readonly string[] HistoryStrokePalette =
	[
		AppThemeBrushes.ChartAccentColorHex,
		"#FF6CB0FF",
		"#FF9A8CFF",
		"#FF78C6A3",
		"#FFE8B86C",
		"#FFFF8A80",
	];
	private readonly ObservableValue _coolGaugeObservable = new(0d);
	private readonly ObservableValue _normalGaugeObservable = new(0d);
	private readonly ObservableValue _warmGaugeObservable = new(0d);
	private readonly ObservableValue _hotGaugeObservable = new(0d);
	private readonly ObservableValue _remainingGaugeObservable = new(100d);

	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(DisplayName))]
	[NotifyPropertyChangedFor(nameof(TemperatureValueDisplay))]
	[NotifyPropertyChangedFor(nameof(TemperatureValueWithUnit))]
	[NotifyPropertyChangedFor(nameof(SelectionDisplay))]
	[NotifyPropertyChangedFor(nameof(HistoryStrokeHex))]
	public partial TemperatureTelemetrySnapshot Snapshot { get; set; } = default!;

	public ObservableCollection<DateTimePoint> TemperatureHistory { get; } = [];

	public ObservableCollection<DateTimePoint> OverviewTemperatureHistory { get; } = [];

	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(OverviewTemperatureHistory))]
	[NotifyPropertyChangedFor(nameof(TemperatureHistory))]
	public partial int HistoryRevision { get; set; }

	[ObservableProperty]
	public partial double[] Separators { get; set; } = [];

	[ObservableProperty]
	public partial bool IsSelected { get; set; } = true;

	[ObservableProperty]
	public partial string StatusText { get; set; } = "Status: Checking";

	[ObservableProperty]
	public partial Brush StatusBrush { get; set; } = AppThemeBrushes.Get("StatusWarningBrush", AppThemeBrushes.StatusWarningColor);

	[ObservableProperty]
	public partial Brush TemperatureBrush { get; set; } = AppThemeBrushes.Get("TextPrimaryBrush", AppThemeBrushes.TextPrimaryColor);

	public ThermalSensorModel()
	{
		CoolGaugeValues = [_coolGaugeObservable];
		NormalGaugeValues = [_normalGaugeObservable];
		WarmGaugeValues = [_warmGaugeObservable];
		HotGaugeValues = [_hotGaugeObservable];
		RemainingGaugeValues = [_remainingGaugeObservable];
	}

	public Func<DateTime, string> LabelsFormatter { get; } = Formatter;

	public string DisplayName => string.IsNullOrWhiteSpace(Snapshot.DisplayName)
		? $"Sensor {Snapshot.SensorIndex + 1}"
		: Snapshot.DisplayName;

	public string TemperatureValueDisplay => ShouldDisplayMeasuredTemperature && Snapshot.TemperatureCelsius is double value
		? $"{value:N0}"
		: "--";

	public string TemperatureValueWithUnit => $"{TemperatureValueDisplay}°C";

	public string SelectionDisplay => $"{DisplayName}: {TemperatureValueWithUnit}";

	public double GaugeValue => ShouldDisplayMeasuredTemperature
		? Math.Clamp(Snapshot.TemperatureCelsius ?? 0d, 0d, 100d)
		: 0d;

	public double CoolGaugeValue => GetGaugeSegmentValue(0d, 45d);

	public ObservableValue[] CoolGaugeValues { get; }

	public double NormalGaugeValue => GetGaugeSegmentValue(45d, 70d);

	public ObservableValue[] NormalGaugeValues { get; }

	public double WarmGaugeValue => GetGaugeSegmentValue(70d, 85d);

	public ObservableValue[] WarmGaugeValues { get; }

	public double HotGaugeValue => GetGaugeSegmentValue(85d, 100d);

	public ObservableValue[] HotGaugeValues { get; }

	public double RemainingGaugeValue => Math.Max(0d, 100d - GaugeValue);

	public ObservableValue[] RemainingGaugeValues { get; }

	public string HistoryStrokeHex => HistoryStrokePalette[Math.Abs(Snapshot.SensorIndex) % HistoryStrokePalette.Length];

	partial void OnSnapshotChanged(TemperatureTelemetrySnapshot value)
	{
		UpdateGaugeValues();
		UpdatePresentation();
	}

	private void UpdatePresentation()
	{
		if (!Snapshot.IsAvailable)
		{
			StatusText = "Status: Unavailable";
			StatusBrush = AppThemeBrushes.Get("StatusErrorBrush", AppThemeBrushes.StatusErrorColor);
			TemperatureBrush = AppThemeBrushes.Get("TextSecondaryBrush", AppThemeBrushes.TextSecondaryColor);
			return;
		}

		var temperatureState = Snapshot.TemperatureState;

		switch (temperatureState)
		{
			case FrameworkTemperatureState.NotPresent:
				StatusText = "Status: Not Present";
					StatusBrush = AppThemeBrushes.Get("StatusErrorBrush", AppThemeBrushes.StatusErrorColor);
					TemperatureBrush = AppThemeBrushes.Get("TextSecondaryBrush", AppThemeBrushes.TextSecondaryColor);
				return;
			case FrameworkTemperatureState.NotPowered:
				StatusText = "Status: Not Powered";
						StatusBrush = AppThemeBrushes.Get("BrandDisabledBrush", AppThemeBrushes.BrandDisabledColor);
						TemperatureBrush = AppThemeBrushes.Get("BrandDisabledBrush", AppThemeBrushes.BrandDisabledColor);
				return;
			case FrameworkTemperatureState.NotCalibrated:
				StatusText = "Status: Not Calibrated";
					StatusBrush = AppThemeBrushes.Get("StatusWarningBrush", AppThemeBrushes.StatusWarningColor);
					TemperatureBrush = AppThemeBrushes.Get("TextSecondaryBrush", AppThemeBrushes.TextSecondaryColor);
				return;
			case FrameworkTemperatureState.Error:
				StatusText = "Status: Error";
					StatusBrush = AppThemeBrushes.Get("StatusErrorBrush", AppThemeBrushes.StatusErrorColor);
					TemperatureBrush = AppThemeBrushes.Get("StatusErrorBrush", AppThemeBrushes.StatusErrorColor);
				return;
		}

		if (Snapshot.TemperatureCelsius is not double temperature)
		{
			StatusText = "Status: Checking";
			StatusBrush = AppThemeBrushes.Get("StatusWarningBrush", AppThemeBrushes.StatusWarningColor);
			TemperatureBrush = AppThemeBrushes.Get("TextPrimaryBrush", AppThemeBrushes.TextPrimaryColor);
			return;
		}

		StatusText = "Status: OK";
		StatusBrush = AppThemeBrushes.Get("StatusSuccessBrush", AppThemeBrushes.StatusSuccessColor);

		if (temperature < 45d)
		{
			TemperatureBrush = AppThemeBrushes.Get("BrandPrimaryBrush", AppThemeBrushes.TemperatureAccentColor);
			return;
		}

		if (temperature < 70d)
		{
			TemperatureBrush = AppThemeBrushes.Get("TextPrimaryBrush", AppThemeBrushes.TextPrimaryColor);
			return;
		}

		if (temperature < 85d)
		{
			TemperatureBrush = AppThemeBrushes.Get("StatusWarningBrush", AppThemeBrushes.StatusWarningColor);
			return;
		}

		TemperatureBrush = AppThemeBrushes.Get("StatusErrorBrush", AppThemeBrushes.StatusErrorColor);
	}

	private void UpdateGaugeValues()
	{
		_coolGaugeObservable.Value = CoolGaugeValue;
		_normalGaugeObservable.Value = NormalGaugeValue;
		_warmGaugeObservable.Value = WarmGaugeValue;
		_hotGaugeObservable.Value = HotGaugeValue;
		_remainingGaugeObservable.Value = RemainingGaugeValue;
	}

	public void ClearTemperatureHistory()
	{
		SynchronizePoints(OverviewTemperatureHistory, []);
		SynchronizePoints(TemperatureHistory, []);
		Separators = [];
		HistoryRevision++;
	}

	public void UpdateTemperatureHistory(IReadOnlyList<DateTimePoint> overviewHistory, IReadOnlyList<DateTimePoint> cardHistory)
	{
		SynchronizePoints(OverviewTemperatureHistory, overviewHistory);
		SynchronizePoints(TemperatureHistory, cardHistory);
		Separators = GetSeparators();
		HistoryRevision++;
	}

	private bool ShouldDisplayMeasuredTemperature => Snapshot.IsAvailable
		&& Snapshot.TemperatureCelsius is not null
		&& (Snapshot.TemperatureState is null || Snapshot.TemperatureState == FrameworkTemperatureState.Ok);

	private double GetGaugeSegmentValue(double startInclusive, double endExclusive)
	{
		var value = GaugeValue;
		if (value <= startInclusive)
		{
			return 0d;
		}

		return Math.Min(value, endExclusive) - startInclusive;
	}

	private static void SynchronizePoints(ObservableCollection<DateTimePoint> target, IReadOnlyList<DateTimePoint> source)
	{
		var targetIndex = 0;
		var sourceIndex = 0;

		while (targetIndex < target.Count && sourceIndex < source.Count)
		{
			var current = target[targetIndex];
			var next = source[sourceIndex];

			if (current.DateTime < next.DateTime)
			{
				target.RemoveAt(targetIndex);
				continue;
			}

			if (current.DateTime > next.DateTime)
			{
				target.Insert(targetIndex, next);
				targetIndex++;
				sourceIndex++;
				continue;
			}

			if (current.Value != next.Value)
			{
				target[targetIndex] = next;
			}

			targetIndex++;
			sourceIndex++;
		}

		while (targetIndex < target.Count)
		{
			target.RemoveAt(targetIndex);
		}

		for (; sourceIndex < source.Count; sourceIndex++)
		{
			target.Add(source[sourceIndex]);
		}
	}

	private static double[] GetSeparators()
	{
		var now = DateTime.Now;
		return TimeChartAxisHelper.BuildSeparators(
			now - PresentationDefaults.RecentTelemetryHistoryWindow,
			now,
			PresentationDefaults.RecentTelemetrySeparatorStep);
	}

	public static string Formatter(DateTime date)
	{
		var elapsed = DateTime.Now - date;

		if (elapsed.TotalSeconds < 1d)
		{
			return "now";
		}

		if (elapsed.TotalMinutes < 1d)
		{
			return $"{elapsed.TotalSeconds:N0}s";
		}

		if (elapsed.TotalHours < 1d)
		{
			return $"{elapsed.TotalMinutes:N0}m";
		}

		var hours = (int)Math.Floor(elapsed.TotalHours);
		var minutes = (int)Math.Round(elapsed.TotalMinutes - (hours * 60d), MidpointRounding.AwayFromZero);

		if (minutes == 60)
		{
			hours++;
			minutes = 0;
		}

		return minutes == 0
			? $"{hours}h"
			: $"{hours}h {minutes}m";
	}
}
