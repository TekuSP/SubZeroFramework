using System;
using System.ComponentModel;

using CommunityToolkit.Mvvm.ComponentModel;

using Microsoft.UI.Xaml;

using SubZeroFramework.Controls.Fans.Models;
using SubZeroFramework.Presentation.MenuItems.DeviceCapabilities;

namespace SubZeroFramework.Controls.DeviceCapabilities.Models;

public sealed partial class DeviceCapabilitiesCoolingSectionModel : ObservableObject, IDisposable
{
    private readonly DeviceCapabilitiesModel _parent;

    public DeviceCapabilitiesCoolingSectionModel(DeviceCapabilitiesModel parent)
    {
        _parent = parent;
        FanAdvancedInfo = _parent.FanAdvancedInfo;
        _parent.PropertyChanged += ParentPropertyChanged;
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CoolingHardwareVisibility))]
    public partial FanAdvancedInfoCardModel? FanAdvancedInfo { get; set; }

    public Visibility CoolingHardwareVisibility => FanAdvancedInfo is null
        ? Visibility.Collapsed
        : Visibility.Visible;

    public void Dispose()
    {
        _parent.PropertyChanged -= ParentPropertyChanged;
    }

    private void ParentPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(DeviceCapabilitiesModel.FanAdvancedInfo):
                FanAdvancedInfo = _parent.FanAdvancedInfo;
                break;
        }
    }
}