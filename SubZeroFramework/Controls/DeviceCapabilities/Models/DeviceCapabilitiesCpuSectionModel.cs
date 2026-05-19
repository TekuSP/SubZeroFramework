using System;
using System.Collections.ObjectModel;
using System.ComponentModel;

using CommunityToolkit.Mvvm.ComponentModel;

using LiveChartsCore.Defaults;

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
    [NotifyPropertyChangedFor(nameof(AverageClockSpeed))]
    [NotifyPropertyChangedFor(nameof(AverageMaxClockSpeed))]
    [NotifyPropertyChangedFor(nameof(CpuClockHistory))]
    [NotifyPropertyChangedFor(nameof(CpuClockHistorySeparators))]
    [NotifyPropertyChangedFor(nameof(CpuClockHistoryMinLimit))]
    [NotifyPropertyChangedFor(nameof(CpuClockHistoryMaxLimit))]
    private partial int RefreshVersion { get; set; }

    public int CpuCount => _parent.CpuCount;

    public string AverageClockSpeed => _parent.AverageClockSpeed;

    public string AverageMaxClockSpeed => _parent.AverageMaxClockSpeed;

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
            case nameof(DeviceCapabilitiesModel.CpuClockHistory):
            case nameof(DeviceCapabilitiesModel.CpuClockHistorySeparators):
            case nameof(DeviceCapabilitiesModel.CpuClockHistoryMinLimit):
            case nameof(DeviceCapabilitiesModel.CpuClockHistoryMaxLimit):
                RefreshVersion++;
                break;
        }
    }
}