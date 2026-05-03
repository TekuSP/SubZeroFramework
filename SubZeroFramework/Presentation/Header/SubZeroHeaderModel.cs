using System;
using System.Collections.Generic;
using System.Text;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.WinUI;

using FrameworkDotnet.Interfaces;

using Hardware.Info;

using Microsoft.UI.Dispatching;
using System.Reactive;
using SubZeroFramework.Services;
using System.Reactive.Linq;
using Material.Icons;
namespace SubZeroFramework.Presentation.Header;

public partial class SubZeroHeaderModel : ObservableObject
{
    //Error part

    [ObservableProperty]
    public partial bool IsInError { get; set; } = false;

    [ObservableProperty]
    public partial string ErrorTitle { get; set; }

    [ObservableProperty]
    public partial string ErrorReason { get; set; }

    [ObservableProperty]
    public partial InfoBarSeverity ErrorSeverity { get; set; }

    [ObservableProperty]
    public partial bool IsErrorClosable { get; set; }

    //System info part
    [ObservableProperty]
    public partial string ProductName { get; set; } = "Model: ";
    [ObservableProperty]
    public partial MaterialIconKind ProductIcon { get; set; } = MaterialIconKind.Laptop;
    [ObservableProperty]
    public partial string ECDriver { get; set; } = "Driver: ";

    [ObservableProperty]
    public partial string CPUName { get; set; } = "CPU: ";

    [ObservableProperty]
    public partial string RAMAmount { get; set; } = "RAM: ";

    private IServiceProvider? _serviceProvider = null;
    private IFrameworkDataProvider? _frameworkDataProvider = null;
    private IHardwareInfo? _hardwareInfo = null;
    private DispatcherQueue? _dispatcherQueue = null;
    private SynchronizationContext? _synchronizationContext = null;
    private IDisposable? _runningSubscription = null;

    public void IServiceProviderChanged(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
        if (_frameworkDataProvider != null)
            _frameworkDataProvider.Dispose(); //Dispose old connection

        //Get new providers
        _frameworkDataProvider = serviceProvider.GetRequiredService<IFrameworkDataProvider>();
        _hardwareInfo = serviceProvider.GetRequiredService<IHardwareInfo>();
        _dispatcherQueue = serviceProvider.GetRequiredService<DispatcherQueue>();
        _synchronizationContext = serviceProvider.GetRequiredService<SynchronizationContext>();

        _ = Task.Run(GatherNewInformation);
    }

    public async Task GatherNewInformation()
    {
        if (_frameworkDataProvider is null)
            return;
        if (_hardwareInfo is null)
            return;
        if (_dispatcherQueue is null)
            return;
        if (_synchronizationContext is null)
            return;

        await _dispatcherQueue?.EnqueueAsync(() =>
        {
            _hardwareInfo.RefreshCPUList(false, 500, false);
            _hardwareInfo.RefreshMemoryList();
            CPUName = $"CPU: {string.Join(" + ", _hardwareInfo.CpuList.Select(x => x.Name.Trim()))}";
            RAMAmount = $"RAM: {string.Join(" + ", _hardwareInfo.MemoryList.Select(x => x.Capacity / (1024d * 1024d * 1024d)))} GB RAM";
        });

        if (_runningSubscription is not null)
            _runningSubscription.Dispose();

        _runningSubscription = _frameworkDataProvider.SystemStatus.ObserveOn(_synchronizationContext).Subscribe(FrameworkSystemDataUpdated);

        await _frameworkDataProvider.RefreshAsync();
    }

    private void FrameworkSystemDataUpdated(FrameworkSystemStatus status)
    {
        if (!status.IsLibraryAvailable)
        {
            IsInError = true;
            ErrorReason = "Required framework library is missing. Your installation may be corrupted!";
            ErrorTitle = "FrameworkDotnet Not Found!";
            ErrorSeverity = InfoBarSeverity.Error;
            IsErrorClosable = false;
            return;
        }

        if (status.IsFrameworkDevice != true)
        {
            IsInError = true;
            ErrorReason = "Your device is not recognized as Framework device. Are you on Framework?";
            ErrorTitle = "Framework EC not found!";
            ErrorSeverity = InfoBarSeverity.Error;
            IsErrorClosable = false;
            return;
        }

        if (!string.IsNullOrEmpty(status.LastError))
        {
            if (ErrorReason != status.LastError)
            {
                IsInError = true;
                ErrorReason = status.LastError;
                ErrorTitle = "SubZero Framework Processing error.";
                ErrorSeverity = InfoBarSeverity.Warning;
                IsErrorClosable = true;
            }
        }
        else
        {
            IsInError = false;
            ErrorReason = string.Empty;
            ErrorTitle = string.Empty;
            IsErrorClosable = true;
        }

        ECDriver = $"Driver: {status.ActiveDriver.ToString() ?? "Unknown driver"} {status.EcBuildInfo.Split(' ', StringSplitOptions.TrimEntries).FirstOrDefault()}";
        ProductName = $"Model: {status.DeviceModel ?? "Unknown device"}";

        if (status.PlatformFamily == FrameworkDotnet.Enums.FrameworkPlatformFamily.FrameworkDesktop)
        {
            ProductIcon = MaterialIconKind.DesktopTower;
        }
        else
        {
            ProductIcon = MaterialIconKind.Laptop;
        }
    }
}
