using System.Collections.ObjectModel;
using System.Globalization;
using System.Reactive.Disposables;
using System.Reactive.Disposables.Fluent;
using System.Reactive.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.WinUI;

using DynamicData;

using Material.Icons;

using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

using SubZeroFramework.Controls.Settings.Models;
using SubZeroFramework.Services;
using SubZeroFramework.Services.Units;
using SubZeroFramework.Themes;

using Windows.UI;

namespace SubZeroFramework.Presentation.MenuItems.Settings;

/// <summary>
/// Page model for the redesigned Settings page: a sub-navigation card (Service / Display units /
/// Startup &amp; alerts / Licenses / About) with a "Service reachable" footer, and per-section bodies
/// resolved by nested-region navigation (the Device Capabilities pattern). Publishes itself through
/// <see cref="SettingsAccessor"/> so the section VMs bind to the displayed instance.
/// </summary>
public partial class SettingsModel : ObservableObject, IDisposable
{
    private const string SubZeroRepositoryUrl = "https://github.com/TekuSP/SubZeroFramework";
    private const string FrameworkDotnetRepositoryUrl = "https://github.com/TekuSP/framework-dotnet";
    private const string FfiExtensionsRepositoryUrl = "https://github.com/TekuSP/framework-system-ffi-extensions";
    private const string FrameworkSystemRepositoryUrl = "https://github.com/FrameworkComputer/framework-system";

    // Representative fallbacks shown until (or when no) live telemetry backs a sample row.
    private const double FallbackTemperatureCelsius = 65d;
    private const double FallbackFanRpm = 3200d;
    private const double FallbackClockMegahertz = 3600d;
    private const double FallbackRefreshHertz = 60d;
    private const ulong FallbackInformationBytes = 34_359_738_368; // 32 GiB
    private const double FallbackVoltageVolts = 15.4d;
    private const double FallbackCurrentAmperes = 1.2d;
    private const double FallbackChargeAmpereHours = 3.5d;
    private const double FallbackRatioPercent = 76d;
    private const double FallbackBitRateBitsPerSecond = 1_000_000_000d;
    private const double FallbackPowerWatts = 18d;
    // Length/airflow have no live telemetry source; these mirror typical FW16 fan specs.
    private const double SampleLengthMillimeters = 75d;
    private const double SampleAirflowCfm = 42d;

    private readonly CompositeDisposable _subscriptions = [];
    private readonly IFrameworkServiceControlClient _frameworkServiceControlClient;
    private readonly IFrameworkServiceConfigurationClient _frameworkServiceConfigurationClient;
    private readonly IUserUnitPreferencesClient _userUnitPreferencesClient;
    private readonly IUnitFormattingService _unitFormattingService;
    private readonly ILocalClientSettingsStore _clientSettings;
    private readonly IStartupRegistrationService _startupRegistration;
    private readonly DispatcherQueue _dispatcherQueue;

    private bool _suppressStartupCallbacks;
    private double? _sampleTemperatureCelsius;
    private double? _sampleFanRpm;
    private double? _sampleClockMegahertz;
    private double? _sampleRefreshHertz;
    private ulong? _sampleInformationBytes;
    private double? _sampleVoltageVolts;
    private double? _sampleCurrentAmperes;
    private double? _sampleChargeAmpereHours;
    private double? _sampleRatioPercent;
    private double? _sampleBitRateBitsPerSecond;
    private double? _samplePowerWatts;

    public SettingsModel(
        IFrameworkStatusClient frameworkStatusClient,
        IFrameworkServiceControlClient frameworkServiceControlClient,
        IFrameworkServiceConfigurationClient frameworkServiceConfigurationClient,
        UnitPreferenceCatalog unitPreferenceCatalog,
        IUserUnitPreferencesClient userUnitPreferencesClient,
        IUnitFormattingService unitFormattingService,
        ILocalClientSettingsStore clientSettings,
        IStartupRegistrationService startupRegistration,
        ITemperatureTelemetryClient temperatureTelemetryClient,
        IFanTelemetryClient fanTelemetryClient,
        IBatteryTelemetryClient batteryTelemetryClient,
        IHardwareInfoClient hardwareInfoClient,
        SettingsAccessor accessor,
        DispatcherQueue dispatcherQueue)
    {
        ArgumentNullException.ThrowIfNull(frameworkStatusClient);
        ArgumentNullException.ThrowIfNull(frameworkServiceControlClient);
        ArgumentNullException.ThrowIfNull(frameworkServiceConfigurationClient);
        ArgumentNullException.ThrowIfNull(unitPreferenceCatalog);
        ArgumentNullException.ThrowIfNull(userUnitPreferencesClient);
        ArgumentNullException.ThrowIfNull(unitFormattingService);
        ArgumentNullException.ThrowIfNull(clientSettings);
        ArgumentNullException.ThrowIfNull(startupRegistration);
        ArgumentNullException.ThrowIfNull(temperatureTelemetryClient);
        ArgumentNullException.ThrowIfNull(fanTelemetryClient);
        ArgumentNullException.ThrowIfNull(batteryTelemetryClient);
        ArgumentNullException.ThrowIfNull(hardwareInfoClient);
        ArgumentNullException.ThrowIfNull(accessor);
        ArgumentNullException.ThrowIfNull(dispatcherQueue);

        _frameworkServiceControlClient = frameworkServiceControlClient;
        _frameworkServiceConfigurationClient = frameworkServiceConfigurationClient;
        _userUnitPreferencesClient = userUnitPreferencesClient;
        _unitFormattingService = unitFormattingService;
        _clientSettings = clientSettings;
        _startupRegistration = startupRegistration;
        _dispatcherQueue = dispatcherQueue;

        // Publish before any nested-region navigation constructs a section VM.
        accessor.Current = this;

        Sections =
        [
            new SettingsSectionRailItemModel(0, "Service", "Lifecycle & recovery", MaterialIconKind.Wrench),
            new SettingsSectionRailItemModel(1, "Display units", "Temperature, power, speed", MaterialIconKind.Ruler),
            new SettingsSectionRailItemModel(2, "Startup & alerts", "Launch behavior", MaterialIconKind.RocketLaunchOutline),
            new SettingsSectionRailItemModel(3, "Licenses", "Open-source notices", MaterialIconKind.ScaleBalance),
            new SettingsSectionRailItemModel(4, "About", "Version & links", MaterialIconKind.InformationOutline),
        ];
        Sections[0].IsSelected = true;

        SelectSectionCommand = new RelayCommand<SettingsSectionRailItemModel>(SelectSection);

        LastStatusObservedAt = frameworkStatusClient.LastObservedAt is DateTimeOffset observedAt
            ? observedAt.LocalDateTime.ToString("T", CultureInfo.CurrentCulture)
            : "waiting for status";

        ShutdownServiceCommand = new AsyncRelayCommand(() => ExecuteServiceActionAsync(_frameworkServiceControlClient.ShutdownAsync), CanRunInstalledServiceAction);
        RestartServiceCommand = new AsyncRelayCommand(() => ExecuteServiceActionAsync(_frameworkServiceControlClient.RestartAsync), CanRunInstalledServiceAction);
        InstallServiceCommand = new AsyncRelayCommand(() => ExecuteServiceActionAsync(_frameworkServiceControlClient.InstallAsync), CanRunInstallAction);
        UpdateServiceCommand = new AsyncRelayCommand(() => ExecuteServiceActionAsync(_frameworkServiceControlClient.UpdateAsync), CanRunUpdateAction);
        UninstallServiceCommand = new AsyncRelayCommand(() => ExecuteServiceActionAsync(_frameworkServiceControlClient.UninstallAsync), CanRunUninstallAction);
        RecheckServiceCommand = new RelayCommand(RecheckService);
        ApplyConfigurationCommand = new AsyncRelayCommand(ApplyConfigurationAsync, CanRunApplyConfigurationAction);
        SaveConfigurationCommand = new AsyncRelayCommand(SaveConfigurationAsync, CanRunSaveConfigurationAction);
        ResetConfigurationCommand = new RelayCommand(ResetConfiguration, CanRunResetConfigurationAction);
        ResetUnitsCommand = new AsyncRelayCommand(ResetUnitsAsync);

        UnitRows =
        [
            .. unitPreferenceCatalog.Definitions.Select(definition => new UnitPreferenceRowModel(definition, HandleUnitRowSelectionChanged))
        ];
        ApplyUnitPreferenceSnapshot(unitPreferenceCatalog.Normalize(_userUnitPreferencesClient.CurrentPreferences));

        InitializeStartupToggles();
        AboutRows = BuildAboutRows();
        _ = LoadLicensesAsync();

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

        userUnitPreferencesClient
            .WatchPreferences()
            .Select(snapshot => Observable.FromAsync(_ => _dispatcherQueue.EnqueueAsync(() => ApplyUnitPreferenceSnapshot(snapshot))))
            .Concat()
            .Subscribe()
            .DisposeWith(_subscriptions);

        // Live sample feeds for the Display-units rows. Each stream reduces to one representative number;
        // sampling caps UI churn to one refresh per two seconds.
        temperatureTelemetryClient
            .WatchTemperatures()
            .QueryWhenChanged(query => query.Items.Max(sensor => sensor.TemperatureCelsius))
            .Sample(TimeSpan.FromSeconds(2))
            .Subscribe(celsius => UpdateSample(() => _sampleTemperatureCelsius = celsius))
            .DisposeWith(_subscriptions);

        fanTelemetryClient
            .WatchFans()
            .QueryWhenChanged(query => query.Items.Where(fan => fan.IsAvailable).Select(fan => (double?)fan.SpeedRpm).Max())
            .Sample(TimeSpan.FromSeconds(2))
            .Subscribe(rpm => UpdateSample(() => _sampleFanRpm = rpm))
            .DisposeWith(_subscriptions);

        batteryTelemetryClient
            .WatchBatteries()
            .QueryWhenChanged(query => query.Items.FirstOrDefault(battery => battery.IsAvailable))
            .Sample(TimeSpan.FromSeconds(2))
            .Subscribe(battery => UpdateSample(() =>
            {
                _sampleVoltageVolts = battery?.Voltage;
                _sampleCurrentAmperes = battery?.Amperage is double amperage ? Math.Abs(amperage) : null;
                _sampleChargeAmpereHours = battery?.RemainingCapacityAmpereHours;
                _sampleRatioPercent = battery?.ChargePercent;
                _samplePowerWatts = battery?.Voltage is double volts && battery?.Amperage is double amps
                    ? Math.Abs(volts * amps)
                    : null;
            }))
            .DisposeWith(_subscriptions);

        hardwareInfoClient
            .WatchHardwareInfo()
            .Subscribe(snapshot => UpdateSample(() =>
            {
                _sampleClockMegahertz = snapshot.Cpus
                    .Select(cpu => cpu.CurrentClockSpeedMHz > 0 ? (double?)cpu.CurrentClockSpeedMHz : cpu.MaxClockSpeedMHz)
                    .FirstOrDefault(clock => clock > 0);
                _sampleRefreshHertz = snapshot.Monitors
                    .Select(monitor => (double?)monitor.CurrentRefreshRate)
                    .FirstOrDefault(rate => rate > 0);
                var totalMemoryBytes = snapshot.MemoryModules.Aggregate(0UL, (total, module) => total + module.CapacityBytes);
                _sampleInformationBytes = totalMemoryBytes > 0 ? totalMemoryBytes : null;
                _sampleBitRateBitsPerSecond = snapshot.NetworkAdapters
                    .Select(adapter => (double?)adapter.Speed)
                    .Where(speed => speed > 0)
                    .Max();
            }))
            .DisposeWith(_subscriptions);
    }

    // ----- Sub-navigation -----

    public IReadOnlyList<SettingsSectionRailItemModel> Sections { get; }

    [ObservableProperty]
    public partial int SelectedSectionIndex { get; set; }

    public IRelayCommand<SettingsSectionRailItemModel> SelectSectionCommand { get; }

    private void SelectSection(SettingsSectionRailItemModel? section)
    {
        if (section is null)
        {
            return;
        }

        foreach (var item in Sections)
        {
            item.IsSelected = item.Index == section.Index;
        }

        SelectedSectionIndex = section.Index;
    }

    // ----- Service state (banner + footer) -----

    [ObservableProperty]
    public partial string LastStatusObservedAt { get; set; }

    [ObservableProperty]
    public partial string BannerTitle { get; set; } = "Checking service health";

    [ObservableProperty]
    public partial string BannerDetail { get; set; } = "Waiting for status stream updates from SubZeroFramework.Service.";

    // Brushes are derived (never stored): creating a SolidColorBrush is only legal on the UI thread, and the
    // VM may be constructed off it — computed getters evaluate at binding time instead.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BannerBackground))]
    [NotifyPropertyChangedFor(nameof(BannerBorderBrush))]
    [NotifyPropertyChangedFor(nameof(BannerForeground))]
    [NotifyPropertyChangedFor(nameof(BannerIconKind))]
    [NotifyPropertyChangedFor(nameof(FooterText))]
    [NotifyPropertyChangedFor(nameof(FooterBrush))]
    [NotifyPropertyChangedFor(nameof(FooterIconKind))]
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

    public string FooterText => ServiceStateSeverity switch
    {
        InfoBarSeverity.Success => "Service reachable",
        InfoBarSeverity.Warning => "Service degraded",
        InfoBarSeverity.Error => "Service unreachable",
        _ => "Checking service",
    };

    public Brush FooterBrush => SeverityForegroundBrush(ServiceStateSeverity);

    public MaterialIconKind FooterIconKind => BannerIconKind;

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

        // Live values surfaced on the About page ride the same status stream.
        if (!string.IsNullOrWhiteSpace(status.EcBuildInfo))
        {
            AboutRows[1].Value = status.EcBuildInfo!;
        }

        if (!string.IsNullOrWhiteSpace(status.ConnectionLibraryVersion) && status.ConnectionLibraryVersion != "Unknown")
        {
            AboutRows[2].Value = status.ConnectionLibraryVersion;
        }
    }

    private void SetServiceState(InfoBarSeverity severity, string title, string detail)
    {
        BannerTitle = title;
        BannerDetail = severity == InfoBarSeverity.Success ? detail : $"{detail} — last check {LastStatusObservedAt}";
        ServiceStateSeverity = severity;
    }

    private static Brush TintBrush(Color color, byte alpha)
        => new SolidColorBrush(Color.FromArgb(alpha, color.R, color.G, color.B));

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

    [ObservableProperty]
    public partial bool? IsAutorunEnabled { get; set; }

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
            var result = await action(CancellationToken.None).ConfigureAwait(false);
            await _dispatcherQueue.EnqueueAsync(() =>
            {
                ApplyServiceControlInfo(_frameworkServiceControlClient.GetInfo());
                LastActionTitle = result.OperationName;
                LastActionMessage = result.Message;
                LastActionSeverity = MapSeverity(result.Kind);
                IsLastActionVisible = true;
            });
        }
        finally
        {
            await _dispatcherQueue.EnqueueAsync(() => IsOperationInProgress = false);
        }
    }

    private void RecheckService()
    {
        ApplyServiceControlInfo(_frameworkServiceControlClient.GetInfo());
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
        IsAutorunEnabled = serviceInfo.IsAutorunEnabled;
        SyncAutorunToggle();
    }

    private Task UpdateStatusAsync(FrameworkSystemStatus status)
        => _dispatcherQueue.EnqueueAsync(() => ApplyStatus(status));

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
            await _dispatcherQueue.EnqueueAsync(() =>
                ApplyConfigurationActionResult("Apply service configuration", validationError, InfoBarSeverity.Error));
            return;
        }

        IsConfigurationOperationInProgress = true;

        try
        {
            var result = await _frameworkServiceConfigurationClient.ApplyConfigurationAsync(request, CancellationToken.None).ConfigureAwait(false);
            await _dispatcherQueue.EnqueueAsync(() =>
            {
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
            });
        }
        catch (Exception exception)
        {
            await _dispatcherQueue.EnqueueAsync(() =>
                ApplyConfigurationActionResult("Apply service configuration", exception.Message, InfoBarSeverity.Error));
        }
        finally
        {
            await _dispatcherQueue.EnqueueAsync(() => IsConfigurationOperationInProgress = false);
        }
    }

    private async Task SaveConfigurationAsync()
    {
        IsConfigurationOperationInProgress = true;

        try
        {
            var result = await _frameworkServiceConfigurationClient.SaveConfigurationAsync(CancellationToken.None).ConfigureAwait(false);
            await _dispatcherQueue.EnqueueAsync(() =>
            {
                if (result.Succeeded)
                {
                    HasUnsavedConfigurationChanges = false;
                }

                ApplyConfigurationActionResult("Save service configuration", result.Message, result.Succeeded ? InfoBarSeverity.Success : InfoBarSeverity.Error);
            });
        }
        catch (Exception exception)
        {
            await _dispatcherQueue.EnqueueAsync(() =>
                ApplyConfigurationActionResult("Save service configuration", exception.Message, InfoBarSeverity.Error));
        }
        finally
        {
            await _dispatcherQueue.EnqueueAsync(() => IsConfigurationOperationInProgress = false);
        }
    }

    private void ResetConfiguration()
    {
        if (CurrentConfigurationSnapshot is not null)
        {
            ApplyConfigurationDraft(CurrentConfigurationSnapshot);
        }
    }

    private Task UpdateConfigurationSnapshotAsync(FrameworkServiceConfigurationSnapshot snapshot)
    {
        return _dispatcherQueue.EnqueueAsync(() =>
        {
            var shouldRefreshDraft = !IsConfigurationLoaded || !HasConfigurationChanges || IsConfigurationOperationInProgress;

            CurrentConfigurationSnapshot = snapshot;
            IsConfigurationLoaded = true;

            if (shouldRefreshDraft)
            {
                ApplyConfigurationDraft(snapshot);
            }
        });
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

    // ----- Display units (client-owned) -----

    public IReadOnlyList<UnitPreferenceRowModel> UnitRows { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UnitsStatusVisibility))]
    public partial string UnitsStatusMessage { get; set; } = string.Empty;

    public Visibility UnitsStatusVisibility => string.IsNullOrEmpty(UnitsStatusMessage) ? Visibility.Collapsed : Visibility.Visible;

    public IAsyncRelayCommand ResetUnitsCommand { get; }

    private void HandleUnitRowSelectionChanged(UnitPreferenceRowModel row)
    {
        var snapshot = new UserUnitPreferencesSnapshot
        {
            SchemaVersion = UserUnitPreferencesSnapshot.CurrentSchemaVersion,
            Entries = [.. UnitRows.Select(unitRow => new UserUnitPreferenceEntry(unitRow.Kind, unitRow.SelectedKey))],
        };

        _ = PersistUnitPreferencesAsync(_userUnitPreferencesClient.ApplyPreferencesAsync(snapshot, CancellationToken.None));
    }

    private Task ResetUnitsAsync()
        => PersistUnitPreferencesAsync(_userUnitPreferencesClient.ResetToDefaultsAsync(CancellationToken.None));

    private async Task PersistUnitPreferencesAsync(Task<UserPreferencesOperationResult> operation)
    {
        var result = await operation.ConfigureAwait(false);
        await _dispatcherQueue.EnqueueAsync(() => UnitsStatusMessage = result.Succeeded ? string.Empty : result.Message);
    }

    private void ApplyUnitPreferenceSnapshot(UserUnitPreferencesSnapshot snapshot)
    {
        foreach (var row in UnitRows)
        {
            row.ApplySelection(snapshot.GetOptionKey(row.Kind, row.SelectedKey));
        }

        UpdateSampleTexts();
    }

    private void UpdateSample(Action applyLatestValues)
    {
        _dispatcherQueue.TryEnqueue(() =>
        {
            applyLatestValues();
            UpdateSampleTexts();
        });
    }

    private void UpdateSampleTexts()
    {
        foreach (var row in UnitRows)
        {
            row.SampleText = row.Kind switch
            {
                UnitQuantityKind.Temperature => _unitFormattingService.FormatTemperature(_sampleTemperatureCelsius ?? FallbackTemperatureCelsius, decimals: 1),
                UnitQuantityKind.FanSpeed => _unitFormattingService.FormatFanSpeed(_sampleFanRpm ?? FallbackFanRpm),
                UnitQuantityKind.ClockFrequency => _unitFormattingService.FormatClockFrequencyMegahertz(_sampleClockMegahertz ?? FallbackClockMegahertz),
                UnitQuantityKind.RefreshRate => _unitFormattingService.FormatRefreshRateHertz(_sampleRefreshHertz ?? FallbackRefreshHertz),
                UnitQuantityKind.InformationSize => _unitFormattingService.FormatInformationBytes(_sampleInformationBytes ?? FallbackInformationBytes),
                UnitQuantityKind.Voltage => _unitFormattingService.FormatVoltage(_sampleVoltageVolts ?? FallbackVoltageVolts),
                UnitQuantityKind.Current => _unitFormattingService.FormatCurrent(_sampleCurrentAmperes ?? FallbackCurrentAmperes),
                UnitQuantityKind.ElectricChargeCapacity => _unitFormattingService.FormatChargeCapacity(_sampleChargeAmpereHours ?? FallbackChargeAmpereHours),
                UnitQuantityKind.Ratio => _unitFormattingService.FormatRatio(_sampleRatioPercent ?? FallbackRatioPercent),
                UnitQuantityKind.Length => _unitFormattingService.FormatLengthMillimeters(SampleLengthMillimeters),
                UnitQuantityKind.Airflow => _unitFormattingService.FormatAirflowCfm(SampleAirflowCfm),
                UnitQuantityKind.BitRate => _unitFormattingService.FormatBitRateBitsPerSecond(_sampleBitRateBitsPerSecond ?? FallbackBitRateBitsPerSecond),
                UnitQuantityKind.Power => _unitFormattingService.FormatPowerWatts(_samplePowerWatts ?? FallbackPowerWatts),
                _ => row.SampleText,
            };
        }
    }

    // ----- Startup & alerts -----

    [ObservableProperty]
    public partial bool StartWithSystemBoot { get; set; }

    [ObservableProperty]
    public partial bool StartWithSystemBootSupported { get; set; }

    [ObservableProperty]
    public partial bool ThermalAlertsEnabled { get; set; }

    [ObservableProperty]
    public partial bool AutorunIsOn { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanToggleAutorun))]
    public partial bool AutorunToggleAvailable { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StartupStatusVisibility))]
    public partial string StartupStatusMessage { get; set; } = string.Empty;

    public Visibility StartupStatusVisibility => string.IsNullOrEmpty(StartupStatusMessage) ? Visibility.Collapsed : Visibility.Visible;

    public bool CanToggleAutorun => AutorunToggleAvailable;

    private void InitializeStartupToggles()
    {
        _suppressStartupCallbacks = true;
        StartWithSystemBootSupported = _startupRegistration.IsSupported;
        StartWithSystemBoot = _startupRegistration.IsEnabled();
        // Thermal alerts are disabled for the MVP (toast delivery is unreliable — see ThermalAlertMonitor's
        // remarks); the toggle reads off regardless of any previously persisted opt-in.
        ThermalAlertsEnabled = false;
        _suppressStartupCallbacks = false;
    }

    partial void OnStartWithSystemBootChanged(bool value)
    {
        if (_suppressStartupCallbacks)
        {
            return;
        }

        if (!_startupRegistration.TrySetEnabled(value))
        {
            StartupStatusMessage = "Updating the launch-at-sign-in registration failed. The setting was not changed.";
            _suppressStartupCallbacks = true;
            StartWithSystemBoot = _startupRegistration.IsEnabled();
            _suppressStartupCallbacks = false;
            return;
        }

        StartupStatusMessage = string.Empty;
    }

    partial void OnThermalAlertsEnabledChanged(bool value)
    {
        if (!_suppressStartupCallbacks)
        {
            _clientSettings.ThermalAlertsEnabled = value;
        }
    }

    partial void OnAutorunIsOnChanged(bool value)
    {
        if (_suppressStartupCallbacks || IsAutorunEnabled == value)
        {
            return;
        }

        _ = ExecuteServiceActionAsync(value
            ? _frameworkServiceControlClient.EnableAutorunAsync
            : _frameworkServiceControlClient.DisableAutorunAsync);
    }

    private void SyncAutorunToggle()
    {
        AutorunToggleAvailable = IsServiceControlSupported && IsServiceInstalled && IsAutorunEnabled.HasValue && !IsOperationInProgress;

        if (IsAutorunEnabled is bool autorunEnabled && AutorunIsOn != autorunEnabled)
        {
            _suppressStartupCallbacks = true;
            AutorunIsOn = autorunEnabled;
            _suppressStartupCallbacks = false;
        }
    }

    // ----- Licenses -----

    public ObservableCollection<LicenseEntryModel> Licenses { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LicensesMessageVisibility))]
    public partial string LicensesMessage { get; set; } = "Loading license report…";

    public Visibility LicensesMessageVisibility => string.IsNullOrEmpty(LicensesMessage) ? Visibility.Collapsed : Visibility.Visible;

    private async Task LoadLicensesAsync()
    {
        try
        {
            var reportPath = Path.Combine(AppContext.BaseDirectory, "Assets", "ThirdPartyLicenses", "third-party-licenses.json");
            if (!File.Exists(reportPath))
            {
                await _dispatcherQueue.EnqueueAsync(() =>
                    LicensesMessage = "The third-party license report was not generated during this build.");
                return;
            }

            var json = await File.ReadAllTextAsync(reportPath).ConfigureAwait(false);
            var entries = JsonSerializer.Deserialize(json, SettingsLicensesJsonContext.Default.LicenseReportEntryArray) ?? [];

            await _dispatcherQueue.EnqueueAsync(() =>
            {
                Licenses.Clear();
                foreach (var entry in entries.OrderBy(item => item.PackageId, StringComparer.OrdinalIgnoreCase))
                {
                    Licenses.Add(new LicenseEntryModel(entry.PackageId, entry.Version, entry.License, entry.Text));
                }

                LicensesMessage = Licenses.Count == 0 ? "The license report is empty." : string.Empty;
            });
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            await _dispatcherQueue.EnqueueAsync(() =>
                LicensesMessage = $"The third-party license report could not be read: {exception.Message}");
        }
    }

    // ----- About -----

    public IReadOnlyList<AboutRowModel> AboutRows { get; }

    private static IReadOnlyList<AboutRowModel> BuildAboutRows()
    {
        return
        [
            new AboutRowModel("SubZero", ResolveAppVersion(), SubZeroRepositoryUrl),
            new AboutRowModel("EC Build", "Waiting for service", null),
            new AboutRowModel("framework-dotnet", ResolveFrameworkDotnetVersion(), FrameworkDotnetRepositoryUrl),
            new AboutRowModel("framework-system-ffi-extensions", ResolveAssemblyMetadata("FrameworkSystemFfiExtensionsVersion"), FfiExtensionsRepositoryUrl),
            new AboutRowModel("framework-system", ResolveAssemblyMetadata("FrameworkSystemVersion"), FrameworkSystemRepositoryUrl),
        ];
    }

    private static string ResolveAppVersion()
    {
        var assembly = typeof(SettingsModel).Assembly;
        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

        if (!string.IsNullOrWhiteSpace(informational))
        {
            // Strip the "+<commit-hash>" build-metadata suffix SourceLink appends.
            var plusIndex = informational.IndexOf('+', StringComparison.Ordinal);
            return plusIndex > 0 ? informational[..plusIndex] : informational;
        }

        return assembly.GetName().Version?.ToString() ?? "Unknown";
    }

    private static string ResolveFrameworkDotnetVersion()
        => typeof(FrameworkDotnet.FrameworkSystem).Assembly.GetName().Version?.ToString() ?? "Unknown";

    private static string ResolveAssemblyMetadata(string key)
    {
        // framework-dotnet does not embed its native component versions yet (recorded as a library
        // follow-up); show an honest placeholder instead of a stale hardcoded number.
        var metadata = typeof(FrameworkDotnet.FrameworkSystem).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(attribute => string.Equals(attribute.Key, key, StringComparison.Ordinal));

        return string.IsNullOrWhiteSpace(metadata?.Value) ? "Bundled with framework-dotnet" : metadata!.Value!;
    }

    public void Dispose()
    {
        _subscriptions.Dispose();
    }

    internal sealed record LicenseReportEntry(string PackageId, string Version, string License, string Text);
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(SettingsModel.LicenseReportEntry[]))]
internal sealed partial class SettingsLicensesJsonContext : JsonSerializerContext;
