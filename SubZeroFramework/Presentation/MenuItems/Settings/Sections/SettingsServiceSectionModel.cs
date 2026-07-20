using System.Globalization;
using System.Reactive.Disposables;
using System.Reactive.Disposables.Fluent;
using System.Reactive.Linq;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.WinUI;

using Material.Icons;

using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

using SubZeroFramework.Services;
using SubZeroFramework.Services.Navigation;
using SubZeroFramework.Themes;

using Windows.UI;

namespace SubZeroFramework.Presentation.MenuItems.Settings.Sections;

/// <summary>
/// ViewModel for the Service section: the reachability banner, the packaged-service lifecycle actions, and
/// the service-owned runtime configuration. Navigation constructs it (ViewMap-registered); stream callbacks
/// marshal to the UI thread before writing observable properties, and user-initiated work awaits without
/// ConfigureAwait(false) so its continuations stay on the UI thread. The page that navigated here disposes
/// it when another section takes over.
/// </summary>
public partial class SettingsServiceSectionModel : ObservableObject, IUnsavedChangesGuard, IDisposable
{
    private readonly CompositeDisposable _subscriptions = [];
    private readonly IFrameworkServiceControlClient _serviceControlClient;
    private readonly IFrameworkServiceConfigurationClient _serviceConfigurationClient;

    public SettingsServiceSectionModel(
        IFrameworkStatusClient frameworkStatusClient,
        IFrameworkServiceControlClient serviceControlClient,
        IFrameworkServiceConfigurationClient serviceConfigurationClient,
        DispatcherQueue dispatcherQueue)
    {
        ArgumentNullException.ThrowIfNull(frameworkStatusClient);
        ArgumentNullException.ThrowIfNull(serviceControlClient);
        ArgumentNullException.ThrowIfNull(serviceConfigurationClient);
        ArgumentNullException.ThrowIfNull(dispatcherQueue);

        _serviceControlClient = serviceControlClient;
        _serviceConfigurationClient = serviceConfigurationClient;

        LastStatusObservedAt = frameworkStatusClient.LastObservedAt is DateTimeOffset observedAt
            ? observedAt.LocalDateTime.ToString("T", CultureInfo.CurrentCulture)
            : "waiting for status";

        ShutdownServiceCommand = new AsyncRelayCommand(() => ExecuteServiceActionAsync(_serviceControlClient.ShutdownAsync), CanRunInstalledServiceAction);
        RestartServiceCommand = new AsyncRelayCommand(() => ExecuteServiceActionAsync(_serviceControlClient.RestartAsync), CanRunInstalledServiceAction);
        InstallServiceCommand = new AsyncRelayCommand(() => ExecuteServiceActionAsync(_serviceControlClient.InstallAsync), CanRunInstallAction);
        UpdateServiceCommand = new AsyncRelayCommand(() => ExecuteServiceActionAsync(_serviceControlClient.UpdateAsync), CanRunUpdateAction);
        UninstallServiceCommand = new AsyncRelayCommand(() => ExecuteServiceActionAsync(_serviceControlClient.UninstallAsync), CanRunUninstallAction);
        RecheckServiceCommand = new RelayCommand(RecheckService);
        ApplyConfigurationCommand = new AsyncRelayCommand(ApplyConfigurationAsync, CanRunApplyConfigurationAction);
        SaveConfigurationCommand = new AsyncRelayCommand(SaveConfigurationAsync, CanRunSaveConfigurationAction);
        ResetConfigurationCommand = new RelayCommand(ResetConfiguration, CanRunResetConfigurationAction);

        ApplyServiceControlInfo(_serviceControlClient.GetInfo());

        frameworkStatusClient
            .WatchStatus()
            .Select(status => Observable.FromAsync(_ => dispatcherQueue.EnqueueAsync(() => ApplyStatus(status))))
            .Concat()
            .Subscribe()
            .DisposeWith(_subscriptions);

        serviceConfigurationClient
            .WatchConfiguration()
            .Select(snapshot => Observable.FromAsync(_ => dispatcherQueue.EnqueueAsync(() => ApplyConfigurationSnapshot(snapshot))))
            .Concat()
            .Subscribe()
            .DisposeWith(_subscriptions);
    }

    // ----- Reachability banner -----

    [ObservableProperty]
    public partial string LastStatusObservedAt { get; set; }

    [ObservableProperty]
    public partial string BannerTitle { get; set; } = "Checking service health";

    [ObservableProperty]
    public partial string BannerDetail { get; set; } = "Waiting for status stream updates from SubZeroFramework.Service.";

    // Brushes are derived (never stored): creating a SolidColorBrush is only legal on the UI thread, and
    // computed getters evaluate at binding time instead.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BannerBackground))]
    [NotifyPropertyChangedFor(nameof(BannerBorderBrush))]
    [NotifyPropertyChangedFor(nameof(BannerForeground))]
    [NotifyPropertyChangedFor(nameof(BannerIconKind))]
    public partial InfoBarSeverity ServiceStateSeverity { get; set; } = InfoBarSeverity.Informational;

    public Brush BannerBackground => TintBrush(SeverityColor(ServiceStateSeverity), 0x24);

    public Brush BannerBorderBrush => TintBrush(SeverityColor(ServiceStateSeverity), 0x55);

    public Brush BannerForeground => SeverityForegroundBrush(ServiceStateSeverity);

    public MaterialIconKind BannerIconKind => ServiceStateSeverity switch
    {
        InfoBarSeverity.Success => MaterialIconKind.CheckDecagram,
        InfoBarSeverity.Warning => MaterialIconKind.AlertOutline,
        InfoBarSeverity.Error => MaterialIconKind.AlertOctagonOutline,
        _ => MaterialIconKind.InformationOutline,
    };

    private static Color SeverityColor(InfoBarSeverity severity) => severity switch
    {
        InfoBarSeverity.Success => AppThemeBrushes.StatusSuccessColor,
        InfoBarSeverity.Warning => AppThemeBrushes.StatusWarningColor,
        InfoBarSeverity.Error => AppThemeBrushes.SeverityCriticalColor,
        _ => AppThemeBrushes.StatusInfoColor,
    };

    private static Brush SeverityForegroundBrush(InfoBarSeverity severity) => severity switch
    {
        InfoBarSeverity.Success => AppThemeBrushes.Get("StatusSuccessBrush", AppThemeBrushes.StatusSuccessColor),
        InfoBarSeverity.Warning => AppThemeBrushes.Get("StatusWarningBrush", AppThemeBrushes.StatusWarningColor),
        InfoBarSeverity.Error => AppThemeBrushes.Get("StatusErrorTextBrush", AppThemeBrushes.SeverityCriticalColor),
        _ => AppThemeBrushes.Get("StatusInfoBrush", AppThemeBrushes.StatusInfoColor),
    };

    private static Brush TintBrush(Color color, byte alpha)
        => new SolidColorBrush(Color.FromArgb(alpha, color.R, color.G, color.B));

    private void ApplyStatus(FrameworkSystemStatus status)
    {
        LastStatusObservedAt = status.ObservedAt.LocalDateTime.ToString("T", CultureInfo.CurrentCulture);

        if (!status.IsGrpcActive)
        {
            SetServiceState(
                InfoBarSeverity.Error,
                IsServiceInstalled ? "Service offline" : "Service not installed",
                IsServiceInstalled
                    ? status.LastError ?? "The client cannot reach SubZeroFramework.Service over local gRPC IPC."
                    : "SubZeroFramework.Service is not currently installed.");
            return;
        }

        if (!status.IsLibraryAvailable)
        {
            SetServiceState(InfoBarSeverity.Error, "Service running with library issue", status.LastError ?? "The service is running, but FrameworkDotnet could not be loaded.");
            return;
        }

        if (status.RequiresElevation)
        {
            SetServiceState(InfoBarSeverity.Warning, "Service requires elevation", "The service is running without the privileges required for Framework EC access.");
            return;
        }

        if (!string.IsNullOrEmpty(status.LastError))
        {
            SetServiceState(InfoBarSeverity.Warning, "Service warning", status.LastError);
            return;
        }

        SetServiceState(InfoBarSeverity.Success, "Reachable over local gRPC", $"{ServiceIdentity} — last check {LastStatusObservedAt}");
    }

    private void SetServiceState(InfoBarSeverity severity, string title, string detail)
    {
        BannerTitle = title;
        BannerDetail = severity == InfoBarSeverity.Success ? detail : $"{detail} — last check {LastStatusObservedAt}";
        ServiceStateSeverity = severity;
    }

    // ----- Service lifecycle -----

    [ObservableProperty]
    public partial string ServiceIdentity { get; set; } = "SubZeroFrameworkService";

    [ObservableProperty]
    public partial string PrivilegePromptMessage { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string InstallReadinessMessage { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string LastActionTitle { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string LastActionMessage { get; set; } = string.Empty;

    [ObservableProperty]
    public partial InfoBarSeverity LastActionSeverity { get; set; } = InfoBarSeverity.Informational;

    [ObservableProperty]
    public partial bool IsLastActionVisible { get; set; }

    [ObservableProperty]
    public partial bool IsOperationInProgress { get; set; }

    [ObservableProperty]
    public partial bool IsServiceControlSupported { get; set; }

    [ObservableProperty]
    public partial bool IsServiceInstalled { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(InstallButtonVisibility))]
    public partial bool CanInstallService { get; set; }

    [ObservableProperty]
    public partial bool CanUpdateService { get; set; }

    [ObservableProperty]
    public partial bool CanUninstallService { get; set; }

    [ObservableProperty]
    public partial bool PackagedHelperAvailable { get; set; }

    public Visibility InstallButtonVisibility => CanInstallService ? Visibility.Visible : Visibility.Collapsed;

    public IAsyncRelayCommand ShutdownServiceCommand { get; }

    public IAsyncRelayCommand RestartServiceCommand { get; }

    public IAsyncRelayCommand InstallServiceCommand { get; }

    public IAsyncRelayCommand UpdateServiceCommand { get; }

    public IAsyncRelayCommand UninstallServiceCommand { get; }

    public IRelayCommand RecheckServiceCommand { get; }

    partial void OnIsOperationInProgressChanged(bool value) => RefreshCommandStates();

    partial void OnIsServiceControlSupportedChanged(bool value) => RefreshCommandStates();

    partial void OnIsServiceInstalledChanged(bool value) => RefreshCommandStates();

    partial void OnCanInstallServiceChanged(bool value) => RefreshCommandStates();

    partial void OnCanUpdateServiceChanged(bool value) => RefreshCommandStates();

    partial void OnCanUninstallServiceChanged(bool value) => RefreshCommandStates();

    private void RefreshCommandStates()
    {
        ShutdownServiceCommand.NotifyCanExecuteChanged();
        RestartServiceCommand.NotifyCanExecuteChanged();
        InstallServiceCommand.NotifyCanExecuteChanged();
        UpdateServiceCommand.NotifyCanExecuteChanged();
        UninstallServiceCommand.NotifyCanExecuteChanged();
        ApplyConfigurationCommand.NotifyCanExecuteChanged();
        SaveConfigurationCommand.NotifyCanExecuteChanged();
        ResetConfigurationCommand.NotifyCanExecuteChanged();
    }

    private bool CanRunInstalledServiceAction()
        => IsServiceControlSupported && IsServiceInstalled && !IsOperationInProgress;

    private bool CanRunInstallAction()
        => IsServiceControlSupported && CanInstallService && !IsOperationInProgress;

    private bool CanRunUpdateAction()
        => IsServiceControlSupported && CanUpdateService && !IsOperationInProgress;

    private bool CanRunUninstallAction()
        => IsServiceControlSupported && CanUninstallService && !IsOperationInProgress;

    private async Task ExecuteServiceActionAsync(Func<CancellationToken, Task<FrameworkServiceCommandResult>> action)
    {
        if (IsOperationInProgress)
        {
            return;
        }

        IsOperationInProgress = true;

        try
        {
            // Commands run on the UI thread; without ConfigureAwait(false) the continuation returns there,
            // so the observable-property writes below are UI-thread-safe.
            var result = await action(CancellationToken.None);
            ApplyServiceControlInfo(_serviceControlClient.GetInfo());
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

    private void RecheckService()
    {
        ApplyServiceControlInfo(_serviceControlClient.GetInfo());
        LastActionTitle = "Recheck service";
        LastActionMessage = $"Service manager re-queried at {DateTimeOffset.Now.LocalDateTime.ToString("T", CultureInfo.CurrentCulture)}. Streamed status refreshes continuously.";
        LastActionSeverity = InfoBarSeverity.Informational;
        IsLastActionVisible = true;
    }

    private void ApplyServiceControlInfo(FrameworkServiceControlInfo serviceInfo)
    {
        ServiceIdentity = serviceInfo.ServiceIdentity;
        PrivilegePromptMessage = serviceInfo.PrivilegePromptMessage;
        InstallReadinessMessage = serviceInfo.InstallReadinessMessage;
        IsServiceControlSupported = serviceInfo.IsSupported;
        IsServiceInstalled = serviceInfo.IsInstalled;
        CanInstallService = serviceInfo.CanInstall;
        CanUpdateService = serviceInfo.CanUpdate;
        CanUninstallService = serviceInfo.CanUninstall;
        PackagedHelperAvailable = serviceInfo.PackagedHelperAvailable;
    }

    private static InfoBarSeverity MapSeverity(FrameworkServiceCommandResultKind kind)
        => kind switch
        {
            FrameworkServiceCommandResultKind.Success => InfoBarSeverity.Success,
            FrameworkServiceCommandResultKind.Warning => InfoBarSeverity.Warning,
            _ => InfoBarSeverity.Error,
        };

    // ----- Service runtime configuration (service-owned settings) -----

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ConfigurationValidationMessage))]
    [NotifyPropertyChangedFor(nameof(HasConfigurationValidationError))]
    [NotifyPropertyChangedFor(nameof(ConfigurationValidationVisibility))]
    public partial string TelemetryPollingIntervalMillisecondsText { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ConfigurationValidationMessage))]
    [NotifyPropertyChangedFor(nameof(HasConfigurationValidationError))]
    [NotifyPropertyChangedFor(nameof(ConfigurationValidationVisibility))]
    public partial string HardwareInfoPollingIntervalMillisecondsText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool AllowFanControlCommandsDraft { get; set; }

    [ObservableProperty]
    public partial bool IsConfigurationLoaded { get; set; }

    [ObservableProperty]
    public partial bool IsConfigurationOperationInProgress { get; set; }

    [ObservableProperty]
    public partial bool HasUnsavedConfigurationChanges { get; set; }

    [ObservableProperty]
    public partial string ConfigurationActionTitle { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ConfigurationActionMessage { get; set; } = string.Empty;

    [ObservableProperty]
    public partial InfoBarSeverity ConfigurationActionSeverity { get; set; } = InfoBarSeverity.Informational;

    [ObservableProperty]
    public partial bool IsConfigurationActionVisible { get; set; }

    [ObservableProperty]
    public partial FrameworkServiceConfigurationSnapshot? CurrentConfigurationSnapshot { get; set; }

    public string ConfigurationValidationMessage
    {
        get
        {
            _ = TryBuildDraftConfiguration(out _, out var validationError);
            return validationError;
        }
    }

    public bool HasConfigurationValidationError => !string.IsNullOrEmpty(ConfigurationValidationMessage);

    public Visibility ConfigurationValidationVisibility => HasConfigurationValidationError ? Visibility.Visible : Visibility.Collapsed;

    public bool HasConfigurationChanges
    {
        get
        {
            if (CurrentConfigurationSnapshot is null)
            {
                return false;
            }

            var currentTelemetryText = FormatMilliseconds(CurrentConfigurationSnapshot.PollingInterval);
            var currentHardwareInfoText = FormatMilliseconds(CurrentConfigurationSnapshot.HardwareInfoPollingInterval);

            return !string.Equals(TelemetryPollingIntervalMillisecondsText?.Trim(), currentTelemetryText, StringComparison.Ordinal)
                || !string.Equals(HardwareInfoPollingIntervalMillisecondsText?.Trim(), currentHardwareInfoText, StringComparison.Ordinal)
                || AllowFanControlCommandsDraft != CurrentConfigurationSnapshot.AllowFanControlCommands;
        }
    }

    public IAsyncRelayCommand ApplyConfigurationCommand { get; }

    public IAsyncRelayCommand SaveConfigurationCommand { get; }

    public IRelayCommand ResetConfigurationCommand { get; }

    partial void OnTelemetryPollingIntervalMillisecondsTextChanged(string value) => RefreshCommandStates();

    partial void OnHardwareInfoPollingIntervalMillisecondsTextChanged(string value) => RefreshCommandStates();

    partial void OnAllowFanControlCommandsDraftChanged(bool value) => RefreshCommandStates();

    partial void OnIsConfigurationLoadedChanged(bool value) => RefreshCommandStates();

    partial void OnIsConfigurationOperationInProgressChanged(bool value) => RefreshCommandStates();

    partial void OnHasUnsavedConfigurationChangesChanged(bool value) => RefreshCommandStates();

    private bool CanRunApplyConfigurationAction()
        => IsServiceControlSupported
            && IsConfigurationLoaded
            && !IsOperationInProgress
            && !IsConfigurationOperationInProgress
            && HasConfigurationChanges
            && !HasConfigurationValidationError;

    private bool CanRunSaveConfigurationAction()
        => IsServiceControlSupported
            && IsConfigurationLoaded
            && !IsOperationInProgress
            && !IsConfigurationOperationInProgress
            && HasUnsavedConfigurationChanges;

    private bool CanRunResetConfigurationAction()
        => IsConfigurationLoaded && !IsOperationInProgress && !IsConfigurationOperationInProgress && HasConfigurationChanges;

    private async Task ApplyConfigurationAsync()
    {
        if (!TryBuildDraftConfiguration(out var request, out var validationError))
        {
            ApplyConfigurationActionResult("Apply service configuration", validationError, InfoBarSeverity.Error);
            return;
        }

        IsConfigurationOperationInProgress = true;

        try
        {
            var result = await _serviceConfigurationClient.ApplyConfigurationAsync(request, CancellationToken.None);

            if (result.Configuration is not null)
            {
                CurrentConfigurationSnapshot = result.Configuration;
                IsConfigurationLoaded = true;

                if (result.Succeeded)
                {
                    ApplyConfigurationDraft(result.Configuration);
                    HasUnsavedConfigurationChanges = true;
                }
            }

            ApplyConfigurationActionResult("Apply service configuration", result.Message, result.Succeeded ? InfoBarSeverity.Success : InfoBarSeverity.Error);
        }
        catch (Exception exception)
        {
            ApplyConfigurationActionResult("Apply service configuration", exception.Message, InfoBarSeverity.Error);
        }
        finally
        {
            IsConfigurationOperationInProgress = false;
        }
    }

    private async Task SaveConfigurationAsync()
    {
        IsConfigurationOperationInProgress = true;

        try
        {
            var result = await _serviceConfigurationClient.SaveConfigurationAsync(CancellationToken.None);

            if (result.Succeeded)
            {
                HasUnsavedConfigurationChanges = false;
            }

            ApplyConfigurationActionResult("Save service configuration", result.Message, result.Succeeded ? InfoBarSeverity.Success : InfoBarSeverity.Error);
        }
        catch (Exception exception)
        {
            ApplyConfigurationActionResult("Save service configuration", exception.Message, InfoBarSeverity.Error);
        }
        finally
        {
            IsConfigurationOperationInProgress = false;
        }
    }

    private void ResetConfiguration()
    {
        if (CurrentConfigurationSnapshot is not null)
        {
            ApplyConfigurationDraft(CurrentConfigurationSnapshot);
        }
    }

    // ----- Navigation guard: warn before leaving with an unapplied runtime-configuration draft -----

    bool IUnsavedChangesGuard.HasUnsavedChanges => HasConfigurationChanges;

    Task IUnsavedChangesGuard.DiscardUnsavedChangesAsync()
    {
        ResetConfiguration();
        return Task.CompletedTask;
    }

    private void ApplyConfigurationSnapshot(FrameworkServiceConfigurationSnapshot snapshot)
    {
        var shouldRefreshDraft = !IsConfigurationLoaded || !HasConfigurationChanges || IsConfigurationOperationInProgress;

        CurrentConfigurationSnapshot = snapshot;
        IsConfigurationLoaded = true;

        if (shouldRefreshDraft)
        {
            ApplyConfigurationDraft(snapshot);
        }
    }

    private void ApplyConfigurationDraft(FrameworkServiceConfigurationSnapshot snapshot)
    {
        TelemetryPollingIntervalMillisecondsText = FormatMilliseconds(snapshot.PollingInterval);
        HardwareInfoPollingIntervalMillisecondsText = FormatMilliseconds(snapshot.HardwareInfoPollingInterval);
        AllowFanControlCommandsDraft = snapshot.AllowFanControlCommands;
    }

    private void ApplyConfigurationActionResult(string title, string message, InfoBarSeverity severity)
    {
        ConfigurationActionTitle = title;
        ConfigurationActionMessage = message;
        ConfigurationActionSeverity = severity;
        IsConfigurationActionVisible = true;
    }

    private bool TryBuildDraftConfiguration(out FrameworkServiceConfigurationApplyRequest request, out string validationError)
    {
        request = null!;

        if (!long.TryParse(TelemetryPollingIntervalMillisecondsText?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var pollingIntervalMilliseconds))
        {
            validationError = "Telemetry polling interval must be a whole number of milliseconds.";
            return false;
        }

        if (!long.TryParse(HardwareInfoPollingIntervalMillisecondsText?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var hardwareInfoPollingIntervalMilliseconds))
        {
            validationError = "Hardware info polling interval must be a whole number of milliseconds.";
            return false;
        }

        if (pollingIntervalMilliseconds <= 0 || hardwareInfoPollingIntervalMilliseconds <= 0)
        {
            validationError = "Polling intervals must be greater than zero milliseconds.";
            return false;
        }

        request = new FrameworkServiceConfigurationApplyRequest
        {
            PollingInterval = TimeSpan.FromMilliseconds(pollingIntervalMilliseconds),
            HardwareInfoPollingInterval = TimeSpan.FromMilliseconds(hardwareInfoPollingIntervalMilliseconds),
            AllowFanControlCommands = AllowFanControlCommandsDraft,
        };

        validationError = string.Empty;
        return true;
    }

    private static string FormatMilliseconds(TimeSpan timeSpan)
        => checked((long)Math.Round(timeSpan.TotalMilliseconds, MidpointRounding.AwayFromZero)).ToString(CultureInfo.InvariantCulture);

    public void Dispose()
    {
        _subscriptions.Dispose();
    }
}
