using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.WinUI;

using Microsoft.UI.Dispatching;

using SubZeroFramework.Models;
using SubZeroFramework.Services;

using System.Reactive.Disposables;
using System.Reactive.Disposables.Fluent;
using System.Reactive.Linq;

using Material.Icons;
namespace SubZeroFramework.Presentation.Header;

public partial class SubZeroHeaderModel : ObservableObject, IDisposable
{
    //Error part

    [ObservableProperty]
    public partial bool IsInError { get; set; } = false;

    [ObservableProperty]
    public partial string ErrorTitle { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ErrorReason { get; set; } = string.Empty;

    [ObservableProperty]
    public partial InfoBarSeverity ErrorSeverity { get; set; }

    [ObservableProperty]
    public partial bool IsErrorClosable { get; set; }

    [ObservableProperty]
    public partial bool SuppressErrorBar { get; set; }

    public bool ShouldShowErrorBar => IsInError && !SuppressErrorBar;

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

    [ObservableProperty]
    public partial string StatusHeartbeat { get; set; }

    [ObservableProperty]
    public partial string EndpointValidationMessage { get; set; }

    private IServiceProvider? _serviceProvider = null;
    private IFrameworkStatusClient? _frameworkStatusClient = null;
    private IHardwareInfoClient? _hardwareInfoClient = null;
    private DispatcherQueue? _dispatcherQueue = null;
    private SynchronizationContext? _synchronizationContext = null;
    private readonly CompositeDisposable _subscriptions = [];
    private readonly SerialDisposable _runningSubscription = new();
    private readonly SerialDisposable _hardwareInfoSubscription = new();
    private bool disposedValue;

    public void IServiceProviderChanged(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;

        //Get new providers
        _frameworkStatusClient = serviceProvider.GetRequiredService<IFrameworkStatusClient>();
        _hardwareInfoClient = serviceProvider.GetRequiredService<IHardwareInfoClient>();
        _dispatcherQueue = serviceProvider.GetRequiredService<DispatcherQueue>();
        _synchronizationContext = serviceProvider.GetRequiredService<SynchronizationContext>();
        EndpointValidationMessage = _frameworkStatusClient.EndpointValidation.Message;

        SubscribeHardwareInfo();
    }

    public SubZeroHeaderModel()
    {
        _runningSubscription.DisposeWith(_subscriptions);
        _hardwareInfoSubscription.DisposeWith(_subscriptions);
        StatusHeartbeat = "Telemetry: waiting";
        EndpointValidationMessage = string.Empty;
    }

    partial void OnIsInErrorChanged(bool value)
    {
        OnPropertyChanged(nameof(ShouldShowErrorBar));
    }

    partial void OnSuppressErrorBarChanged(bool value)
    {
        OnPropertyChanged(nameof(ShouldShowErrorBar));
    }

    public void SubscribeHardwareInfo()
    {
        if (_hardwareInfoClient is null || _synchronizationContext is null)
        {
            return;
        }

        _hardwareInfoSubscription.Disposable = _hardwareInfoClient
            .WatchHardwareInfo()
            .ObserveOn(_synchronizationContext)
            .Subscribe(UpdateHardwareInfoSnapshot);

        if (_frameworkStatusClient is not null)
        {
            _runningSubscription.Disposable = _frameworkStatusClient
                .WatchStatus()
                .ObserveOn(_synchronizationContext)
                .Subscribe(FrameworkSystemDataUpdated);
        }
        else
        {
            _runningSubscription.Disposable = null;
        }
    }

    private void UpdateHardwareInfoSnapshot(HardwareInfoSnapshot snapshot)
    {
        if (snapshot == null)
        {
            return;
        }

        if (snapshot.Cpus.Any())
        {
            CPUName = $"CPU: {string.Join(" + ", snapshot.Cpus.Select(x => x.Name?.Trim() ?? string.Empty))}";
        }

        if (snapshot.MemoryModules.Any())
        {
            var totals = snapshot.MemoryModules.Select(x => x.CapacityBytes / (1024d * 1024d * 1024d));
            RAMAmount = $"RAM: {string.Join(" + ", totals.Select(value => value.ToString("0.##")))} ({totals.Sum():0.##}) GB RAM";
        }
    }

    private void FrameworkSystemDataUpdated(FrameworkSystemStatus status)
    {
        StatusHeartbeat = status.LastTelemetryObservedAt == DateTimeOffset.MinValue
            ? "Telemetry: no samples yet"
            : $"Telemetry: {status.LastTelemetryObservedAt.LocalDateTime:T}";

        if (!status.IsGrpcActive)
        {
            IsInError = true;
            ErrorReason = "The SubZero Framework background service is not reachable over local gRPC IPC. Is your service running?";
            ErrorTitle = "SubZero Framework Service offline";
            ErrorSeverity = InfoBarSeverity.Error;
            IsErrorClosable = false;
            return;
        }

        if (!status.IsLibraryAvailable)
        {
            IsInError = true;
            ErrorReason = "Required framework library is missing. Your installation may be corrupted!";
            ErrorTitle = "FrameworkDotnet Not Found!";
            ErrorSeverity = InfoBarSeverity.Error;
            IsErrorClosable = false;
            return;
        }

        if (status.RequiresElevation)
        {
            IsInError = true;
            ErrorReason = "SubZero Framework fan control requires the Framework-System provider for SubZero Framework service to run as root.";
            ErrorTitle = "SubZero Framework Service elevation required";
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

        ECDriver = $"Driver: {status.ActiveDriver.ToString() ?? "Unknown driver"} {status.EcBuildInfo?.Split(' ', StringSplitOptions.TrimEntries).FirstOrDefault() ?? string.Empty}";
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

    protected virtual void Dispose(bool disposing)
    {
        if (!disposedValue)
        {
            if (disposing)
            {
                _subscriptions.Dispose();
                _frameworkStatusClient = null;
                _hardwareInfoClient = null;
                _serviceProvider = null;
                _dispatcherQueue = null;
                _synchronizationContext = null;
            }
            
            disposedValue = true;
        }
    }

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
}
