using System.Globalization;
using System.Text.RegularExpressions;

using CommunityToolkit.Mvvm.ComponentModel;

using SubZeroFramework.Services.Units;

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
        RefreshUnitFormatting();
    }

    [ObservableProperty]
    public partial string ModelName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial double WidthMillimeters { get; set; }

    partial void OnWidthMillimetersChanged(double value) => RefreshUnitFormatting();

    [ObservableProperty]
    public partial double HeightMillimeters { get; set; }

    partial void OnHeightMillimetersChanged(double value) => RefreshUnitFormatting();

    [ObservableProperty]
    public partial double ThicknessMillimeters { get; set; }

    partial void OnThicknessMillimetersChanged(double value) => RefreshUnitFormatting();

    [ObservableProperty]
    public partial string ConnectorType { get; set; } = string.Empty;

    [ObservableProperty]
    public partial double MaximumAirflowCfm { get; set; }

    partial void OnMaximumAirflowCfmChanged(double value) => RefreshUnitFormatting();

    [ObservableProperty]
    public partial string? AlternateAirflowDisplay { get; set; }

    partial void OnAlternateAirflowDisplayChanged(string? value) => RefreshUnitFormatting();

    [ObservableProperty]
    public partial string RawAcousticNoiseDisplay { get; set; } = string.Empty;

    partial void OnRawAcousticNoiseDisplayChanged(string value) => RefreshUnitFormatting();

    [ObservableProperty]
    public partial double? AcousticNoiseDecibels { get; set; }

    partial void OnAcousticNoiseDecibelsChanged(double? value) => RefreshUnitFormatting();

    [ObservableProperty]
    public partial double? MaximumAcousticNoiseDecibels { get; set; }

    partial void OnMaximumAcousticNoiseDecibelsChanged(double? value) => RefreshUnitFormatting();

    [ObservableProperty]
    public partial int MaximumFanSpeedRpm { get; set; }

    partial void OnMaximumFanSpeedRpmChanged(int value) => RefreshUnitFormatting();

    [ObservableProperty]
    public partial string FanDimensionsDisplay { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string MaximumAirflowDisplay { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string AcousticNoiseDisplay { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string MaximumFanSpeedDisplay { get; private set; } = string.Empty;

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
        FanDimensionsDisplay = $"{_unitFormattingService.FormatLengthMillimeters(WidthMillimeters)} × {_unitFormattingService.FormatLengthMillimeters(HeightMillimeters)} × {_unitFormattingService.FormatLengthMillimeters(ThicknessMillimeters)}";
        MaximumAirflowDisplay = _unitFormattingService.FormatAirflowCfm(MaximumAirflowCfm, decimals: 1);
        AcousticNoiseDisplay = BuildAcousticNoiseDisplay();
        MaximumFanSpeedDisplay = _unitFormattingService.FormatFanSpeed(MaximumFanSpeedRpm);
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
