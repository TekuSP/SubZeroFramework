using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Microsoft.UI.Xaml.Controls;

using System.Reactive.Disposables;
using System.Reactive.Disposables.Fluent;
using System.Reactive.Linq;

using SubZeroFramework.Services;

namespace SubZeroFramework.Presentation.MenuItems.Settings;

public partial class SettingsModel : ObservableObject, IDisposable
{
    private readonly CompositeDisposable _subscriptions = [];
    private readonly IFrameworkServiceControlClient _frameworkServiceControlClient;
    private FrameworkSystemStatus? _lastObservedStatus;

    [ObservableProperty]
    public partial string EndpointValidationMessage { get; set; }

    [ObservableProperty]
    public partial string LastStatusObservedAt { get; set; }

    [ObservableProperty]
    public partial string ServiceStateTitle { get; set; }

    [ObservableProperty]
    public partial string ServiceStateMessage { get; set; }

    [ObservableProperty]
    public partial string PlatformServiceManager { get; set; }

    [ObservableProperty]
    public partial string ServiceIdentity { get; set; }

    [ObservableProperty]
    public partial string InstallSourceSummary { get; set; }

    [ObservableProperty]
    public partial string InstallReadinessMessage { get; set; }

    [ObservableProperty]
    public partial string PrivilegePromptMessage { get; set; }

    [ObservableProperty]
    public partial string LastActionTitle { get; set; }

    [ObservableProperty]
    public partial string LastActionMessage { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanManageInstalledService))]
    [NotifyCanExecuteChangedFor(nameof(ShutdownServiceCommand))]
    [NotifyCanExecuteChangedFor(nameof(RestartServiceCommand))]
    [NotifyCanExecuteChangedFor(nameof(EnableAutorunCommand))]
    [NotifyCanExecuteChangedFor(nameof(DisableAutorunCommand))]
    [NotifyCanExecuteChangedFor(nameof(InstallServiceCommand))]
    [NotifyCanExecuteChangedFor(nameof(UpdateServiceCommand))]
    [NotifyCanExecuteChangedFor(nameof(UninstallServiceCommand))]
    public partial bool IsOperationInProgress { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanManageInstalledService))]
    [NotifyCanExecuteChangedFor(nameof(ShutdownServiceCommand))]
    [NotifyCanExecuteChangedFor(nameof(RestartServiceCommand))]
    [NotifyCanExecuteChangedFor(nameof(EnableAutorunCommand))]
    [NotifyCanExecuteChangedFor(nameof(DisableAutorunCommand))]
    [NotifyCanExecuteChangedFor(nameof(InstallServiceCommand))]
    [NotifyCanExecuteChangedFor(nameof(UpdateServiceCommand))]
    [NotifyCanExecuteChangedFor(nameof(UninstallServiceCommand))]
    public partial bool IsServiceControlSupported { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanManageInstalledService))]
    [NotifyCanExecuteChangedFor(nameof(ShutdownServiceCommand))]
    [NotifyCanExecuteChangedFor(nameof(RestartServiceCommand))]
    [NotifyCanExecuteChangedFor(nameof(EnableAutorunCommand))]
    [NotifyCanExecuteChangedFor(nameof(DisableAutorunCommand))]
    [NotifyCanExecuteChangedFor(nameof(UninstallServiceCommand))]
    public partial bool IsServiceInstalled { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(InstallServiceCommand))]
    public partial bool CanInstallService { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(UpdateServiceCommand))]
    public partial bool CanUpdateService { get; set; }

    [ObservableProperty]
    public partial bool IsLastActionVisible { get; set; }

    [ObservableProperty]
    public partial InfoBarSeverity LastActionSeverity { get; set; }

    public IAsyncRelayCommand ShutdownServiceCommand { get; }

    public IAsyncRelayCommand RestartServiceCommand { get; }

    public IAsyncRelayCommand EnableAutorunCommand { get; }

    public IAsyncRelayCommand DisableAutorunCommand { get; }

    public IAsyncRelayCommand InstallServiceCommand { get; }

    public IAsyncRelayCommand UpdateServiceCommand { get; }

    public IAsyncRelayCommand UninstallServiceCommand { get; }

    public bool CanManageInstalledService => IsServiceControlSupported && IsServiceInstalled && !IsOperationInProgress;

    public SettingsModel(
        IStringLocalizer localizer,
        IOptions<AppConfig> appInfo,
        IFrameworkStatusClient frameworkStatusClient,
        IFrameworkServiceControlClient frameworkServiceControlClient,
        SynchronizationContext synchronizationContext)
    {
        _frameworkServiceControlClient = frameworkServiceControlClient;

        EndpointValidationMessage = frameworkStatusClient.EndpointValidation.Message;
        LastStatusObservedAt = frameworkStatusClient.LastObservedAt is DateTimeOffset observedAt
            ? observedAt.LocalDateTime.ToString("T")
            : "No status received yet";
        ServiceStateTitle = "Checking service health";
        ServiceStateMessage = "Waiting for status stream updates from SubZeroFramework.Service.";
        LastActionTitle = string.Empty;
        LastActionMessage = string.Empty;
        LastActionSeverity = InfoBarSeverity.Informational;

        var serviceInfo = _frameworkServiceControlClient.GetInfo();

        ShutdownServiceCommand = new AsyncRelayCommand(() => ExecuteServiceActionAsync(_frameworkServiceControlClient.ShutdownAsync), CanRunInstalledServiceAction);
        RestartServiceCommand = new AsyncRelayCommand(() => ExecuteServiceActionAsync(_frameworkServiceControlClient.RestartAsync), CanRunInstalledServiceAction);
        EnableAutorunCommand = new AsyncRelayCommand(() => ExecuteServiceActionAsync(_frameworkServiceControlClient.EnableAutorunAsync), CanRunInstalledServiceAction);
        DisableAutorunCommand = new AsyncRelayCommand(() => ExecuteServiceActionAsync(_frameworkServiceControlClient.DisableAutorunAsync), CanRunInstalledServiceAction);
        InstallServiceCommand = new AsyncRelayCommand(() => ExecuteServiceActionAsync(_frameworkServiceControlClient.InstallAsync), CanRunInstallAction);
        UpdateServiceCommand = new AsyncRelayCommand(() => ExecuteServiceActionAsync(_frameworkServiceControlClient.UpdateAsync), CanRunUpdateAction);
        UninstallServiceCommand = new AsyncRelayCommand(() => ExecuteServiceActionAsync(_frameworkServiceControlClient.UninstallAsync), CanRunInstalledServiceAction);

        ApplyServiceControlInfo(serviceInfo);

        frameworkStatusClient
            .WatchStatus()
            .ObserveOn(synchronizationContext)
            .Subscribe(UpdateStatus)
            .DisposeWith(_subscriptions);
    }

    public void Dispose()
    {
        _subscriptions.Dispose();
    }

    private bool CanRunServiceAction()
        => IsServiceControlSupported && !IsOperationInProgress;

    private bool CanRunInstalledServiceAction()
        => CanRunServiceAction() && IsServiceInstalled;

    private bool CanRunInstallAction()
        => CanRunServiceAction() && CanInstallService;

    private bool CanRunUpdateAction()
        => CanRunServiceAction() && CanUpdateService;

    private async Task ExecuteServiceActionAsync(Func<CancellationToken, Task<FrameworkServiceCommandResult>> action)
    {
        if (!CanRunServiceAction())
        {
            return;
        }

        IsOperationInProgress = true;

        try
        {
            var result = await action(CancellationToken.None);
            RefreshServiceControlInfo();
            LastActionTitle = result.OperationName;
            LastActionMessage = result.Message;
            LastActionSeverity = MapSeverity(result.Kind);
            IsLastActionVisible = true;
        }
        finally
        {
            IsOperationInProgress = false;
        }
    }

    private void UpdateStatus(FrameworkSystemStatus status)
    {
        _lastObservedStatus = status;
        LastStatusObservedAt = status.ObservedAt.LocalDateTime.ToString("T");

        if (!status.IsGrpcActive)
        {
            ServiceStateTitle = IsServiceInstalled ? "Service offline" : "Service not installed";
            ServiceStateMessage = IsServiceInstalled
                ? status.LastError ?? "The client cannot reach SubZeroFramework.Service over local gRPC IPC."
                : "SubZeroFramework.Service is not currently installed.";
            return;
        }

        if (!status.IsLibraryAvailable)
        {
            ServiceStateTitle = "Service running with library issue";
            ServiceStateMessage = status.LastError ?? "The service is running, but FrameworkDotnet could not be loaded.";
            return;
        }

        if (status.RequiresElevation)
        {
            ServiceStateTitle = "Service requires elevation";
            ServiceStateMessage = "The service is running without the privileges required for Framework EC access.";
            return;
        }

        if (!string.IsNullOrEmpty(status.LastError))
        {
            ServiceStateTitle = "Service warning";
            ServiceStateMessage = status.LastError;
            return;
        }

        ServiceStateTitle = "Service reachable";
        ServiceStateMessage = "The background service is reachable and reporting status successfully.";
    }

    private void ApplyServiceControlInfo(FrameworkServiceControlInfo serviceInfo)
    {
        PlatformServiceManager = serviceInfo.PlatformServiceManager;
        ServiceIdentity = serviceInfo.ServiceIdentity;
        InstallSourceSummary = serviceInfo.InstallSourceSummary;
        InstallReadinessMessage = serviceInfo.InstallReadinessMessage;
        PrivilegePromptMessage = serviceInfo.PrivilegePromptMessage;
        IsServiceControlSupported = serviceInfo.IsSupported;
        IsServiceInstalled = serviceInfo.IsInstalled;
        CanInstallService = serviceInfo.CanInstall;
        CanUpdateService = serviceInfo.CanUpdate;
    }

    private void RefreshServiceControlInfo()
    {
        ApplyServiceControlInfo(_frameworkServiceControlClient.GetInfo());

        if (_lastObservedStatus is not null)
        {
            UpdateStatus(_lastObservedStatus);
        }
    }

    private InfoBarSeverity MapSeverity(FrameworkServiceCommandResultKind kind)
        => kind switch
        {
            FrameworkServiceCommandResultKind.Success => InfoBarSeverity.Success,
            FrameworkServiceCommandResultKind.Warning => InfoBarSeverity.Warning,
            _ => InfoBarSeverity.Error,
        };
}
