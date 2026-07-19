using System.Collections.ObjectModel;

using FrameworkDotnet.Enums;

using CommunityToolkit.Mvvm.ComponentModel;

using LiveChartsCore.Defaults;

using Material.Icons;

using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

using SubZeroFramework.Services;
using SubZeroFramework.Services.Units;
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
	private readonly IUnitFormattingService _unitFormattingService;

	public ThermalSensorModel(IUnitFormattingService unitFormattingService)
	{
		_unitFormattingService = unitFormattingService;
		CoolGaugeValues = [_coolGaugeObservable];
		NormalGaugeValues = [_normalGaugeObservable];
		WarmGaugeValues = [_warmGaugeObservable];
		HotGaugeValues = [_hotGaugeObservable];
		RemainingGaugeValues = [_remainingGaugeObservable];

		// Snapshot is seated by the object initializer right after construction, which runs the full
		// RefreshUnitFormatting pass via OnSnapshotChanged; seed the service-derived text so the
		// stored properties are never null in the interim.
		TemperatureValueWithUnit = _unitFormattingService.FormatTemperature(null);
		TemperatureUnitSuffix = _unitFormattingService.TemperatureUnitSuffix;
		TemperatureAxisMinLimit = _unitFormattingService.ConvertTemperature(0d);
		TemperatureAxisMaxLimit = _unitFormattingService.ConvertTemperature(100d);
	}

	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(DisplayName))]
	[NotifyPropertyChangedFor(nameof(CardTitle))]
	[NotifyPropertyChangedFor(nameof(LocationDisplay))]
	[NotifyPropertyChangedFor(nameof(HasLocation))]
	[NotifyPropertyChangedFor(nameof(LocationVisibility))]
	[NotifyPropertyChangedFor(nameof(HistoryStrokeHex))]
	[NotifyPropertyChangedFor(nameof(SeriesBrush))]
	[NotifyPropertyChangedFor(nameof(PlottedIndicatorBrush))]
	[NotifyPropertyChangedFor(nameof(StatusIconKind))]
	public partial TemperatureTelemetrySnapshot Snapshot { get; set; } = default!;

	public ObservableCollection<DateTimePoint> TemperatureHistory { get; } = [];

	public ObservableCollection<DateTimePoint> OverviewTemperatureHistory { get; } = [];

	[ObservableProperty]
	public partial double[] Separators { get; set; } = [];

	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(PlottedIndicatorBrush))]
	[NotifyPropertyChangedFor(nameof(CardOpacity))]
	public partial bool IsSelected { get; set; } = true;

	/// <summary>Plotted cards render at full opacity; unplotted ones dim slightly (matches the design).</summary>
	public double CardOpacity => IsSelected ? 1d : 0.6d;

	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(StatusShortText))]
	public partial string StatusText { get; set; } = "Status: Checking";

	[ObservableProperty]
	public partial Brush StatusBrush { get; set; } = AppThemeBrushes.Get("StatusWarningBrush", AppThemeBrushes.StatusWarningColor);

	[ObservableProperty]
	public partial Brush TemperatureBrush { get; set; } = AppThemeBrushes.Get("TextPrimaryBrush", AppThemeBrushes.TextPrimaryColor);

	public Func<DateTime, string> LabelsFormatter { get; } = Formatter;

	public string DisplayName => string.IsNullOrWhiteSpace(Snapshot.DisplayName)
		? $"Sensor {Snapshot.SensorIndex + 1}"
		: Snapshot.DisplayName;

	/// <summary>Card/legend title — index-based "Sensor N" (the descriptive role is shown as the location).</summary>
	public string CardTitle => $"Sensor {Snapshot.SensorIndex}";

	/// <summary>Short platform-role location shown beneath the title (e.g. "APU / SoC"); null when unidentified.</summary>
	public string? LocationDisplay => FrameworkSensorNameDisplay.ToLocation(Snapshot.SensorName);

	public bool HasLocation => !string.IsNullOrEmpty(LocationDisplay);

	public Visibility LocationVisibility => HasLocation ? Visibility.Visible : Visibility.Collapsed;

	// Unit-formatted displays are stored and reassigned (RefreshUnitFormattedDisplays) whenever the
	// snapshot or the unit preference changes; the setters raise PropertyChanged only on a real change.
	[ObservableProperty]
	public partial string TemperatureValueDisplay { get; private set; } = "--";

	[ObservableProperty]
	public partial string TemperatureValueWithUnit { get; private set; } = string.Empty;

	[ObservableProperty]
	public partial string TemperatureUnitSuffix { get; private set; } = string.Empty;

	[ObservableProperty]
	public partial string SelectionDisplay { get; private set; } = string.Empty;

	// The card's history chart plots the sensor's temperature in the display unit, so the fixed 0–100 °C
	// Y-axis band converts through UnitsNet (0/100 °C → 32/212 °F, 273/373 K, …). Reassigned on unit change.
	[ObservableProperty]
	public partial double TemperatureAxisMinLimit { get; private set; }

	[ObservableProperty]
	public partial double TemperatureAxisMaxLimit { get; private set; }

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

	/// <summary>The sensor's chart-series colour, used for the card's top stripe, the plotted dot and the legend swatch.</summary>
	public Brush SeriesBrush => BrushFromHex(HistoryStrokeHex);

	/// <summary>Series colour when plotted, muted grey when not — drives the top stripe and the plotted dot.</summary>
	public Brush PlottedIndicatorBrush => IsSelected
		? SeriesBrush
		: AppThemeBrushes.Get("BrandDisabledBrush", AppThemeBrushes.BrandDisabledColor);

	/// <summary>Status text without the "Status: " prefix (e.g. "OK"), for the compact card footer.</summary>
	public string StatusShortText => StatusText.StartsWith("Status: ", StringComparison.Ordinal)
		? StatusText["Status: ".Length..]
		: StatusText;

	/// <summary>Status glyph: a check for healthy sensors, otherwise a caution/error mark.</summary>
	public MaterialIconKind StatusIconKind
	{
		get
		{
			if (!Snapshot.IsAvailable)
			{
				return MaterialIconKind.CloseCircleOutline;
			}

			return Snapshot.TemperatureState switch
			{
				FrameworkTemperatureState.NotPresent => MaterialIconKind.CloseCircleOutline,
				FrameworkTemperatureState.Error => MaterialIconKind.AlertCircleOutline,
				FrameworkTemperatureState.NotCalibrated => MaterialIconKind.AlertCircleOutline,
				FrameworkTemperatureState.NotPowered => MaterialIconKind.AlertCircleOutline,
				_ => MaterialIconKind.CheckCircle,
			};
		}
	}

	private static SolidColorBrush BrushFromHex(string hex)
	{
		var value = hex.TrimStart('#');
		var alpha = Convert.ToByte(value.Substring(0, 2), 16);
		var red = Convert.ToByte(value.Substring(2, 2), 16);
		var green = Convert.ToByte(value.Substring(4, 2), 16);
		var blue = Convert.ToByte(value.Substring(6, 2), 16);
		return new SolidColorBrush(ColorHelper.FromArgb(alpha, red, green, blue));
	}

	public void RefreshUnitFormatting()
	{
		RefreshUnitFormattedDisplays();
		TemperatureAxisMinLimit = _unitFormattingService.ConvertTemperature(0d);
		TemperatureAxisMaxLimit = _unitFormattingService.ConvertTemperature(100d);
	}

	// Reassigns every unit-formatted display from the current snapshot + unit preference. Safe once the
	// snapshot is seated (the object initializer runs OnSnapshotChanged right after construction).
	private void RefreshUnitFormattedDisplays()
	{
		TemperatureValueDisplay = ShouldDisplayMeasuredTemperature && Snapshot.TemperatureCelsius is double value
			? _unitFormattingService.FormatTemperatureValue(value, decimals: 0)
			: "--";

		TemperatureValueWithUnit = ShouldDisplayMeasuredTemperature
			? _unitFormattingService.FormatTemperature(Snapshot.TemperatureCelsius, decimals: 0)
			: _unitFormattingService.FormatTemperature(null);

		TemperatureUnitSuffix = _unitFormattingService.TemperatureUnitSuffix;
		SelectionDisplay = $"{DisplayName}: {TemperatureValueWithUnit}";
	}

	partial void OnSnapshotChanged(TemperatureTelemetrySnapshot value)
	{
		UpdateGaugeValues();
		UpdatePresentation();
		RefreshUnitFormattedDisplays();
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
					StatusBrush = AppThemeBrushes.Get("SeverityCriticalBrush", AppThemeBrushes.SeverityCriticalColor);
					TemperatureBrush = AppThemeBrushes.Get("SeverityCriticalBrush", AppThemeBrushes.SeverityCriticalColor);
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

		TemperatureBrush = AppThemeBrushes.Get("SeverityCriticalBrush", AppThemeBrushes.SeverityCriticalColor);
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
		// The history collections are ObservableCollections bound as LiveCharts Values; mutating them in
		// place raises CollectionChanged, which drives the chart directly — no revision nudge needed.
		SynchronizePoints(OverviewTemperatureHistory, []);
		SynchronizePoints(TemperatureHistory, []);
		Separators = [];
	}

	public void UpdateTemperatureHistory(IReadOnlyList<DateTimePoint> overviewHistory, IReadOnlyList<DateTimePoint> cardHistory)
	{
		SynchronizePoints(OverviewTemperatureHistory, overviewHistory);
		SynchronizePoints(TemperatureHistory, cardHistory);
		Separators = GetSeparators();
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
