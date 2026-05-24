using System.Globalization;
using System.Text.RegularExpressions;

using CommunityToolkit.Mvvm.ComponentModel;

using SubZeroFramework.Presentation.Units;

namespace SubZeroFramework.Controls.Fans.Models;

public partial class FrameworkDesktopFanOptionModel : ObservableObject
{
    private static readonly Regex AcousticNoisePattern = new(
        @"^\s*(?<primary>\d+(?:\.\d+)?)\s*dB(?:A|\(A\))?(?:\s*\(max\s*(?<max>\d+(?:\.\d+)?)\s*dB(?:A|\(A\))?\))?\s*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private static readonly Regex AcousticNoiseLabelPattern = new(
        @"dB\s*A|dBA|dB\(A\)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private readonly IUnitFormattingService _unitFormattingService;

    public FrameworkDesktopFanOptionModel(IUnitFormattingService unitFormattingService)
    {
        _unitFormattingService = unitFormattingService;
    }

    [ObservableProperty]
    public partial string ModelName { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FanDimensionsDisplay))]
    public partial double WidthMillimeters { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FanDimensionsDisplay))]
    public partial double HeightMillimeters { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FanDimensionsDisplay))]
    public partial double ThicknessMillimeters { get; set; }

    [ObservableProperty]
    public partial string ConnectorType { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MaximumAirflowDisplay))]
    public partial double MaximumAirflowCfm { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MaximumAirflowDisplay))]
    public partial string? AlternateAirflowDisplay { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AcousticNoiseDisplay))]
    public partial string RawAcousticNoiseDisplay { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AcousticNoiseDisplay))]
    public partial double? AcousticNoiseDecibels { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AcousticNoiseDisplay))]
    public partial double? MaximumAcousticNoiseDecibels { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MaximumFanSpeedDisplay))]
    public partial int MaximumFanSpeedRpm { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FanDimensionsDisplay))]
    [NotifyPropertyChangedFor(nameof(MaximumAirflowDisplay))]
    [NotifyPropertyChangedFor(nameof(AcousticNoiseDisplay))]
    [NotifyPropertyChangedFor(nameof(MaximumFanSpeedDisplay))]
    private partial int UnitFormattingRevision { get; set; }

    public string FanDimensionsDisplay => $"{_unitFormattingService.FormatLengthMillimeters(WidthMillimeters)} × {_unitFormattingService.FormatLengthMillimeters(HeightMillimeters)} × {_unitFormattingService.FormatLengthMillimeters(ThicknessMillimeters)}";

    public string MaximumAirflowDisplay => _unitFormattingService.FormatAirflowCfm(MaximumAirflowCfm, decimals: 1);

    public string AcousticNoiseDisplay => BuildAcousticNoiseDisplay();

    public string MaximumFanSpeedDisplay => _unitFormattingService.FormatFanSpeed(MaximumFanSpeedRpm);

    public void UpdateFrom(FrameworkDesktopFanOption option)
    {
        ModelName = option.ModelName;
        WidthMillimeters = option.FanDimensions.WidthMillimeters;
        HeightMillimeters = option.FanDimensions.HeightMillimeters;
        ThicknessMillimeters = option.FanDimensions.ThicknessMillimeters;
        ConnectorType = option.ConnectorType;
        MaximumAirflowCfm = option.MaximumAirflowCfm;
        AlternateAirflowDisplay = option.AlternateAirflowDisplay;
        RawAcousticNoiseDisplay = option.AcousticNoiseDisplay;
        AcousticNoiseDecibels = option.AcousticNoiseDecibels;
        MaximumAcousticNoiseDecibels = option.MaximumAcousticNoiseDecibels;
        MaximumFanSpeedRpm = option.MaximumFanSpeedRpm;
    }

    public void RefreshUnitFormatting()
    {
        UnitFormattingRevision++;
    }

    private string BuildAcousticNoiseDisplay()
    {
        if (TryGetStructuredAcousticNoise(out var acousticNoiseDecibels, out var maximumAcousticNoiseDecibels))
        {
            var primaryDisplay = _unitFormattingService.FormatAcousticLevelDecibels(acousticNoiseDecibels);

            return maximumAcousticNoiseDecibels is double maximumValue
                ? $"{primaryDisplay} (max {_unitFormattingService.FormatAcousticLevelDecibels(maximumValue)})"
                : primaryDisplay;
        }

        return NormalizeAcousticNoiseDisplay(RawAcousticNoiseDisplay);
    }

    private bool TryGetStructuredAcousticNoise(out double acousticNoiseDecibels, out double? maximumAcousticNoiseDecibels)
    {
        if (AcousticNoiseDecibels is double structuredAcousticNoiseDecibels)
        {
            acousticNoiseDecibels = structuredAcousticNoiseDecibels;
            maximumAcousticNoiseDecibels = MaximumAcousticNoiseDecibels;
            return true;
        }

        if (!TryParseAcousticNoiseDisplay(RawAcousticNoiseDisplay, out acousticNoiseDecibels, out maximumAcousticNoiseDecibels))
        {
            acousticNoiseDecibels = 0d;
            maximumAcousticNoiseDecibels = null;
            return false;
        }

        return true;
    }

    private static bool TryParseAcousticNoiseDisplay(string? rawDisplay, out double acousticNoiseDecibels, out double? maximumAcousticNoiseDecibels)
    {
        acousticNoiseDecibels = 0d;
        maximumAcousticNoiseDecibels = null;

        if (string.IsNullOrWhiteSpace(rawDisplay))
        {
            return false;
        }

        var match = AcousticNoisePattern.Match(rawDisplay.Trim());
        if (!match.Success
            || !double.TryParse(match.Groups["primary"].Value, CultureInfo.InvariantCulture, out acousticNoiseDecibels))
        {
            return false;
        }

        if (match.Groups["max"].Success
            && double.TryParse(match.Groups["max"].Value, CultureInfo.InvariantCulture, out var parsedMaximumAcousticNoiseDecibels))
        {
            maximumAcousticNoiseDecibels = parsedMaximumAcousticNoiseDecibels;
        }

        return true;
    }

    private static string NormalizeAcousticNoiseDisplay(string? rawDisplay)
    {
        if (string.IsNullOrWhiteSpace(rawDisplay))
        {
            return "Unknown";
        }

        return AcousticNoiseLabelPattern.Replace(rawDisplay.Trim(), "dB(A)");
    }
}
