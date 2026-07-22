using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

using System.Reactive.Disposables;
using System.Reactive.Disposables.Fluent;
using System.Reactive.Linq;

using Windows.UI;

using SubZeroFramework.Services;
using SubZeroFramework.Themes;

namespace SubZeroFramework.Presentation.MenuItems.WarningsIssues;

public partial class WarningIssuesModel : ObservableObject, IDisposable
{
    private readonly IFrameworkStatusClient _frameworkStatusClient;
    private readonly SynchronizationContext _synchronizationContext;
    private readonly IFrameworkServiceControlClient _frameworkServiceControlClient;
    private readonly CompositeDisposable _subscriptions = [];
    private FrameworkSystemStatus? _lastObservedStatus;

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

        RestartServiceCommand = new AsyncRelayCommand(() => ExecuteServiceActionAsync(_frameworkServiceControlClient.RestartAsync), CanRunInstalledServiceAction);
        InstallServiceCommand = new AsyncRelayCommand(() => ExecuteServiceActionAsync(_frameworkServiceControlClient.InstallAsync), CanRunInstallAction);
        UpdateServiceCommand = new AsyncRelayCommand(() => ExecuteServiceActionAsync(_frameworkServiceControlClient.UpdateAsync), CanRunUpdateAction);
        RecheckCommand = new RelayCommand(RefreshServiceControlInfo);

        ApplyServiceControlInfo(serviceInfo);

        ApplyPageState(
            eyebrow: "CHECKING SERVICE",
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
            actionHint: ResolveActionHint());

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
    [NotifyPropertyChangedFor(nameof(HeroAccentBrush))]
    [NotifyPropertyChangedFor(nameof(HeroBadgeBackground))]
    [NotifyPropertyChangedFor(nameof(HeroBadgeBorderBrush))]
    [NotifyPropertyChangedFor(nameof(PausedCardTitle))]
    public partial InfoBarSeverity StatusSeverity { get; set; }

    /// <summary>Letter-spaced mode label above the headline (e.g. "RECOVERY MODE"), set per state.</summary>
    [ObservableProperty]
    public partial string Eyebrow { get; set; } = "CHECKING SERVICE";

    // Brushes are derived at binding time (UI thread) — never stored (see uno-vm-thread-affinity).
    public Brush HeroAccentBrush => StatusSeverity switch
    {
        InfoBarSeverity.Error => AppThemeBrushes.Get("StatusErrorTextBrush", AppThemeBrushes.SeverityCriticalColor),
        InfoBarSeverity.Warning => AppThemeBrushes.Get("StatusWarningBrush", AppThemeBrushes.StatusWarningColor),
        InfoBarSeverity.Success => AppThemeBrushes.Get("StatusSuccessBrush", AppThemeBrushes.StatusSuccessColor),
        _ => AppThemeBrushes.Get("StatusInfoBrush", AppThemeBrushes.StatusInfoColor),
    };

    public Brush HeroBadgeBackground => TintBrush(SeverityColor(StatusSeverity), 0x26);

    public Brush HeroBadgeBorderBrush => TintBrush(SeverityColor(StatusSeverity), 0x55);

    public string PausedCardTitle => StatusSeverity switch
    {
        InfoBarSeverity.Error => "Paused in recovery mode",
        InfoBarSeverity.Warning => "Paused in limited mode",
        InfoBarSeverity.Success => "Nothing is paused",
        _ => "Paused in read-only mode",
    };

    private static Color SeverityColor(InfoBarSeverity severity) => severity switch
    {
        InfoBarSeverity.Error => AppThemeBrushes.SeverityCriticalColor,
        InfoBarSeverity.Warning => AppThemeBrushes.StatusWarningColor,
        InfoBarSeverity.Success => AppThemeBrushes.StatusSuccessColor,
        _ => AppThemeBrushes.StatusInfoColor,
    };

    private static Brush TintBrush(Color color, byte alpha)
        => new SolidColorBrush(Color.FromArgb(alpha, color.R, color.G, color.B));

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
    [NotifyPropertyChangedFor(nameof(DisabledControlOneVisibility))]
    public partial string DisabledControlOne { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisabledControlTwoVisibility))]
    public partial string DisabledControlTwo { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisabledControlThreeVisibility))]
    public partial string DisabledControlThree { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisabledControlFourVisibility))]
    public partial string DisabledControlFour { get; set; } = string.Empty;

    public Visibility DisabledControlOneVisibility => string.IsNullOrEmpty(DisabledControlOne) ? Visibility.Collapsed : Visibility.Visible;

    public Visibility DisabledControlTwoVisibility => string.IsNullOrEmpty(DisabledControlTwo) ? Visibility.Collapsed : Visibility.Visible;

    public Visibility DisabledControlThreeVisibility => string.IsNullOrEmpty(DisabledControlThree) ? Visibility.Collapsed : Visibility.Visible;

    public Visibility DisabledControlFourVisibility => string.IsNullOrEmpty(DisabledControlFour) ? Visibility.Collapsed : Visibility.Visible;

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
    [NotifyPropertyChangedFor(nameof(CanManageInstalledService))]
    [NotifyCanExecuteChangedFor(nameof(RestartServiceCommand))]
    [NotifyCanExecuteChangedFor(nameof(InstallServiceCommand))]
    [NotifyCanExecuteChangedFor(nameof(UpdateServiceCommand))]
    public partial bool IsOperationInProgress { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanManageInstalledService))]
    [NotifyCanExecuteChangedFor(nameof(RestartServiceCommand))]
    [NotifyCanExecuteChangedFor(nameof(InstallServiceCommand))]
    [NotifyCanExecuteChangedFor(nameof(UpdateServiceCommand))]
    public partial bool IsServiceControlSupported { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanManageInstalledService))]
    [NotifyPropertyChangedFor(nameof(RestartButtonVisibility))]
    [NotifyPropertyChangedFor(nameof(RegistrationDisplay))]
    [NotifyPropertyChangedFor(nameof(RegistrationBrush))]
    [NotifyCanExecuteChangedFor(nameof(RestartServiceCommand))]
    public partial bool IsServiceInstalled { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(InstallButtonVisibility))]
    [NotifyCanExecuteChangedFor(nameof(InstallServiceCommand))]
    public partial bool CanInstallService { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UpdateButtonVisibility))]
    [NotifyCanExecuteChangedFor(nameof(UpdateServiceCommand))]
    public partial bool CanUpdateService { get; set; }

    [ObservableProperty]
    public partial string ServiceIdentity { get; set; } = "SubZeroFrameworkService";

    public bool CanManageInstalledService => IsServiceControlSupported && IsServiceInstalled && !IsOperationInProgress;

    // Detected-state card rows: registration goes red when missing, green when present (design).
    public string RegistrationDisplay => IsServiceInstalled ? "Installed" : "Not installed";

    public Brush RegistrationBrush => IsServiceInstalled
        ? AppThemeBrushes.Get("StatusSuccessBrush", AppThemeBrushes.StatusSuccessColor)
        : AppThemeBrushes.Get("StatusErrorTextBrush", AppThemeBrushes.SeverityCriticalColor);

    // The design shows Install (primary) + Restart; Update appears only when the packaged helper is newer.
    public Visibility InstallButtonVisibility => CanInstallService ? Visibility.Visible : Visibility.Collapsed;

    public Visibility RestartButtonVisibility => IsServiceInstalled ? Visibility.Visible : Visibility.Collapsed;

    public Visibility UpdateButtonVisibility => CanUpdateService ? Visibility.Visible : Visibility.Collapsed;

    public IAsyncRelayCommand RestartServiceCommand { get; }

    public IAsyncRelayCommand InstallServiceCommand { get; }

    public IAsyncRelayCommand UpdateServiceCommand { get; }

    public IRelayCommand RecheckCommand { get; }

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

        if (!status.IsGrpcActive)
        {
            var serviceIsInstalled = IsServiceInstalled;

            ApplyPageState(
                title: serviceIsInstalled ? "Background service offline" : "Background service not installed",
                message: serviceIsInstalled
                    ? "SubZeroFramework.Service is not reachable, so the app has switched to a safe recovery mode."
                    : "SubZeroFramework.Service is not installed, so the app has switched to a safe recovery mode.",
                severity: InfoBarSeverity.Error,
                stateLineOne: $"Manager: {PlatformServiceManager}",
                stateLineTwo: serviceIsInstalled ? InstallSourceSummary : "Service registration is not currently installed.",
                stateLineThree: serviceIsInstalled ? InstallReadinessMessage : InstallSourceSummary,
                disabledControlOne: "Live telemetry and history refresh",
                disabledControlTwo: "Manual fan control and direct write actions",
                disabledControlThree: "Curve, profile, and override changes",
                disabledControlFour: "Service-backed inventory refresh and hardware checks",
                explanation: serviceIsInstalled
                    ? "SubZeroFramework.Service was not found running, or the installed service is not currently reachable over local gRPC IPC. Until it is installed and running again, the UI stays read-only so it does not surface stale telemetry or unsafe control actions."
                    : "SubZeroFramework.Service is not currently installed. Until it is installed and reachable again, the UI stays read-only so it does not surface stale telemetry or unsafe control actions.",
                actionHint: ResolveActionHint(),
                eyebrow: "RECOVERY MODE");
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
                actionHint: ResolveActionHint(),
                eyebrow: "RECOVERY MODE");
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
                actionHint: ResolveActionHint(),
                eyebrow: "LIMITED MODE");
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
                actionHint: "Use the service actions only if you need to repair the service installation. Hardware-control features stay disabled on unsupported devices.",
                eyebrow: "UNSUPPORTED DEVICE");
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
                actionHint: ResolveActionHint(),
                eyebrow: "SERVICE WARNING");
            return;
        }

        if (!status.IsFanControlEnabled)
        {
            ApplyPageState(
                title: "Fan control disabled",
                message: status.FanControlAuthorizationMessage ?? "Fan-control commands are switched off. Turn on \"Allow fan control commands\" under Settings → Service, then apply.",
                severity: InfoBarSeverity.Informational,
                stateLineOne: $"Manager: {PlatformServiceManager}",
                stateLineTwo: InstallSourceSummary,
                stateLineThree: "The service is reachable, but the command boundary is currently read-only.",
                disabledControlOne: "Manual fan RPM or duty writes",
                disabledControlTwo: "Custom curve activation",
                disabledControlThree: "Fan override persistence changes",
                disabledControlFour: "Any control that mutates EC fan state",
                explanation: "The service is online, but fan-control commands are intentionally disabled for this session or deployment. The page stays in read-only mode so you can inspect the state without surfacing unsafe or unavailable write actions.",
                actionHint: "Enable \"Allow fan control commands\" under Settings → Service to turn fan control on; lifecycle actions remain available meanwhile.",
                eyebrow: "READ-ONLY MODE");
            return;
        }

        // NOTE: there is deliberately no page state keyed on HasCallerIdentityValidation. An earlier
        // "LIMITED MODE" warning fired whenever that flag was false — which in this release is ALWAYS —
        // and claimed mutating fan controls were disabled, which was untrue (nothing client-side gates on
        // the flag; fan control works). It permanently shadowed the healthy state and read as an IPC
        // error: the first Linux tester concluded fan control was broken. The shipped authorization
        // posture is a documented decision (SECURITY.md, Docs/IpcAuthorizationAndUiCadence.md) and is
        // surfaced honestly in the healthy state line below, not dressed up as a degraded mode.
        ApplyPageState(
            title: "Background service healthy",
            message: "Status streaming is connected and the service reports healthy Framework telemetry access.",
            severity: InfoBarSeverity.Success,
            stateLineOne: $"Manager: {PlatformServiceManager}",
            stateLineTwo: InstallSourceSummary,
            stateLineThree: status.HasCallerIdentityValidation
                ? "The service is online and normal control surfaces should be available."
                : "The service is online and normal control surfaces should be available. Fan-control authorization is enforced by the service itself on the local-only socket (see SECURITY.md).",
            disabledControlOne: "No fallback-only restrictions are currently active",
            disabledControlTwo: "Warnings and Issues can now yield to the normal pages",
            disabledControlThree: string.Empty,
            disabledControlFour: string.Empty,
            explanation: "This page is intended for degraded or unsupported states. If you are seeing the healthy state here, navigation should be able to return to the standard control surfaces.",
            actionHint: "No remediation is currently required.",
            eyebrow: "ALL CLEAR");
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
        string actionHint,
        string eyebrow)
    {
        Eyebrow = eyebrow;
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

    private string ResolveActionHint()
    {
        if (CanInstallService || CanUpdateService || (IsServiceControlSupported && IsServiceInstalled))
        {
            return PrivilegePromptMessage;
        }

        return InstallReadinessMessage;
    }

    private InfoBarSeverity MapSeverity(FrameworkServiceCommandResultKind kind)
        => kind switch
        {
            FrameworkServiceCommandResultKind.Success => InfoBarSeverity.Success,
            FrameworkServiceCommandResultKind.Warning => InfoBarSeverity.Warning,
            _ => InfoBarSeverity.Error,
        };
}
