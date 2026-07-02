using System;
using System.Collections.ObjectModel;
using System.ComponentModel;

using CommunityToolkit.Mvvm.ComponentModel;

using LiveChartsCore.Defaults;

using SubZeroFramework.Presentation;
using SubZeroFramework.Presentation.MenuItems.DeviceCapabilities;

namespace SubZeroFramework.Controls.DeviceCapabilities.Models;

public sealed partial class DeviceCapabilitiesCpuSectionModel : ObservableObject, IDisposable
{
    private readonly DeviceCapabilitiesModel _parent;

    public DeviceCapabilitiesCpuSectionModel(DeviceCapabilitiesModel parent)
    {
        _parent = parent;
        _parent.PropertyChanged += ParentPropertyChanged;
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CpuCount))]
    [NotifyPropertyChangedFor(nameof(CpuCountDisplay))]
    [NotifyPropertyChangedFor(nameof(SocketsDisplay))]
    [NotifyPropertyChangedFor(nameof(AverageClockSpeed))]
    [NotifyPropertyChangedFor(nameof(AverageMaxClockSpeed))]
    [NotifyPropertyChangedFor(nameof(AverageCpuUsageDisplay))]
    [NotifyPropertyChangedFor(nameof(CpuUsageHistory))]
    [NotifyPropertyChangedFor(nameof(CpuUsageHistorySeparators))]
    [NotifyPropertyChangedFor(nameof(CpuUsageHistoryMinLimit))]
    [NotifyPropertyChangedFor(nameof(CpuUsageHistoryMaxLimit))]
    [NotifyPropertyChangedFor(nameof(CpuClockHistory))]
    [NotifyPropertyChangedFor(nameof(CpuClockHistorySeparators))]
    [NotifyPropertyChangedFor(nameof(CpuClockHistoryMinLimit))]
    [NotifyPropertyChangedFor(nameof(CpuClockHistoryMaxLimit))]
    private partial int RefreshVersion { get; set; }

    public int CpuCount => _parent.CpuCount;

    public string CpuCountDisplay => _parent.CpuCount.ToString();

    /// <summary>HardwareInfo reports one package per socket, so populated == present (mockup "1 of 1 populated").</summary>
    public string SocketsDisplay => $"{_parent.CpuCount} of {_parent.CpuCount} populated";

    public string AverageClockSpeed => _parent.AverageClockSpeed;

    public string AverageMaxClockSpeed => _parent.AverageMaxClockSpeed;

    public string AverageCpuUsageDisplay => _parent.AverageCpuUsageDisplay;

    public string RecentTelemetryHistoryWindowDisplay => PresentationDefaults.RecentTelemetryHistoryWindowLabel;

    public DateTimePoint[] CpuUsageHistory => _parent.CpuUsageHistory;

    public double[] CpuUsageHistorySeparators => _parent.CpuUsageHistorySeparators;

    public double? CpuUsageHistoryMinLimit => _parent.CpuUsageHistoryMinLimit;

    public double? CpuUsageHistoryMaxLimit => _parent.CpuUsageHistoryMaxLimit;

    public Func<DateTime, string> CpuUsageLabelsFormatter => _parent.CpuUsageLabelsFormatter;

    public DateTimePoint[] CpuClockHistory => _parent.CpuClockHistory;

    public double[] CpuClockHistorySeparators => _parent.CpuClockHistorySeparators;

    public double? CpuClockHistoryMinLimit => _parent.CpuClockHistoryMinLimit;

    public double? CpuClockHistoryMaxLimit => _parent.CpuClockHistoryMaxLimit;

    public Func<DateTime, string> CpuClockLabelsFormatter => _parent.CpuClockLabelsFormatter;

    public ReadOnlyObservableCollection<DeviceCapabilitiesCpuPackageCardModel> CpuPackageCards => _parent.CpuPackageCards;

    public void Dispose()
    {
        _parent.PropertyChanged -= ParentPropertyChanged;
    }

    private void ParentPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(DeviceCapabilitiesModel.Snapshot):
            case nameof(DeviceCapabilitiesModel.AverageClockSpeed):
            case nameof(DeviceCapabilitiesModel.AverageMaxClockSpeed):
            case nameof(DeviceCapabilitiesModel.AverageCpuUsageDisplay):
            case nameof(DeviceCapabilitiesModel.CpuUsageHistory):
            case nameof(DeviceCapabilitiesModel.CpuUsageHistorySeparators):
            case nameof(DeviceCapabilitiesModel.CpuUsageHistoryMinLimit):
            case nameof(DeviceCapabilitiesModel.CpuUsageHistoryMaxLimit):
            case nameof(DeviceCapabilitiesModel.CpuClockHistory):
            case nameof(DeviceCapabilitiesModel.CpuClockHistorySeparators):
            case nameof(DeviceCapabilitiesModel.CpuClockHistoryMinLimit):
            case nameof(DeviceCapabilitiesModel.CpuClockHistoryMaxLimit):
                RefreshVersion++;
                break;
        }
    }
}