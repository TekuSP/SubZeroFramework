using System.Globalization;
using System.Reactive.Disposables;
using System.Reactive.Disposables.Fluent;
using System.Reactive.Linq;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.WinUI;

using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

using SubZeroFramework.Services;

namespace SubZeroFramework.Presentation.MenuItems.Settings;

public partial class SettingsModel : ObservableObject, IDisposable
{
    private readonly CompositeDisposable _subscriptions = [];
    private readonly IFrameworkServiceControlClient _frameworkServiceControlClient;
    private readonly IFrameworkServiceConfigurationClient _frameworkServiceConfigurationClient;
    private readonly DispatcherQueue _dispatcherQueue;

    public SettingsModel(
        IStringLocalizer localizer,
        IOptions<AppConfig> appInfo,
        IFrameworkStatusClient frameworkStatusClient,
        IFrameworkServiceControlClient frameworkServiceControlClient,
        IFrameworkServiceConfigurationClient frameworkServiceConfigurationClient,
        DispatcherQueue dispatcherQueue)
    {
        ArgumentNullException.ThrowIfNull(frameworkServiceControlClient);
        ArgumentNullException.ThrowIfNull(frameworkServiceConfigurationClient);
        ArgumentNullException.ThrowIfNull(dispatcherQueue);

        _frameworkServiceControlClient = frameworkServiceControlClient;
        _frameworkServiceConfigurationClient = frameworkServiceConfigurationClient;
        _dispatcherQueue = dispatcherQueue;

        EndpointValidationMessage = frameworkStatusClient.EndpointValidation.Message;
        LastStatusObservedAt = frameworkStatusClient.LastObservedAt is DateTimeOffset observedAt
            ? observedAt.LocalDateTime.ToString("T", CultureInfo.CurrentCulture)
            : "No status received yet";
        ServiceStateTitle = "Checking service health";
        ServiceStateMessage = "Waiting for status stream updates from SubZeroFramework.Service.";
        ServiceStateSeverity = InfoBarSeverity.Informational;
        LastActionTitle = string.Empty;
        LastActionMessage = string.Empty;
        LastActionSeverity = InfoBarSeverity.Informational;
        ConfigurationSourcePath = string.Empty;

        ShutdownServiceCommand = new AsyncRelayCommand(() => ExecuteServiceActionAsync(_frameworkServiceControlClient.ShutdownAsync), CanRunInstalledServiceAction);
        RestartServiceCommand = new AsyncRelayCommand(() => ExecuteServiceActionAsync(_frameworkServiceControlClient.RestartAsync), CanRunInstalledServiceAction);
        InstallServiceCommand = new AsyncRelayCommand(() => ExecuteServiceActionAsync(_frameworkServiceControlClient.InstallAsync), CanRunInstallAction);
        UpdateServiceCommand = new AsyncRelayCommand(() => ExecuteServiceActionAsync(_frameworkServiceControlClient.UpdateAsync), CanRunUpdateAction);
        UninstallServiceCommand = new AsyncRelayCommand(() => ExecuteServiceActionAsync(_frameworkServiceControlClient.UninstallAsync), CanRunUninstallAction);
        ReinstallServiceCommand = new AsyncRelayCommand(() => ExecuteServiceActionAsync(_frameworkServiceControlClient.ReinstallAsync), CanRunReinstallAction);
        ToggleAutorunCommand = new AsyncRelayCommand(ToggleAutorunAsync, CanRunToggleAutorunAction);
        ApplyConfigurationCommand = new AsyncRelayCommand(ApplyConfigurationAsync, CanRunApplyConfigurationAction);
        ResetConfigurationCommand = new RelayCommand(ResetConfiguration, CanRunResetConfigurationAction);

        ApplyServiceControlInfo(_frameworkServiceControlClient.GetInfo());

        frameworkStatusClient
            .WatchStatus()
            .Select(status => Observable.FromAsync(_ => UpdateStatusAsync(status)))
            .Concat()
            .Subscribe()
            .DisposeWith(_subscriptions);

        frameworkServiceConfigurationClient
            .WatchConfiguration()
            .Select(snapshot => Observable.FromAsync(_ => UpdateConfigurationSnapshotAsync(snapshot)))
            .Concat()
            .Subscribe()
            .DisposeWith(_subscriptions);
    }

    [ObservableProperty]
    public partial string EndpointValidationMessage { get; set; }

    [ObservableProperty]
    public partial string LastStatusObservedAt { get; set; }

    [ObservableProperty]
    public partial string ServiceStateTitle { get; set; }

    [ObservableProperty]
    public partial string ServiceStateMessage { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ServiceErrorHeroVisibility))]
    [NotifyPropertyChangedFor(nameof(ServiceWarningHeroVisibility))]
    [NotifyPropertyChangedFor(nameof(ServiceSuccessHeroVisibility))]
    [NotifyPropertyChangedFor(nameof(ServiceInformationalHeroVisibility))]
    public partial InfoBarSeverity ServiceStateSeverity { get; set; }

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
    public partial InfoBarSeverity LastActionSeverity { get; set; }

    [ObservableProperty]
    public partial bool IsLastActionVisible { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanManageInstalledService))]
    [NotifyPropertyChangedFor(nameof(CanReinstallService))]
    [NotifyPropertyChangedFor(nameof(CanToggleAutorun))]
    [NotifyPropertyChangedFor(nameof(CanApplyConfiguration))]
    [NotifyPropertyChangedFor(nameof(CanResetConfiguration))]
    [NotifyCanExecuteChangedFor(nameof(ShutdownServiceCommand))]
    [NotifyCanExecuteChangedFor(nameof(RestartServiceCommand))]
    [NotifyCanExecuteChangedFor(nameof(InstallServiceCommand))]
    [NotifyCanExecuteChangedFor(nameof(UpdateServiceCommand))]
    [NotifyCanExecuteChangedFor(nameof(UninstallServiceCommand))]
    [NotifyCanExecuteChangedFor(nameof(ReinstallServiceCommand))]
    [NotifyCanExecuteChangedFor(nameof(ToggleAutorunCommand))]
    [NotifyCanExecuteChangedFor(nameof(ApplyConfigurationCommand))]
    [NotifyCanExecuteChangedFor(nameof(ResetConfigurationCommand))]
    public partial bool IsOperationInProgress { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanManageInstalledService))]
    [NotifyPropertyChangedFor(nameof(CanReinstallService))]
    [NotifyPropertyChangedFor(nameof(CanToggleAutorun))]
    [NotifyPropertyChangedFor(nameof(CanApplyConfiguration))]
    [NotifyPropertyChangedFor(nameof(CanResetConfiguration))]
    [NotifyCanExecuteChangedFor(nameof(ShutdownServiceCommand))]
    [NotifyCanExecuteChangedFor(nameof(RestartServiceCommand))]
    [NotifyCanExecuteChangedFor(nameof(InstallServiceCommand))]
    [NotifyCanExecuteChangedFor(nameof(UpdateServiceCommand))]
    [NotifyCanExecuteChangedFor(nameof(UninstallServiceCommand))]
    [NotifyCanExecuteChangedFor(nameof(ReinstallServiceCommand))]
    [NotifyCanExecuteChangedFor(nameof(ToggleAutorunCommand))]
    [NotifyCanExecuteChangedFor(nameof(ApplyConfigurationCommand))]
    [NotifyCanExecuteChangedFor(nameof(ResetConfigurationCommand))]
    public partial bool IsConfigurationOperationInProgress { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanManageInstalledService))]
    [NotifyPropertyChangedFor(nameof(CanReinstallService))]
    [NotifyPropertyChangedFor(nameof(CanToggleAutorun))]
    [NotifyPropertyChangedFor(nameof(CanApplyConfiguration))]
    [NotifyCanExecuteChangedFor(nameof(ShutdownServiceCommand))]
    [NotifyCanExecuteChangedFor(nameof(RestartServiceCommand))]
    [NotifyCanExecuteChangedFor(nameof(InstallServiceCommand))]
    [NotifyCanExecuteChangedFor(nameof(UpdateServiceCommand))]
    [NotifyCanExecuteChangedFor(nameof(UninstallServiceCommand))]
    [NotifyCanExecuteChangedFor(nameof(ReinstallServiceCommand))]
    [NotifyCanExecuteChangedFor(nameof(ToggleAutorunCommand))]
    [NotifyCanExecuteChangedFor(nameof(ApplyConfigurationCommand))]
    public partial bool IsServiceControlSupported { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanManageInstalledService))]
    [NotifyPropertyChangedFor(nameof(CanReinstallService))]
    [NotifyPropertyChangedFor(nameof(CanToggleAutorun))]
    [NotifyCanExecuteChangedFor(nameof(ShutdownServiceCommand))]
    [NotifyCanExecuteChangedFor(nameof(RestartServiceCommand))]
    [NotifyCanExecuteChangedFor(nameof(ReinstallServiceCommand))]
    [NotifyCanExecuteChangedFor(nameof(ToggleAutorunCommand))]
    public partial bool IsServiceInstalled { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(InstallServiceCommand))]
    public partial bool CanInstallService { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(UpdateServiceCommand))]
    public partial bool CanUpdateService { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(UninstallServiceCommand))]
    public partial bool CanUninstallService { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanReinstallService))]
    [NotifyCanExecuteChangedFor(nameof(ReinstallServiceCommand))]
    public partial bool PackagedHelperAvailable { get; set; }

    [ObservableProperty]
    public partial bool IsElevatedSession { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanToggleAutorun))]
    [NotifyPropertyChangedFor(nameof(AutorunStateTitle))]
    [NotifyPropertyChangedFor(nameof(AutorunStateDescription))]
    [NotifyCanExecuteChangedFor(nameof(ToggleAutorunCommand))]
    public partial bool? IsAutorunEnabled { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanApplyConfiguration))]
    [NotifyPropertyChangedFor(nameof(CanResetConfiguration))]
    [NotifyCanExecuteChangedFor(nameof(ApplyConfigurationCommand))]
    [NotifyCanExecuteChangedFor(nameof(ResetConfigurationCommand))]
    public partial bool IsConfigurationLoaded { get; set; }

    [ObservableProperty]
    public partial string ConfigurationSourcePath { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ConfigurationValidationMessage))]
    [NotifyPropertyChangedFor(nameof(HasConfigurationValidationError))]
    [NotifyPropertyChangedFor(nameof(HasConfigurationChanges))]
    [NotifyPropertyChangedFor(nameof(CanApplyConfiguration))]
    [NotifyPropertyChangedFor(nameof(CanResetConfiguration))]
    [NotifyPropertyChangedFor(nameof(TelemetryPollingSamplesPerSecondDisplay))]
    [NotifyCanExecuteChangedFor(nameof(ApplyConfigurationCommand))]
    [NotifyCanExecuteChangedFor(nameof(ResetConfigurationCommand))]
    public partial string TelemetryPollingIntervalMillisecondsText { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ConfigurationValidationMessage))]
    [NotifyPropertyChangedFor(nameof(HasConfigurationValidationError))]
    [NotifyPropertyChangedFor(nameof(HasConfigurationChanges))]
    [NotifyPropertyChangedFor(nameof(CanApplyConfiguration))]
    [NotifyPropertyChangedFor(nameof(CanResetConfiguration))]
    [NotifyPropertyChangedFor(nameof(HardwareInfoPollingSamplesPerSecondDisplay))]
    [NotifyCanExecuteChangedFor(nameof(ApplyConfigurationCommand))]
    [NotifyCanExecuteChangedFor(nameof(ResetConfigurationCommand))]
    public partial string HardwareInfoPollingIntervalMillisecondsText { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasConfigurationChanges))]
    [NotifyPropertyChangedFor(nameof(CanApplyConfiguration))]
    [NotifyPropertyChangedFor(nameof(CanResetConfiguration))]
    [NotifyPropertyChangedFor(nameof(FanControlConfigurationWarning))]
    [NotifyCanExecuteChangedFor(nameof(ApplyConfigurationCommand))]
    [NotifyCanExecuteChangedFor(nameof(ResetConfigurationCommand))]
    public partial bool AllowFanControlCommandsDraft { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RuntimeAuthorizationMessage))]
    [NotifyPropertyChangedFor(nameof(FanControlConfigurationWarning))]
    private partial FrameworkSystemStatus? ObservedServiceStatus { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasConfigurationChanges))]
    [NotifyPropertyChangedFor(nameof(CanApplyConfiguration))]
    [NotifyPropertyChangedFor(nameof(CanResetConfiguration))]
    [NotifyCanExecuteChangedFor(nameof(ApplyConfigurationCommand))]
    [NotifyCanExecuteChangedFor(nameof(ResetConfigurationCommand))]
    private partial FrameworkServiceConfigurationSnapshot? CurrentConfigurationSnapshot { get; set; }

    public Visibility ServiceErrorHeroVisibility => ServiceStateSeverity == InfoBarSeverity.Error ? Visibility.Visible : Visibility.Collapsed;

    public Visibility ServiceWarningHeroVisibility => ServiceStateSeverity == InfoBarSeverity.Warning ? Visibility.Visible : Visibility.Collapsed;

    public Visibility ServiceSuccessHeroVisibility => ServiceStateSeverity == InfoBarSeverity.Success ? Visibility.Visible : Visibility.Collapsed;

    public Visibility ServiceInformationalHeroVisibility => ServiceStateSeverity == InfoBarSeverity.Informational ? Visibility.Visible : Visibility.Collapsed;

    public bool CanManageInstalledService => IsServiceControlSupported && IsServiceInstalled && !IsOperationInProgress && !IsConfigurationOperationInProgress;

    public bool CanReinstallService => CanManageInstalledService && PackagedHelperAvailable;

    public bool CanToggleAutorun => CanManageInstalledService && IsAutorunEnabled.HasValue;

    public bool CanApplyConfiguration => IsServiceControlSupported
        && IsConfigurationLoaded
        && !IsOperationInProgress
        && !IsConfigurationOperationInProgress
        && HasConfigurationChanges
        && !HasConfigurationValidationError;

    public bool CanResetConfiguration => IsConfigurationLoaded && !IsOperationInProgress && !IsConfigurationOperationInProgress && HasConfigurationChanges;

    public string AutorunStateTitle => IsAutorunEnabled switch
    {
        true => "Autorun enabled",
        false => "Autorun disabled",
        _ => "Autorun state unavailable",
    };

    public string AutorunStateDescription => IsAutorunEnabled switch
    {
        true => "The operating system will start the service automatically during boot.",
        false => "The service currently requires a manual start or explicit lifecycle action after boot.",
        _ => "The current startup mode could not be determined from the local service manager.",
    };

    public string ConfigurationValidationMessage
    {
        get
        {
            _ = TryBuildDraftConfiguration(out _, out var validationError);
            return validationError;
        }
    }

    public bool HasConfigurationValidationError => !string.IsNullOrEmpty(ConfigurationValidationMessage);

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

    public string TelemetryPollingSamplesPerSecondDisplay => FormatSamplesPerSecondDisplay(TelemetryPollingIntervalMillisecondsText);

    public string HardwareInfoPollingSamplesPerSecondDisplay => FormatSamplesPerSecondDisplay(HardwareInfoPollingIntervalMillisecondsText);

    public string RuntimeAuthorizationMessage => ObservedServiceStatus?.FanControlAuthorizationMessage
        ?? "Waiting for the service to report the current fan-control authorization state.";

    public string FanControlConfigurationWarning => AllowFanControlCommandsDraft && ObservedServiceStatus is { HasCallerIdentityValidation: false }
        ? "Warning: the current IPC transport still does not expose caller identity validation. Enabling fan-control commands allows local clients on this endpoint to issue write requests."
        : RuntimeAuthorizationMessage;

    public IAsyncRelayCommand ShutdownServiceCommand { get; }

    public IAsyncRelayCommand RestartServiceCommand { get; }

    public IAsyncRelayCommand InstallServiceCommand { get; }

    public IAsyncRelayCommand UpdateServiceCommand { get; }

    public IAsyncRelayCommand UninstallServiceCommand { get; }

    public IAsyncRelayCommand ReinstallServiceCommand { get; }

    public IAsyncRelayCommand ToggleAutorunCommand { get; }

    public IAsyncRelayCommand ApplyConfigurationCommand { get; }

    public IRelayCommand ResetConfigurationCommand { get; }

    public void Dispose()
    {
        _subscriptions.Dispose();
    }

    private bool CanRunInstalledServiceAction()
        => CanManageInstalledService;

    private bool CanRunInstallAction()
        => IsServiceControlSupported && CanInstallService && !IsOperationInProgress && !IsConfigurationOperationInProgress;

    private bool CanRunUpdateAction()
        => IsServiceControlSupported && CanUpdateService && !IsOperationInProgress && !IsConfigurationOperationInProgress;

    private bool CanRunUninstallAction()
        => IsServiceControlSupported && CanUninstallService && !IsOperationInProgress && !IsConfigurationOperationInProgress;

    private bool CanRunReinstallAction()
        => CanReinstallService;

    private bool CanRunToggleAutorunAction()
        => CanToggleAutorun;

    private bool CanRunApplyConfigurationAction()
        => CanApplyConfiguration;

    private bool CanRunResetConfigurationAction()
        => CanResetConfiguration;

    private async Task ExecuteServiceActionAsync(Func<CancellationToken, Task<FrameworkServiceCommandResult>> action)
    {
        if (IsOperationInProgress || IsConfigurationOperationInProgress)
        {
            return;
        }

        IsOperationInProgress = true;

        try
        {
            var result = await action(CancellationToken.None).ConfigureAwait(false);
            await _dispatcherQueue.EnqueueAsync(() =>
            {
                ApplyServiceControlInfo(_frameworkServiceControlClient.GetInfo());
                ApplyActionResult(result.OperationName, result.Message, MapSeverity(result.Kind));

                if (ObservedServiceStatus is not null)
                {
                    ApplyStatus(ObservedServiceStatus);
                }
            });
        }
        finally
        {
            await _dispatcherQueue.EnqueueAsync(() => IsOperationInProgress = false);
        }
    }

    private async Task ToggleAutorunAsync()
    {
        if (!CanToggleAutorun || IsAutorunEnabled is null)
        {
            return;
        }

        await ExecuteServiceActionAsync(
            IsAutorunEnabled.Value
                ? _frameworkServiceControlClient.DisableAutorunAsync
                : _frameworkServiceControlClient.EnableAutorunAsync).ConfigureAwait(false);
    }

    private async Task ApplyConfigurationAsync()
    {
        if (!TryBuildDraftConfiguration(out var request, out var validationError))
        {
            await _dispatcherQueue.EnqueueAsync(() =>
                ApplyActionResult("Apply service configuration", validationError, InfoBarSeverity.Error));
            return;
        }

        IsConfigurationOperationInProgress = true;

        try
        {
            var result = await _frameworkServiceConfigurationClient.UpdateConfigurationAsync(request, CancellationToken.None).ConfigureAwait(false);
            await _dispatcherQueue.EnqueueAsync(() =>
            {
                CurrentConfigurationSnapshot = result.Configuration;
                IsConfigurationLoaded = true;
                ConfigurationSourcePath = result.Configuration.PersistentConfigurationPath;

                if (result.Succeeded)
                {
                    ApplyConfigurationDraft(result.Configuration);
                }

                ApplyActionResult("Apply service configuration", result.Message, result.Succeeded ? InfoBarSeverity.Success : InfoBarSeverity.Error);
            });
        }
        finally
        {
            await _dispatcherQueue.EnqueueAsync(() => IsConfigurationOperationInProgress = false);
        }
    }

    private void ResetConfiguration()
    {
        if (CurrentConfigurationSnapshot is null)
        {
            return;
        }

        ApplyConfigurationDraft(CurrentConfigurationSnapshot);
    }

    private Task UpdateStatusAsync(FrameworkSystemStatus status)
    {
        return _dispatcherQueue.EnqueueAsync(() =>
        {
            ObservedServiceStatus = status;
            ApplyStatus(status);
        });
    }

    private Task UpdateConfigurationSnapshotAsync(FrameworkServiceConfigurationSnapshot snapshot)
    {
        return _dispatcherQueue.EnqueueAsync(() =>
        {
            var shouldRefreshDraft = !IsConfigurationLoaded || !HasConfigurationChanges || IsConfigurationOperationInProgress;

            CurrentConfigurationSnapshot = snapshot;
            IsConfigurationLoaded = true;
            ConfigurationSourcePath = snapshot.PersistentConfigurationPath;

            if (shouldRefreshDraft)
            {
                ApplyConfigurationDraft(snapshot);
            }
        });
    }

    private void ApplyStatus(FrameworkSystemStatus status)
    {
        LastStatusObservedAt = status.ObservedAt.LocalDateTime.ToString("T", CultureInfo.CurrentCulture);

        if (!status.IsGrpcActive)
        {
            ServiceStateSeverity = InfoBarSeverity.Error;
            ServiceStateTitle = IsServiceInstalled ? "Service offline" : "Service not installed";
            ServiceStateMessage = IsServiceInstalled
                ? status.LastError ?? "The client cannot reach SubZeroFramework.Service over local gRPC IPC."
                : "SubZeroFramework.Service is not currently installed.";
            return;
        }

        if (!status.IsLibraryAvailable)
        {
            ServiceStateSeverity = InfoBarSeverity.Error;
            ServiceStateTitle = "Service running with library issue";
            ServiceStateMessage = status.LastError ?? "The service is running, but FrameworkDotnet could not be loaded.";
            return;
        }

        if (status.RequiresElevation)
        {
            ServiceStateSeverity = InfoBarSeverity.Warning;
            ServiceStateTitle = "Service requires elevation";
            ServiceStateMessage = "The service is running without the privileges required for Framework EC access.";
            return;
        }

        if (!string.IsNullOrEmpty(status.LastError))
        {
            ServiceStateSeverity = InfoBarSeverity.Warning;
            ServiceStateTitle = "Service warning";
            ServiceStateMessage = status.LastError;
            return;
        }

        ServiceStateSeverity = InfoBarSeverity.Success;
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
        CanUninstallService = serviceInfo.CanUninstall;
        PackagedHelperAvailable = serviceInfo.PackagedHelperAvailable;
        IsElevatedSession = serviceInfo.IsElevatedSession;
        IsAutorunEnabled = serviceInfo.IsAutorunEnabled;
    }

    private void ApplyActionResult(string title, string message, InfoBarSeverity severity)
    {
        LastActionTitle = title;
        LastActionMessage = message;
        LastActionSeverity = severity;
        IsLastActionVisible = true;
    }

    private void ApplyConfigurationDraft(FrameworkServiceConfigurationSnapshot snapshot)
    {
        TelemetryPollingIntervalMillisecondsText = FormatMilliseconds(snapshot.PollingInterval);
        HardwareInfoPollingIntervalMillisecondsText = FormatMilliseconds(snapshot.HardwareInfoPollingInterval);
        AllowFanControlCommandsDraft = snapshot.AllowFanControlCommands;
    }

    private bool TryBuildDraftConfiguration(out FrameworkServiceConfigurationUpdateRequest request, out string validationError)
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

        if (pollingIntervalMilliseconds <= 0)
        {
            validationError = "Telemetry polling interval must be greater than zero milliseconds.";
            return false;
        }

        if (hardwareInfoPollingIntervalMilliseconds <= 0)
        {
            validationError = "Hardware info polling interval must be greater than zero milliseconds.";
            return false;
        }

        request = new FrameworkServiceConfigurationUpdateRequest
        {
            PollingInterval = TimeSpan.FromMilliseconds(pollingIntervalMilliseconds),
            HardwareInfoPollingInterval = TimeSpan.FromMilliseconds(hardwareInfoPollingIntervalMilliseconds),
            AllowFanControlCommands = AllowFanControlCommandsDraft,
        };

        validationError = string.Empty;
        return true;
    }

    private static InfoBarSeverity MapSeverity(FrameworkServiceCommandResultKind kind)
        => kind switch
        {
            FrameworkServiceCommandResultKind.Success => InfoBarSeverity.Success,
            FrameworkServiceCommandResultKind.Warning => InfoBarSeverity.Warning,
            _ => InfoBarSeverity.Error,
        };

    private static string FormatMilliseconds(TimeSpan timeSpan)
        => checked((long)Math.Round(timeSpan.TotalMilliseconds, MidpointRounding.AwayFromZero)).ToString(CultureInfo.InvariantCulture);

    private static string FormatSamplesPerSecondDisplay(string? millisecondsText)
    {
        if (!long.TryParse(millisecondsText?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var milliseconds) || milliseconds <= 0)
        {
            return "Unavailable";
        }

        var samplesPerSecond = 1000d / milliseconds;
        return samplesPerSecond >= 1d
            ? $"{samplesPerSecond:0.##} samples/sec"
            : $"1 sample every {milliseconds:N0} ms";
    }
}
