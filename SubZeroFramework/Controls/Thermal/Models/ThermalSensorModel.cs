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
	private readonly IUnitFormattingService _unitFormattingService;

	public ThermalSensorModel(IUnitFormattingService unitFormattingService)
	{
		_unitFormattingService = unitFormattingService;

		// Snapshot is seated by the object initializer right after construction, which runs the full
		// RefreshUnitFormatting pass via OnSnapshotChanged; seed the service-derived text so the
		// stored properties are never null in the interim.
		TemperatureUnitSuffix = _unitFormattingService.TemperatureUnitSuffix;
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

	/// <summary>
	/// The sensor's plotted points, in DISPLAY units.
	/// </summary>
	/// <remarks>
	/// Bound from C#, not XAML: <see cref="Presentation.MenuItems.ThermalTelemetry.ThermalTelemetryModel"/>
	/// hands this very instance to a LiveCharts series as its <c>Values</c>, so the chart updates when the
	/// collection is mutated in place. A search for bindings that only greps XAML will not find that.
	/// </remarks>
	public ObservableCollection<DateTimePoint> OverviewTemperatureHistory { get; } = [];

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

	public string DisplayName => string.IsNullOrWhiteSpace(Snapshot.DisplayName)
		? $"Sensor {Snapshot.SensorIndex + 1}"
		: Snapshot.DisplayName;

	/// <summary>Card/legend title — index-based "Sensor N" (the descriptive role is shown as the location).</summary>
	public string CardTitle => $"Sensor {Snapshot.SensorIndex}";

	/// <summary>Short platform-role location shown beneath the title (e.g. "APU / SoC"); null when unidentified.</summary>
	public string? LocationDisplay => FrameworkSensorNameDisplay.ToLocation(Snapshot.SensorName);

	public bool HasLocation => !string.IsNullOrEmpty(LocationDisplay);

	public Visibility LocationVisibility => HasLocation ? Visibility.Visible : Visibility.Collapsed;

	/// <summary>
	/// The temperature to show, in canonical Celsius, or null when this sensor should not show a measurement.
	/// Formatted by UnitFormatConverter at render time.
	/// </summary>
	[ObservableProperty]
	public partial double? DisplayTemperatureCelsius { get; private set; }

	// The suffix stays formatted in the view model: it is rendered as its own element beside the number,
	// not as part of it.
	[ObservableProperty]
	public partial string TemperatureUnitSuffix { get; private set; } = string.Empty;

	[ObservableProperty]
	public partial string SelectionDisplay { get; private set; } = string.Empty;

	// A 0–100 scale position, not a temperature: the tile renders it as a ProgressBar with Maximum="100",
	// so it stays canonical and unit-independent. The number BESIDE the bar is DisplayTemperatureCelsius,
	// which the UnitFormat converter renders in the user's unit.
	public double GaugeValue => ShouldDisplayMeasuredTemperature
		? Math.Clamp(Snapshot.TemperatureCelsius ?? 0d, 0d, 100d)
		: 0d;

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

		// The canonical reading is formatted by UnitFormatConverter at render time, so it only needs its
		// bindings to run again — that is what the null property name asks for. See UnitFormatConverter.
		OnPropertyChanged(propertyName: null);
	}

	// Reassigns every unit-formatted display from the current snapshot + unit preference. Safe once the
	// snapshot is seated (the object initializer runs OnSnapshotChanged right after construction).
	private void RefreshUnitFormattedDisplays()
	{
		// The canonical value the tiles bind through UnitFormatValue. Null when the sensor should not show a
		// measurement at all, so the converter renders the empty state rather than this deciding the wording.
		DisplayTemperatureCelsius = ShouldDisplayMeasuredTemperature ? Snapshot.TemperatureCelsius : null;

		TemperatureUnitSuffix = _unitFormattingService.TemperatureUnitSuffix;

		// A COMPOSITE (name + reading), so it stays formatted here — through the service, like every
		// composite. A converter formats one value and cannot join two.
		SelectionDisplay = $"{DisplayName}: {_unitFormattingService.FormatTemperature(DisplayTemperatureCelsius, decimals: 0)}";
	}

	partial void OnSnapshotChanged(TemperatureTelemetrySnapshot value)
	{
		UpdatePresentation();
		RefreshUnitFormattedDisplays();
	}

	private void UpdatePresentation()
	{
		if (!Snapshot.IsAvailable)
		{
			StatusText = "Status: Unavailable";
			StatusBrush = AppThemeBrushes.Get("StatusErrorTextBrush", AppThemeBrushes.StatusErrorColor);
			TemperatureBrush = AppThemeBrushes.Get("TextSecondaryBrush", AppThemeBrushes.TextSecondaryColor);
			return;
		}

		var temperatureState = Snapshot.TemperatureState;

		switch (temperatureState)
		{
			case FrameworkTemperatureState.NotPresent:
				StatusText = "Status: Not Present";
					StatusBrush = AppThemeBrushes.Get("StatusErrorTextBrush", AppThemeBrushes.StatusErrorColor);
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

	public void ClearTemperatureHistory()
	{
		// The history collection is an ObservableCollection handed to a LiveCharts series as its Values;
		// mutating it in place raises CollectionChanged, which drives the chart directly — no revision nudge.
		SynchronizePoints(OverviewTemperatureHistory, []);
	}

	public void UpdateTemperatureHistory(IReadOnlyList<DateTimePoint> history)
	{
		SynchronizePoints(OverviewTemperatureHistory, history);
	}

	private bool ShouldDisplayMeasuredTemperature => Snapshot.IsAvailable
		&& Snapshot.TemperatureCelsius is not null
		&& (Snapshot.TemperatureState is null || Snapshot.TemperatureState == FrameworkTemperatureState.Ok);

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
