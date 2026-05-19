using System;
using System.ComponentModel;

using CommunityToolkit.Mvvm.ComponentModel;

using SubZeroFramework.Presentation.MenuItems.DeviceCapabilities;

namespace SubZeroFramework.Controls.DeviceCapabilities.Models;

public sealed partial class DeviceCapabilitiesSystemProfileSectionModel : ObservableObject, IDisposable
{
    private readonly DeviceCapabilitiesModel _parent;

    public DeviceCapabilitiesSystemProfileSectionModel(DeviceCapabilitiesModel parent)
    {
        _parent = parent;
        RefreshFromParent();
        _parent.PropertyChanged += ParentPropertyChanged;
    }

    [ObservableProperty]
    public partial string SystemProfileOs { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string SystemProfileVendor { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string SystemProfileModel { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string SystemProfileOsVersion { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string SystemProfileProductName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string SystemProfileSystemVersion { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string SystemProfileEcBuildInfo { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string SystemProfileBiosVersion { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string SystemProfileBiosReleaseDate { get; set; } = string.Empty;

    public void Dispose()
    {
        _parent.PropertyChanged -= ParentPropertyChanged;
    }

    private void ParentPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(DeviceCapabilitiesModel.Snapshot):
            case nameof(DeviceCapabilitiesModel.FrameworkStatus):
                RefreshFromParent();
                break;
            case nameof(DeviceCapabilitiesModel.SystemProfileOs):
                SystemProfileOs = _parent.SystemProfileOs;
                break;
            case nameof(DeviceCapabilitiesModel.SystemProfileVendor):
                SystemProfileVendor = _parent.SystemProfileVendor;
                break;
            case nameof(DeviceCapabilitiesModel.SystemProfileModel):
                SystemProfileModel = _parent.SystemProfileModel;
                break;
            case nameof(DeviceCapabilitiesModel.SystemProfileOsVersion):
                SystemProfileOsVersion = _parent.SystemProfileOsVersion;
                break;
            case nameof(DeviceCapabilitiesModel.SystemProfileProductName):
                SystemProfileProductName = _parent.SystemProfileProductName;
                break;
            case nameof(DeviceCapabilitiesModel.SystemProfileSystemVersion):
                SystemProfileSystemVersion = _parent.SystemProfileSystemVersion;
                break;
            case nameof(DeviceCapabilitiesModel.SystemProfileEcBuildInfo):
                SystemProfileEcBuildInfo = _parent.SystemProfileEcBuildInfo;
                break;
            case nameof(DeviceCapabilitiesModel.SystemProfileBiosVersion):
                SystemProfileBiosVersion = _parent.SystemProfileBiosVersion;
                break;
            case nameof(DeviceCapabilitiesModel.SystemProfileBiosReleaseDate):
                SystemProfileBiosReleaseDate = _parent.SystemProfileBiosReleaseDate;
                break;
        }
    }

    private void RefreshFromParent()
    {
        SystemProfileOs = _parent.SystemProfileOs;
        SystemProfileVendor = _parent.SystemProfileVendor;
        SystemProfileModel = _parent.SystemProfileModel;
        SystemProfileOsVersion = _parent.SystemProfileOsVersion;
        SystemProfileProductName = _parent.SystemProfileProductName;
        SystemProfileSystemVersion = _parent.SystemProfileSystemVersion;
        SystemProfileEcBuildInfo = _parent.SystemProfileEcBuildInfo;
        SystemProfileBiosVersion = _parent.SystemProfileBiosVersion;
        SystemProfileBiosReleaseDate = _parent.SystemProfileBiosReleaseDate;
    }
}