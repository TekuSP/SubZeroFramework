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
    private readonly IAsyncRelayCommand[] _serviceCommands;

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
    public partial bool IsOperationInProgress { get; set; }

    [ObservableProperty]
    public partial bool IsServiceControlSupported { get; set; }

    [ObservableProperty]
    public partial bool CanInstallService { get; set; }

    [ObservableProperty]
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
        PlatformServiceManager = serviceInfo.PlatformServiceManager;
        ServiceIdentity = serviceInfo.ServiceIdentity;
        InstallSourceSummary = serviceInfo.InstallSourceSummary;
        InstallReadinessMessage = serviceInfo.InstallReadinessMessage;
        PrivilegePromptMessage = serviceInfo.PrivilegePromptMessage;

        ShutdownServiceCommand = new AsyncRelayCommand(() => ExecuteServiceActionAsync(_frameworkServiceControlClient.ShutdownAsync), CanRunServiceAction);
        RestartServiceCommand = new AsyncRelayCommand(() => ExecuteServiceActionAsync(_frameworkServiceControlClient.RestartAsync), CanRunServiceAction);
        EnableAutorunCommand = new AsyncRelayCommand(() => ExecuteServiceActionAsync(_frameworkServiceControlClient.EnableAutorunAsync), CanRunServiceAction);
        DisableAutorunCommand = new AsyncRelayCommand(() => ExecuteServiceActionAsync(_frameworkServiceControlClient.DisableAutorunAsync), CanRunServiceAction);
        InstallServiceCommand = new AsyncRelayCommand(() => ExecuteServiceActionAsync(_frameworkServiceControlClient.InstallAsync), CanRunInstallAction);
        UpdateServiceCommand = new AsyncRelayCommand(() => ExecuteServiceActionAsync(_frameworkServiceControlClient.UpdateAsync), CanRunUpdateAction);
        UninstallServiceCommand = new AsyncRelayCommand(() => ExecuteServiceActionAsync(_frameworkServiceControlClient.UninstallAsync), CanRunServiceAction);

        _serviceCommands =
        [
            ShutdownServiceCommand,
            RestartServiceCommand,
            EnableAutorunCommand,
            DisableAutorunCommand,
            InstallServiceCommand,
            UpdateServiceCommand,
            UninstallServiceCommand,
        ];

        IsServiceControlSupported = serviceInfo.IsSupported;
        CanInstallService = serviceInfo.CanInstall;
        CanUpdateService = serviceInfo.CanUpdate;

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

    partial void OnIsOperationInProgressChanged(bool value)
    {
        foreach (var command in _serviceCommands)
        {
            command.NotifyCanExecuteChanged();
        }
    }

    partial void OnCanInstallServiceChanged(bool value)
    {
        InstallServiceCommand.NotifyCanExecuteChanged();
    }

    partial void OnCanUpdateServiceChanged(bool value)
    {
        UpdateServiceCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsServiceControlSupportedChanged(bool value)
    {
        foreach (var command in _serviceCommands)
        {
            command.NotifyCanExecuteChanged();
        }
    }

    private bool CanRunServiceAction()
        => IsServiceControlSupported && !IsOperationInProgress;

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
        LastStatusObservedAt = status.ObservedAt.LocalDateTime.ToString("T");

        if (!status.IsGrpcActive)
        {
            ServiceStateTitle = "Service offline";
            ServiceStateMessage = status.LastError ?? "The client cannot reach SubZeroFramework.Service over local gRPC IPC.";
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

    private static InfoBarSeverity MapSeverity(FrameworkServiceCommandResultKind kind)
        => kind switch
        {
            FrameworkServiceCommandResultKind.Success => InfoBarSeverity.Success,
            FrameworkServiceCommandResultKind.Warning => InfoBarSeverity.Warning,
            _ => InfoBarSeverity.Error,
        };
}
