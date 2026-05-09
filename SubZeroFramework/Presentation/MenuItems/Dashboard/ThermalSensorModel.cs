using System.Collections.ObjectModel;

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
	public partial double[] Separators { get; set; } = [];

	[ObservableProperty]
	public partial bool IsSelected { get; set; } = true;

	[ObservableProperty]
	public partial string StatusText { get; set; } = "Status: Checking";

	[ObservableProperty]
	public partial Brush StatusBrush { get; set; } = GetBrush("StatusWarningBrush", ColorHelper.FromArgb(255, 197, 153, 78));

	[ObservableProperty]
	public partial Brush TemperatureBrush { get; set; } = GetBrush("TextPrimaryBrush", ColorHelper.FromArgb(255, 215, 216, 255));

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
					StatusBrush = GetBrush("BrandDisabledBrush", ColorHelper.FromArgb(255, 74, 76, 89));
					TemperatureBrush = GetBrush("BrandDisabledBrush", ColorHelper.FromArgb(255, 74, 76, 89));
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
		OnPropertyChanged(nameof(OverviewTemperatureHistory));
		OnPropertyChanged(nameof(TemperatureHistory));
	}

	public void UpdateTemperatureHistory(IReadOnlyList<DateTimePoint> overviewHistory, IReadOnlyList<DateTimePoint> cardHistory)
	{
		SynchronizePoints(OverviewTemperatureHistory, overviewHistory);
		SynchronizePoints(TemperatureHistory, cardHistory);
		Separators = GetSeparators();
		OnPropertyChanged(nameof(OverviewTemperatureHistory));
		OnPropertyChanged(nameof(TemperatureHistory));
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

	private static void SynchronizePoints(ObservableCollection<DateTimePoint> target, IReadOnlyList<DateTimePoint> source)
	{
		var commonCount = Math.Min(target.Count, source.Count);

		for (var index = 0; index < commonCount; index++)
		{
			var current = target[index];
			var next = source[index];

			if (current.DateTime != next.DateTime || current.Value != next.Value)
			{
				target[index] = next;
			}
		}

		for (var index = target.Count - 1; index >= source.Count; index--)
		{
			target.RemoveAt(index);
		}

		for (var index = commonCount; index < source.Count; index++)
		{
			target.Add(source[index]);
		}
	}

	private static double[] GetSeparators()
	{
		var now = DateTime.Now;

		return
		[
            now.AddSeconds(-30).Ticks,
            now.AddSeconds(-25).Ticks,
            now.AddSeconds(-20).Ticks,
            now.AddSeconds(-15).Ticks,
            now.AddSeconds(-10).Ticks,
            now.AddSeconds(-5).Ticks,
            now.Ticks
        ];
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
