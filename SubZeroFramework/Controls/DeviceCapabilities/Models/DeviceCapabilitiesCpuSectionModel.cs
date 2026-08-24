using System;
using System.Collections.ObjectModel;
using System.ComponentModel;

using CommunityToolkit.Mvvm.ComponentModel;

using LiveChartsCore.Defaults;

using SubZeroFramework.Presentation;
using SubZeroFramework.Presentation.MenuItems.DeviceCapabilities;

namespace SubZeroFramework.Controls.DeviceCapabilities.Models;

/// <summary>
/// The CPU section's slice over the Device Capabilities page model. Every figure it shows is MIRRORED as a
/// stored property that <see cref="RefreshDerivedState"/> reassigns when the page reports a relevant change:
/// assignment raises PropertyChanged only for values that actually changed, so nothing re-renders needlessly.
/// </summary>
public sealed partial class DeviceCapabilitiesCpuSectionModel : ObservableObject, IDisposable
{
    private readonly DeviceCapabilitiesModel _parent;

    public DeviceCapabilitiesCpuSectionModel(DeviceCapabilitiesModel parent)
    {
        _parent = parent;
        _parent.PropertyChanged += ParentPropertyChanged;
        RefreshDerivedState();
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CpuCountDisplay))]
    [NotifyPropertyChangedFor(nameof(SocketsDisplay))]
    public partial int CpuCount { get; private set; }

    public string CpuCountDisplay => CpuCount.ToString();

    /// <summary>HardwareInfo reports one package per socket, so populated == present (mockup "1 of 1 populated").</summary>
    public string SocketsDisplay => $"{CpuCount} of {CpuCount} populated";

    // Canonical megahertz, formatted by UnitFormatConverter at render time; null renders "Unknown".
    [ObservableProperty]
    public partial double? AverageClockSpeedMegahertz { get; private set; }

    [ObservableProperty]
    public partial double? AverageMaxClockSpeedMegahertz { get; private set; }

    public string RecentTelemetryHistoryWindowDisplay => PresentationDefaults.RecentTelemetryHistoryWindowLabel;

    [ObservableProperty]
    public partial DateTimePoint[] CpuUsageHistory { get; private set; } = [];

    [ObservableProperty]
    public partial double[] CpuUsageHistorySeparators { get; private set; } = [];

    [ObservableProperty]
    public partial double? CpuUsageHistoryMinLimit { get; private set; }

    [ObservableProperty]
    public partial double? CpuUsageHistoryMaxLimit { get; private set; }

    [ObservableProperty]
    public partial DateTimePoint[] CpuClockHistory { get; private set; } = [];

    [ObservableProperty]
    public partial double[] CpuClockHistorySeparators { get; private set; } = [];

    [ObservableProperty]
    public partial double? CpuClockHistoryMinLimit { get; private set; }

    [ObservableProperty]
    public partial double? CpuClockHistoryMaxLimit { get; private set; }

    // The axis labelers and the card collection are built once by the page and never swapped, so these stay
    // plain pass-throughs.
    public Func<DateTime, string> CpuUsageLabelsFormatter => _parent.CpuUsageLabelsFormatter;

    public Func<DateTime, string> CpuClockLabelsFormatter => _parent.CpuClockLabelsFormatter;

    public ReadOnlyObservableCollection<DeviceCapabilitiesCpuPackageCardModel> CpuPackageCards => _parent.CpuPackageCards;

    public void Dispose()
    {
        _parent.PropertyChanged -= ParentPropertyChanged;
    }

    private void RefreshDerivedState()
    {
        CpuCount = _parent.CpuCount;
        AverageClockSpeedMegahertz = _parent.AverageClockSpeedMegahertz;
        AverageMaxClockSpeedMegahertz = _parent.AverageMaxClockSpeedMegahertz;
        CpuUsageHistory = _parent.CpuUsageHistory;
        CpuUsageHistorySeparators = _parent.CpuUsageHistorySeparators;
        CpuUsageHistoryMinLimit = _parent.CpuUsageHistoryMinLimit;
        CpuUsageHistoryMaxLimit = _parent.CpuUsageHistoryMaxLimit;
        CpuClockHistory = _parent.CpuClockHistory;
        CpuClockHistorySeparators = _parent.CpuClockHistorySeparators;
        CpuClockHistoryMinLimit = _parent.CpuClockHistoryMinLimit;
        CpuClockHistoryMaxLimit = _parent.CpuClockHistoryMaxLimit;
    }

    private void ParentPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // An empty name is the page's "everything changed" signal, raised when the display units change —
        // the averages are unit-formatted text and the chart limits live in display space.
        if (string.IsNullOrEmpty(e.PropertyName))
        {
            RefreshDerivedState();
            return;
        }

        switch (e.PropertyName)
        {
            case nameof(DeviceCapabilitiesModel.Snapshot):
            case nameof(DeviceCapabilitiesModel.AverageClockSpeedMegahertz):
            case nameof(DeviceCapabilitiesModel.AverageMaxClockSpeedMegahertz):
            case nameof(DeviceCapabilitiesModel.CpuUsageHistory):
            case nameof(DeviceCapabilitiesModel.CpuUsageHistorySeparators):
            case nameof(DeviceCapabilitiesModel.CpuUsageHistoryMinLimit):
            case nameof(DeviceCapabilitiesModel.CpuUsageHistoryMaxLimit):
            case nameof(DeviceCapabilitiesModel.CpuClockHistory):
            case nameof(DeviceCapabilitiesModel.CpuClockHistorySeparators):
            case nameof(DeviceCapabilitiesModel.CpuClockHistoryMinLimit):
            case nameof(DeviceCapabilitiesModel.CpuClockHistoryMaxLimit):
                RefreshDerivedState();
                break;
        }
    }
}
