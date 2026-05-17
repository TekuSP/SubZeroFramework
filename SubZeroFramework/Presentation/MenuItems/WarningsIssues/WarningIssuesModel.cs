using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

using System.Reactive.Disposables;
using System.Reactive.Disposables.Fluent;
using System.Reactive.Linq;

using Windows.UI;

using SubZeroFramework.Services;

namespace SubZeroFramework.Presentation.MenuItems.WarningsIssues;

public partial class WarningIssuesModel : ObservableObject, IDisposable
{
    private readonly IFrameworkStatusClient _frameworkStatusClient;
    private readonly SynchronizationContext _synchronizationContext;
    private readonly IFrameworkServiceControlClient _frameworkServiceControlClient;
    private readonly CompositeDisposable _subscriptions = [];
    private readonly IAsyncRelayCommand[] _serviceCommands;

    public WarningIssuesModel(
        IStringLocalizer localizer,
        IOptions<AppConfig> appInfo,
        IFrameworkStatusClient frameworkStatusClient,
        IFrameworkServiceControlClient frameworkServiceControlClient,
        SynchronizationContext synchronizationContext)
    {
        _frameworkStatusClient = frameworkStatusClient;
        _frameworkServiceControlClient = frameworkServiceControlClient;
        _synchronizationContext = synchronizationContext;
        LastActionTitle = string.Empty;
        LastActionMessage = string.Empty;
        LastActionSeverity = InfoBarSeverity.Informational;

        var serviceInfo = _frameworkServiceControlClient.GetInfo();
        PlatformServiceManager = serviceInfo.PlatformServiceManager;
        InstallSourceSummary = serviceInfo.InstallSourceSummary;
        InstallReadinessMessage = serviceInfo.InstallReadinessMessage;
        PrivilegePromptMessage = serviceInfo.PrivilegePromptMessage;

        ApplyPageState(
            title: "Waiting for service status",
            message: "Checking SubZeroFramework.Service health and recovery options.",
            severity: InfoBarSeverity.Informational,
            stateLineOne: $"Manager: {PlatformServiceManager}",
            stateLineTwo: InstallSourceSummary,
            stateLineThree: InstallReadinessMessage,
            disabledControlOne: "Live telemetry dashboards",
            disabledControlTwo: "Manual fan control",
            disabledControlThree: "Curve and profile writes",
            disabledControlFour: "Service-backed hardware refresh",
            explanation: "The fallback surface is waiting for the first service status update before it decides which controls need to stay disabled.",
            actionHint: PrivilegePromptMessage);

        RestartServiceCommand = new AsyncRelayCommand(() => ExecuteServiceActionAsync(_frameworkServiceControlClient.RestartAsync), CanRunServiceAction);
        InstallServiceCommand = new AsyncRelayCommand(() => ExecuteServiceActionAsync(_frameworkServiceControlClient.InstallAsync), CanRunInstallAction);
        UpdateServiceCommand = new AsyncRelayCommand(() => ExecuteServiceActionAsync(_frameworkServiceControlClient.UpdateAsync), CanRunUpdateAction);
        UninstallServiceCommand = new AsyncRelayCommand(() => ExecuteServiceActionAsync(_frameworkServiceControlClient.UninstallAsync), CanRunServiceAction);

        _serviceCommands =
        [
            RestartServiceCommand,
            InstallServiceCommand,
            UpdateServiceCommand,
            UninstallServiceCommand,
        ];

        IsServiceControlSupported = serviceInfo.IsSupported;
        CanInstallService = serviceInfo.CanInstall;
        CanUpdateService = serviceInfo.CanUpdate;

        _frameworkStatusClient
            .WatchStatus()
            .ObserveOn(_synchronizationContext)
            .Subscribe(UpdateStatus)
            .DisposeWith(_subscriptions);
    }

    [ObservableProperty]
    public partial string StatusTitle { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string StatusMessage { get; set; } = string.Empty;

    [ObservableProperty]
    public partial InfoBarSeverity StatusSeverity { get; set; }

    [ObservableProperty]
    public partial string PlatformServiceManager { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string InstallSourceSummary { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string InstallReadinessMessage { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string PrivilegePromptMessage { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ServiceStateLineOne { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ServiceStateLineTwo { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ServiceStateLineThree { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string DisabledControlOne { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string DisabledControlTwo { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string DisabledControlThree { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string DisabledControlFour { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ExplanationBody { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ActionHint { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string LastActionTitle { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string LastActionMessage { get; set; } = string.Empty;

    [ObservableProperty]
    public partial InfoBarSeverity LastActionSeverity { get; set; }

    [ObservableProperty]
    public partial bool IsLastActionVisible { get; set; }

    [ObservableProperty]
    public partial bool IsOperationInProgress { get; set; }

    [ObservableProperty]
    public partial bool IsServiceControlSupported { get; set; }

    [ObservableProperty]
    public partial bool CanInstallService { get; set; }

    [ObservableProperty]
    public partial bool CanUpdateService { get; set; }

    public Visibility ErrorHeroVisibility => StatusSeverity == InfoBarSeverity.Error ? Visibility.Visible : Visibility.Collapsed;

    public Visibility WarningHeroVisibility => StatusSeverity == InfoBarSeverity.Warning ? Visibility.Visible : Visibility.Collapsed;

    public Visibility SuccessHeroVisibility => StatusSeverity == InfoBarSeverity.Success ? Visibility.Visible : Visibility.Collapsed;

    public Visibility InformationalHeroVisibility => StatusSeverity == InfoBarSeverity.Informational ? Visibility.Visible : Visibility.Collapsed;

    public IAsyncRelayCommand RestartServiceCommand { get; }

    public IAsyncRelayCommand InstallServiceCommand { get; }

    public IAsyncRelayCommand UpdateServiceCommand { get; }

    public IAsyncRelayCommand UninstallServiceCommand { get; }

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

    partial void OnStatusSeverityChanged(InfoBarSeverity value)
    {
        OnPropertyChanged(nameof(ErrorHeroVisibility));
        OnPropertyChanged(nameof(WarningHeroVisibility));
        OnPropertyChanged(nameof(SuccessHeroVisibility));
        OnPropertyChanged(nameof(InformationalHeroVisibility));
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
        if (!status.IsGrpcActive)
        {
            ApplyPageState(
                title: "Background service offline",
                message: "SubZeroFramework.Service is not reachable, so the app has switched to a safe recovery mode.",
                severity: InfoBarSeverity.Error,
                stateLineOne: $"Manager: {PlatformServiceManager}",
                stateLineTwo: InstallSourceSummary,
                stateLineThree: InstallReadinessMessage,
                disabledControlOne: "Live telemetry and history refresh",
                disabledControlTwo: "Manual fan control and direct write actions",
                disabledControlThree: "Curve, profile, and override changes",
                disabledControlFour: "Service-backed inventory refresh and hardware checks",
                explanation: "SubZeroFramework.Service was not found running, or the installed service is not currently reachable over local gRPC IPC. Until it is installed and running again, the UI stays read-only so it does not surface stale telemetry or unsafe control actions.",
                actionHint: ResolveActionHint());
            return;
        }

        if (!status.IsLibraryAvailable)
        {
            ApplyPageState(
                title: "FrameworkDotnet not found",
                message: "The service is running, but the required Framework library could not be loaded.",
                severity: InfoBarSeverity.Error,
                stateLineOne: $"Manager: {PlatformServiceManager}",
                stateLineTwo: InstallSourceSummary,
                stateLineThree: status.LastError ?? InstallReadinessMessage,
                disabledControlOne: "Telemetry refresh that depends on FrameworkDotnet",
                disabledControlTwo: "Manual fan control and hardware write paths",
                disabledControlThree: "Device-specific inventory and EC-backed status",
                disabledControlFour: "Service recovery that depends on a healthy runtime image",
                explanation: "The service process started, but it could not load the required Framework library. That leaves the app without the native access layer it needs for Framework telemetry and EC operations, so the control surfaces remain locked down.",
                actionHint: ResolveActionHint());
            return;
        }

        if (status.RequiresElevation)
        {
            ApplyPageState(
                title: "Service elevation required",
                message: "The service is running without the privileges required for Framework EC access.",
                severity: InfoBarSeverity.Warning,
                stateLineOne: $"Manager: {PlatformServiceManager}",
                stateLineTwo: InstallSourceSummary,
                stateLineThree: PrivilegePromptMessage,
                disabledControlOne: "Manual fan control and EC write operations",
                disabledControlTwo: "Framework runtime telemetry collection",
                disabledControlThree: "Privilege-sensitive lifecycle recovery",
                disabledControlFour: "Any control that requires elevated native access",
                explanation: "The service binary is present, but the active service instance is missing the elevated privileges required for Framework EC access. Until it is restarted with the correct service-manager privileges, the app must stay in a limited read-only state.",
                actionHint: ResolveActionHint());
            return;
        }

        if (status.IsFrameworkDevice != true)
        {
            ApplyPageState(
                title: "Framework device not detected",
                message: "The service is reachable, but this machine is not reporting supported Framework hardware.",
                severity: InfoBarSeverity.Warning,
                stateLineOne: $"Manager: {PlatformServiceManager}",
                stateLineTwo: InstallSourceSummary,
                stateLineThree: "Service is reachable, but the hardware signature does not match a supported Framework device.",
                disabledControlOne: "Manual fan curve adjustment",
                disabledControlTwo: "Advanced power and thermal tuning",
                disabledControlThree: "Custom EC integration and write flows",
                disabledControlFour: "Device-specific Framework control surfaces",
                explanation: "The app can talk to the service, but the detected hardware signature is not a supported Framework device. To avoid unsafe EC access or misleading controls, SubZero stays in a restricted mode on this machine.",
                actionHint: "Use the service actions only if you need to repair the service installation. Hardware-control features stay disabled on unsupported devices.");
            return;
        }

        if (!string.IsNullOrEmpty(status.LastError))
        {
            ApplyPageState(
                title: "Background service warning",
                message: status.LastError,
                severity: InfoBarSeverity.Warning,
                stateLineOne: $"Manager: {PlatformServiceManager}",
                stateLineTwo: InstallSourceSummary,
                stateLineThree: InstallReadinessMessage,
                disabledControlOne: "The control surface associated with the reported service warning",
                disabledControlTwo: "Any write flow that depends on a clean service state",
                disabledControlThree: "Telemetry actions that depend on the failing subsystem",
                disabledControlFour: "Recovery steps that require a stable service before proceeding",
                explanation: "The service reported a warning condition and the client is surfacing it here instead of continuing into the normal control pages. Resolve the service warning first, then return to the regular telemetry and control views.",
                actionHint: ResolveActionHint());
            return;
        }

        if (!status.IsFanControlEnabled)
        {
            ApplyPageState(
                title: "Fan control disabled",
                message: status.FanControlAuthorizationMessage ?? "Fan-control RPCs are currently disabled by the background service.",
                severity: InfoBarSeverity.Informational,
                stateLineOne: $"Manager: {PlatformServiceManager}",
                stateLineTwo: InstallSourceSummary,
                stateLineThree: "The service is reachable, but the command boundary is currently read-only.",
                disabledControlOne: "Manual fan RPM or duty writes",
                disabledControlTwo: "Custom curve activation",
                disabledControlThree: "Fan override persistence changes",
                disabledControlFour: "Any control that mutates EC fan state",
                explanation: "The service is online, but fan-control commands are intentionally disabled for this session or deployment. The page stays in read-only mode so you can inspect the state without surfacing unsafe or unavailable write actions.",
                actionHint: "Lifecycle actions remain available, but fan-control commands will stay disabled until the service configuration allows them.");
            return;
        }

        if (!status.HasCallerIdentityValidation)
        {
            ApplyPageState(
                title: "Fan control validation limited",
                message: status.FanControlAuthorizationMessage ?? "The service cannot currently validate caller identity for fan-control RPCs on this IPC transport.",
                severity: InfoBarSeverity.Warning,
                stateLineOne: $"Manager: {PlatformServiceManager}",
                stateLineTwo: InstallSourceSummary,
                stateLineThree: "Status and telemetry are available, but fan-control authorization is still fail-closed.",
                disabledControlOne: "Fan-control writes that depend on caller identity validation",
                disabledControlTwo: "Custom curve updates that cross the command boundary",
                disabledControlThree: "Any unsafe fallback that bypasses service authorization",
                disabledControlFour: "Privilege-sensitive remediation that assumes validated callers",
                explanation: "The service is online, but the current transport cannot fully validate caller identity for fan-control RPCs. The UI stays conservative and keeps those mutating controls disabled until the authorization boundary is fully trustworthy.",
                actionHint: "You can continue with read-only diagnostics, but fan-control commands remain unavailable until caller validation is complete.");
            return;
        }

        ApplyPageState(
            title: "Background service healthy",
            message: "Status streaming is connected and the service reports healthy Framework telemetry access.",
            severity: InfoBarSeverity.Success,
            stateLineOne: $"Manager: {PlatformServiceManager}",
            stateLineTwo: InstallSourceSummary,
            stateLineThree: "The service is online and normal control surfaces should be available.",
            disabledControlOne: "No fallback-only restrictions are currently active",
            disabledControlTwo: "Warnings and Issues can now yield to the normal pages",
            disabledControlThree: string.Empty,
            disabledControlFour: string.Empty,
            explanation: "This page is intended for degraded or unsupported states. If you are seeing the healthy state here, navigation should be able to return to the standard control surfaces.",
            actionHint: "No remediation is currently required.");
    }

    public void Dispose()
    {
        _subscriptions.Dispose();
    }

    private void ApplyPageState(
        string title,
        string message,
        InfoBarSeverity severity,
        string stateLineOne,
        string stateLineTwo,
        string stateLineThree,
        string disabledControlOne,
        string disabledControlTwo,
        string disabledControlThree,
        string disabledControlFour,
        string explanation,
        string actionHint)
    {
        StatusTitle = title;
        StatusMessage = message;
        StatusSeverity = severity;
        ServiceStateLineOne = stateLineOne;
        ServiceStateLineTwo = stateLineTwo;
        ServiceStateLineThree = stateLineThree;
        DisabledControlOne = disabledControlOne;
        DisabledControlTwo = disabledControlTwo;
        DisabledControlThree = disabledControlThree;
        DisabledControlFour = disabledControlFour;
        ExplanationBody = explanation;
        ActionHint = actionHint;
    }

    private string ResolveActionHint()
    {
        if (CanInstallService || CanUpdateService || IsServiceControlSupported)
        {
            return PrivilegePromptMessage;
        }

        return InstallReadinessMessage;
    }

    private static InfoBarSeverity MapSeverity(FrameworkServiceCommandResultKind kind)
        => kind switch
        {
            FrameworkServiceCommandResultKind.Success => InfoBarSeverity.Success,
            FrameworkServiceCommandResultKind.Warning => InfoBarSeverity.Warning,
            _ => InfoBarSeverity.Error,
        };
}
