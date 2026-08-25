using System.Diagnostics.CodeAnalysis;

using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;

using LiveChartsCore.SkiaSharpView;

using Microsoft.Extensions.Options;

using SubZeroFramework.Controls.DeviceCapabilities.Models;
using SubZeroFramework.Converters;
using SubZeroFramework.Controls.DeviceCapabilities.Models.Categories;
using SubZeroFramework.Controls.FanCurveProfiles.Models.Modes;
using SubZeroFramework.Presentation.MenuItems.Dashboard;
using SubZeroFramework.Presentation.MenuItems.DeviceCapabilities;
using SubZeroFramework.Presentation.MenuItems.DeviceCapabilities.Categories;
using SubZeroFramework.Presentation.MenuItems.FanCurveProfiles;
using SubZeroFramework.Presentation.MenuItems.FanCurveProfiles.Modes;
using SubZeroFramework.Presentation.MenuItems.Modules;
using SubZeroFramework.Presentation.MenuItems.Modules.Layouts;
using SubZeroFramework.Presentation.MenuItems.PowerTelemetry;
using SubZeroFramework.Presentation.MenuItems.Settings;
using SubZeroFramework.Presentation.MenuItems.Settings.Sections;
using SubZeroFramework.Presentation.MenuItems.ThermalTelemetry;
using SubZeroFramework.Presentation.MenuItems.WarningsIssues;
using SubZeroFramework.Services;
using SubZeroFramework.Services.Units;
using SubZeroFramework.Themes;

namespace SubZeroFramework;

public partial class App : Application
{

    /// <summary>
    /// Initializes the singleton application object. This is the first line of authored code
    /// executed, and as such is the logical equivalent of main() or WinMain().
    /// </summary>
    public App()
    {
        this.InitializeComponent();
    }

    public Window? MainWindow { get; protected set; }
    protected IHost? Host { get; private set; }
    protected ILogger? Logger { get; private set; }


    [SuppressMessage("Trimming", "IL2026:Members annotated with 'RequiresUnreferencedCodeAttribute' require dynamic access otherwise can break functionality when trimming application code", Justification = "Uno.Extensions APIs are used in a way that is safe for trimming in this template context.")]
    protected async override void OnLaunched(LaunchActivatedEventArgs args)
    {
        LiveChartsCore.LiveCharts.Configure(config =>
            config
                .AddSkiaSharp()
                .AddDefaultMappers()
                .AddDarkTheme()
                .AddMyCustomTheme());

        // The app's own log records had nowhere to go on Windows: this is a GUI-subsystem binary, so the
        // console sink writes to a console that does not exist and the debug sink only exists under a
        // debugger. A released build could warn about a broken service connection every second and the user
        // would never see one line of it. The same bounded buffer the service uses captures them instead,
        // and Settings > Logs shows them alongside the service's. Created here rather than resolved from DI
        // because the logging pipeline is configured before the container exists.
        var appLogBuffer = new InMemoryLogBuffer();

        var builder = this.CreateBuilder(args)
            // Add navigation support for toolkit controls such as TabBar and NavigationView
            .UseToolkitNavigation()
            .Configure(host => host
#if DEBUG
                // Switch to Development environment when running in DEBUG
                .UseEnvironment(Environments.Development)
#endif
                .UseLogging(configure: (context, logBuilder) =>
                {
                    // Verbosity by build configuration. DEBUG turns the app's own categories all the way down
                    // to Trace; RELEASE settles at Information, which is enough to reconstruct what the app
                    // did without a record per telemetry tick.
#if DEBUG
                    logBuilder
                        .SetMinimumLevel(LogLevel.Trace)
                        .CoreLogLevel(LogLevel.Warning);

                    logBuilder.AddFilter("SubZeroFramework", LogLevel.Trace);

                    // The Uno diagnostic groups below are genuinely noisy and only make sense while
                    // debugging the UI itself. BinderMemoryReference in particular tracks every binder
                    // reference, so it is not something to leave on in a shipped build.
                    logBuilder.XamlLogLevel(LogLevel.Debug);
                    logBuilder.XamlLayoutLogLevel(LogLevel.Debug);
                    logBuilder.XamlBindingLogLevel(LogLevel.Debug);
                    logBuilder.BinderMemoryReferenceLogLevel(LogLevel.Debug);
                    logBuilder.HotReloadCoreLogLevel(LogLevel.Information);
#else
                    // Previously this was Warning, and — more importantly — the Xaml/Layout/Binding/Binder
                    // groups above were configured at Debug in EVERY configuration. A category filter beats
                    // the minimum level rather than being capped by it, so a release build really was
                    // emitting Uno layout and binding diagnostics on the UI thread. They are DEBUG-only now.
                    logBuilder
                        .SetMinimumLevel(LogLevel.Information)
                        .CoreLogLevel(LogLevel.Warning);

                    logBuilder.AddFilter("SubZeroFramework", LogLevel.Information);
                    logBuilder.AddFilter("Microsoft", LogLevel.Warning);
                    logBuilder.AddFilter("Uno", LogLevel.Warning);
#endif

                    // Retains whatever the filters above let through, so the buffer shows the same records
                    // the platform sinks received rather than a second, differently-filtered view.
                    logBuilder.AddProvider(new InMemoryLogProvider(appLogBuffer));
                }, enableUnoLogging: true)
                .UseConfiguration(configure: configBuilder =>
                    configBuilder
                        .EmbeddedSource<App>()
                        .Section<AppConfig>()
                )
                // Enable localization (see appsettings.json for supported languages)
                .UseLocalization()
                .ConfigureServices((context, services) =>
                {
                    services.AddOptions<FrameworkServiceControlOptions>()
                        .Bind(context.Configuration.GetSection("ServiceControl"));

                    // The same instance the logging provider above writes into, so the logs view reads the
                    // live buffer rather than an empty second one.
                    services.AddSingleton(appLogBuffer);
                    services.AddSingleton<UnitPreferenceCatalog>();
                    services.AddSingleton<FrameworkGrpcChannelFactory>();
                    services.AddSingleton<IFrameworkStatusClient, GrpcFrameworkStatusClient>();
                    services.AddSingleton<IFrameworkServiceConfigurationClient, GrpcFrameworkServiceConfigurationClient>();
                    services.AddSingleton<IFrameworkTelemetryClient, GrpcFrameworkTelemetryClient>();
                    services.AddSingleton<IFanCapabilityClient, GrpcFanCapabilityClient>();
                    services.AddSingleton<IFanControlStateClient, GrpcFanControlStateClient>();
                    services.AddSingleton<IFanStateClient, GrpcFanStateClient>();
                    services.AddSingleton<IFanTelemetryClient, FanTelemetryClient>();
                    services.AddSingleton<ITemperatureTelemetryClient, TemperatureTelemetryClient>();
                    services.AddSingleton<IBatteryTelemetryClient, BatteryTelemetryClient>();
                    services.AddSingleton<IPowerDeliveryClient, GrpcPowerDeliveryClient>();
                    services.AddSingleton<IModuleInventoryClient, GrpcModuleInventoryClient>();
                    // Display units are client-owned: selections persist in the per-user app-data folder
                    // and never travel to the background service.
                    services.AddSingleton<IUserUnitPreferencesClient, LocalUserUnitPreferencesClient>();
                    services.AddSingleton<IUnitFormattingService, UnitsNetUnitFormattingService>();
                    services.AddSingleton<IFrameworkFanControlClient, GrpcFrameworkFanControlClient>();
                    services.AddSingleton<IFanControlActuator, FanControlActuator>();
                    services.AddSingleton<IFanHistoryStore, FanHistoryStore>();
                    services.AddSingleton<FanTelemetryHub>();
                    services.AddSingleton<IHardwareInfoClient, GrpcHardwareInfoClient>();
                    // Decorated so every lifecycle action (Settings page, Warnings recovery page, …)
                    // raises a status notification with its outcome.
                    services.AddSingleton<LocalFrameworkServiceControlClient>();
                    services.AddSingleton<IFrameworkServiceControlClient>(static provider =>
                        new NotifyingFrameworkServiceControlClient(
                            provider.GetRequiredService<LocalFrameworkServiceControlClient>(),
                            provider.GetRequiredService<IDesktopNotificationService>()));
                    services.AddSingleton<DispatcherQueue>(DispatcherQueue.GetForCurrentThread());
                    services.AddSingleton<SynchronizationContext>(SynchronizationContext.Current!);

                    // Fan Control coordinator. Uno's nested-region navigation resolves a SEPARATE
                    // FanCurveProfilesModel for the mode body VMs (not the page-driven one), so they bridge to the
                    // displayed instance via FanCoordinatorAccessor (set in the coordinator's ctor) instead of DI.
                    services.AddSingleton<FanCurveProfilesModel>();
                    services.AddSingleton<FanCoordinatorAccessor>();

                    // Device Capabilities category bodies bridge to the displayed page model the same way
                    // (see DeviceCapabilitiesAccessor).
                    services.AddSingleton<DeviceCapabilitiesAccessor>();

                    // Modules layout bodies bridge to the displayed page model the same way (see ModulesAccessor).
                    services.AddSingleton<ModulesAccessor>();

                    // Tracks which page/section has unsaved staged edits, so the shell can warn before
                    // navigating away (registered by SettingsPage / FanCurveProfilesPage).
                    services.AddSingleton<SubZeroFramework.Services.Navigation.NavigationGuardRegistry>();

                    // Custom NavigationView navigator that prompts (via the registry above) before a user rail
                    // tap actually switches pages. Named "ConfirmNav" so ONLY MainNavigationView opts in via
                    // uen:Region.Navigator="ConfirmNav"; all other NavigationViews keep the stock navigator.
                    services.AddRegion<Microsoft.UI.Xaml.Controls.NavigationView, SubZeroFramework.Services.Navigation.ConfirmNavigationViewNavigator>(name: "ConfirmNav");

                    // Client-only settings: launch behavior + alert opt-ins persist next to the display units.
                    services.AddSingleton<ILocalClientSettingsStore, LocalClientSettingsStore>();
                    services.AddSingleton<ILocalFanProfileStore, LocalFanProfileStore>();
                    // Cross-platform launch-at-sign-in via the AutoLaunch library (HKCU Run key /
                    // freedesktop autostart / LaunchAgent behind one API).
                    services.AddSingleton<IStartupRegistrationService, AutoLaunchStartupRegistrationService>();
                    services.AddSingleton<IDesktopNotificationService, DesktopNotificationService>();
                    services.AddSingleton<ThermalAlertMonitor>();
                    services.AddSingleton<ServiceHealthNotifier>();
                })
                .UseNavigation(RegisterRoutes)
            );

        Logger = builder.Log();

        MainWindow = builder.Window;

        MainWindow.Title = $"SubZero Framework Edition";

        ApplyWindowIcon();

#if DEBUG
        MainWindow.UseStudio();
#endif

        ConfigureWindowTitleBar();
        MainWindow.SetWindowIcon();

        AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
        TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;
        App.Current.UnhandledException += Current_UnhandledException;

        Host = await builder.NavigateAsync<Shell>();

        Logger = Host.Log();

        // The units converter is built HERE, with the DI-resolved formatting service, and placed in
        // application resources — rather than being constructed by XAML, which cannot inject anything. A
        // XAML-constructed converter would need a static or a service locator to reach the service, making
        // the user's unit preference a hidden global and costing the formatting service its testability.
        var unitFormattingService = Host.Services.GetRequiredService<IUnitFormattingService>();
        Current.Resources["UnitFormat"] = new UnitFormatConverter(unitFormattingService);

        // Same converter, different empty state. Two instances rather than a second parameter because the
        // wording is a property of the SURFACE, not of the quantity: a live readout shows "--" for a reading
        // that has not arrived, while an inventory field shows "Unknown" for something the platform never
        // reports. Both distinctions already existed and neither should be flattened.
        Current.Resources["UnitFormatUnknown"] = new UnitFormatConverter(unitFormattingService)
        {
            UnavailableDisplay = "Unknown",
        };

        // Em-dash empty state: "not applicable here" (no adapter attached, device not reporting) rather than
        // "a reading that has not arrived yet" ("--") or "never reported by the platform" ("Unknown").
        Current.Resources["UnitFormatDash"] = new UnitFormatConverter(unitFormattingService)
        {
            UnavailableDisplay = "—",
        };

        // Bare number, no unit suffix — for the tiles that draw the value large and the unit small beside it,
        // where the suffix cannot be part of the same string.
        Current.Resources["UnitFormatValue"] = new UnitFormatConverter(unitFormattingService)
        {
            ValueOnly = true,
        };

        // Resolves a theme brush KEY per item, for collections whose rows carry their own colour. A
        // DataTemplate cannot pick a StaticResource per row, and putting a Brush on the model would create a
        // UI object off the UI thread — which fails silently in Uno.
        Current.Resources["ThemeBrushKey"] = new ThemeBrushKeyConverter();

        // The NUMERIC converters, for chart axis limits and steps — properties that take a double, not text.
        // Usable only where the bound source raises PropertyChanged: a converter cannot re-run by itself, so
        // a fixed bound converts in the view model instead. UnitValueStep treats its input as a DIFFERENCE,
        // which matters only for temperature — the one scale with an offset, where a 10 °C step is 18 °F.
        Current.Resources["UnitValue"] = new UnitValueConverter(unitFormattingService);
        Current.Resources["UnitValueStep"] = new UnitValueConverter(unitFormattingService) { IsDelta = true };

        // Client-only alert opt-ins (Settings → Startup & alerts).
        Host.Services.GetRequiredService<IDesktopNotificationService>().Start();
        Host.Services.GetRequiredService<ThermalAlertMonitor>().Start();
        Host.Services.GetRequiredService<ServiceHealthNotifier>().Start();
    }

    private void Current_UnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        Logger?.LogError(e.Exception, $"App Current unhandled exception! Message: {e.Message}");
    }

    private void TaskScheduler_UnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        Logger?.LogError(e.Exception, $"TaskScheduler unobserved task exception!");
    }

    private void CurrentDomain_UnhandledException(object sender, System.UnhandledExceptionEventArgs e)
    {
        Logger?.LogError(e.ExceptionObject as Exception, $"Current Domain unhandled exception! Is terminating: {e.IsTerminating}");
    }

    /// <summary>
    /// Gives the window (and therefore the taskbar button and Alt+Tab) the app icon.
    /// </summary>
    /// <remarks>
    /// An unpackaged WinUI 3 window does NOT inherit the icon embedded in the executable — without this it
    /// shows the generic placeholder on the taskbar even though the .exe itself has the right icon in Explorer.
    /// Uno.Resizetizer composes icon.ico next to the executable at build time, which is also what the installer
    /// lays down, so the icon follows the app rather than being duplicated in the repo.
    /// </remarks>
    private void ApplyWindowIcon()
    {
        if (MainWindow is null)
        {
            return;
        }

        try
        {
            var iconPath = Path.Combine(AppContext.BaseDirectory, "icon.ico");
            if (File.Exists(iconPath))
            {
                MainWindow.AppWindow.SetIcon(iconPath);
            }
            else
            {
                Logger?.LogWarning("Window icon {IconPath} is missing; the window keeps the platform default.", iconPath);
            }
        }
        catch (Exception exception)
        {
            // Cosmetic only — never let an icon stop the app from starting.
            Logger?.LogWarning(exception, "Failed to apply the window icon.");
        }
    }

    private void ConfigureWindowTitleBar()
    {
        if (MainWindow is null || !AppWindowTitleBar.IsCustomizationSupported())
        {
            return;
        }

        MainWindow.AppWindow.TitleBar.ExtendsContentIntoTitleBar = true;
        MainWindow.AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Standard;

#if HAS_UNO
        Uno.UI.Xaml.WindowHelper.SetBackground(MainWindow, (Brush)Current.Resources["SidebarBackgroundBrush"]);
#endif
    }

    private static void RegisterRoutes(IViewRegistry views, IRouteRegistry routes)
    {
        views.Register(
            new ViewMap<Shell, ShellModel>(),
            new ViewMap<MainPage, MainModel>(),
            new ViewMap<DashboardPage, DashboardModel>(),
            new ViewMap<DeviceCapabilitiesPage, DeviceCapabilitiesModel>(),
            new ViewMap<DeviceCapabilitiesOnboardCategoryView, DeviceCapabilitiesOnboardCategoryModel>(),
            new ViewMap<DeviceCapabilitiesCpuCategoryView, DeviceCapabilitiesCpuCategoryModel>(),
            new ViewMap<DeviceCapabilitiesMemoryCategoryView, DeviceCapabilitiesMemoryCategoryModel>(),
            new ViewMap<DeviceCapabilitiesStorageCategoryView, DeviceCapabilitiesStorageCategoryModel>(),
            new ViewMap<DeviceCapabilitiesGraphicsCategoryView, DeviceCapabilitiesGraphicsCategoryModel>(),
            new ViewMap<DeviceCapabilitiesNpuCategoryView, DeviceCapabilitiesNpuCategoryModel>(),
            new ViewMap<DeviceCapabilitiesNetworkCategoryView, DeviceCapabilitiesNetworkCategoryModel>(),
            new ViewMap<DeviceCapabilitiesSystemProfileCategoryView, DeviceCapabilitiesSystemProfileCategoryModel>(),
            // Instance detail bodies: resolved by DATA navigation — the category pickers pass the live card model.
            new DataViewMap<DeviceCapabilitiesCpuPackageDetailView, DeviceCapabilitiesCpuPackageDetailModel, DeviceCapabilitiesCpuPackageCardModel>(),
            new DataViewMap<DeviceCapabilitiesMemoryModuleDetailView, DeviceCapabilitiesMemoryModuleDetailModel, DeviceCapabilitiesMemoryModuleCardModel>(),
            new DataViewMap<DeviceCapabilitiesStorageDriveDetailView, DeviceCapabilitiesStorageDriveDetailModel, DeviceCapabilitiesStorageDriveCardModel>(),
            new DataViewMap<DeviceCapabilitiesGraphicsAdapterDetailView, DeviceCapabilitiesGraphicsAdapterDetailModel, DeviceCapabilitiesGraphicsCardGroupModel>(),
            new DataViewMap<DeviceCapabilitiesGraphicsMonitorDetailView, DeviceCapabilitiesGraphicsMonitorDetailModel, DeviceCapabilitiesMonitorCardModel>(),
            new DataViewMap<DeviceCapabilitiesNetworkAdapterDetailView, DeviceCapabilitiesNetworkAdapterDetailModel, DeviceCapabilitiesNetworkAdapterCardModel>(),
            new DataViewMap<DeviceCapabilitiesNpuDetailView, DeviceCapabilitiesNpuDetailModel, ComputeDeviceUsageCardModel>(),
            new ViewMap<ModulesPage, ModulesModel>(),
            new ViewMap<ModulesFw16View, ModulesFw16Model>(),
            new ViewMap<ModulesFw13View, ModulesFw13Model>(),
            new ViewMap<ModulesFw13ProView, ModulesFw13ProModel>(),
            new ViewMap<ModulesFw12View, ModulesFw12Model>(),
            new ViewMap<ModulesFwDesktopView, ModulesFwDesktopModel>(),
            new ViewMap<FanCurveProfilesPage, FanCurveProfilesModel>(),
            new ViewMap<FanAutoModeView, FanAutoModeModel>(),
            new ViewMap<FanManualModeView, FanManualModeModel>(),
            new ViewMap<FanMaxModeView, FanMaxModeModel>(),
            new ViewMap<FanCustomCurveView, FanCustomCurveModel>(),
            new ViewMap<FanAdaptiveModeView, FanAdaptiveModeModel>(),
            new ViewMap<PowerTelemetryPage, PowerTelemetryModel>(),
            new ViewMap<ThermalTelemetryPage, ThermalTelemetryModel>(),
            new ViewMap<WarningIssuesPage, WarningIssuesModel>(),
            new ViewMap<SettingsPage, SettingsModel>(),
            new ViewMap<SettingsServiceSectionView, SettingsServiceSectionModel>(),
            new ViewMap<SettingsUnitsSectionView, SettingsUnitsSectionModel>(),
            new ViewMap<SettingsStartupSectionView, SettingsStartupSectionModel>(),
            new ViewMap<SettingsLicensesSectionView, SettingsLicensesSectionModel>(),
            new ViewMap<SettingsLogsSectionView, SettingsLogsSectionModel>(),
            new ViewMap<SettingsAboutSectionView, SettingsAboutSectionModel>()
        );

        routes.Register(
            new RouteMap("", View: views.FindByViewModel<ShellModel>()),
            new RouteMap("Main", View: views.FindByViewModel<MainModel>(),
            Nested:
            [
                new RouteMap("Dashboard", View: views.FindByViewModel<DashboardModel>()),
                new RouteMap("DeviceCapabilities",  View: views.FindByViewModel<DeviceCapabilitiesModel>(),
                Nested:
                [
                    new RouteMap("Onboard", View: views.FindByViewModel<DeviceCapabilitiesOnboardCategoryModel>(), IsDefault: true),
                    new RouteMap("Cpu", View: views.FindByViewModel<DeviceCapabilitiesCpuCategoryModel>(),
                    Nested:
                    [
                        new RouteMap("CpuPackage", View: views.FindByViewModel<DeviceCapabilitiesCpuPackageDetailModel>()),
                    ]),
                    new RouteMap("Memory", View: views.FindByViewModel<DeviceCapabilitiesMemoryCategoryModel>(),
                    Nested:
                    [
                        new RouteMap("MemoryModule", View: views.FindByViewModel<DeviceCapabilitiesMemoryModuleDetailModel>()),
                    ]),
                    new RouteMap("Storage", View: views.FindByViewModel<DeviceCapabilitiesStorageCategoryModel>(),
                    Nested:
                    [
                        new RouteMap("StorageDrive", View: views.FindByViewModel<DeviceCapabilitiesStorageDriveDetailModel>()),
                    ]),
                    new RouteMap("Graphics", View: views.FindByViewModel<DeviceCapabilitiesGraphicsCategoryModel>(),
                    Nested:
                    [
                        new RouteMap("GraphicsAdapter", View: views.FindByViewModel<DeviceCapabilitiesGraphicsAdapterDetailModel>()),
                        new RouteMap("GraphicsMonitor", View: views.FindByViewModel<DeviceCapabilitiesGraphicsMonitorDetailModel>()),
                    ]),
                    new RouteMap("Npu", View: views.FindByViewModel<DeviceCapabilitiesNpuCategoryModel>(),
                    Nested:
                    [
                        new RouteMap("NpuDevice", View: views.FindByViewModel<DeviceCapabilitiesNpuDetailModel>()),
                    ]),
                    new RouteMap("Network", View: views.FindByViewModel<DeviceCapabilitiesNetworkCategoryModel>(),
                    Nested:
                    [
                        new RouteMap("NetworkAdapter", View: views.FindByViewModel<DeviceCapabilitiesNetworkAdapterDetailModel>()),
                    ]),
                    new RouteMap("Profile", View: views.FindByViewModel<DeviceCapabilitiesSystemProfileCategoryModel>()),
                ]),
                new RouteMap("Modules",  View: views.FindByViewModel<ModulesModel>(),
                Nested:
                [
                    new RouteMap("ModulesFw16", View: views.FindByViewModel<ModulesFw16Model>()),
                    new RouteMap("ModulesFw13", View: views.FindByViewModel<ModulesFw13Model>()),
                    new RouteMap("ModulesFw13Pro", View: views.FindByViewModel<ModulesFw13ProModel>()),
                    new RouteMap("ModulesFw12", View: views.FindByViewModel<ModulesFw12Model>()),
                    new RouteMap("ModulesFwDesktop", View: views.FindByViewModel<ModulesFwDesktopModel>()),
                ]),
                new RouteMap("FanCurveProfiles",  View: views.FindByViewModel<FanCurveProfilesModel>(),
                Nested:
                [
                    new RouteMap("Auto", View: views.FindByViewModel<FanAutoModeModel>(), IsDefault: true),
                    new RouteMap("Manual", View: views.FindByViewModel<FanManualModeModel>()),
                    new RouteMap("Max", View: views.FindByViewModel<FanMaxModeModel>()),
                    new RouteMap("Custom", View: views.FindByViewModel<FanCustomCurveModel>()),
                    new RouteMap("Adaptive", View: views.FindByViewModel<FanAdaptiveModeModel>()),
                ]),
                new RouteMap("PowerTelemetry",  View: views.FindByViewModel<PowerTelemetryModel>()),
                new RouteMap("ThermalTelemetry",  View: views.FindByViewModel<ThermalTelemetryModel>()),
                new RouteMap("WarningIssues",  View: views.FindByViewModel<WarningIssuesModel>()),
                new RouteMap("Settings",  View: views.FindByViewModel<SettingsModel>(),
                Nested:
                [
                    new RouteMap("SettingsService", View: views.FindByViewModel<SettingsServiceSectionModel>()),
                    new RouteMap("SettingsUnits", View: views.FindByViewModel<SettingsUnitsSectionModel>()),
                    new RouteMap("SettingsStartup", View: views.FindByViewModel<SettingsStartupSectionModel>()),
                    new RouteMap("SettingsLicenses", View: views.FindByViewModel<SettingsLicensesSectionModel>()),
                    new RouteMap("SettingsLogs", View: views.FindByViewModel<SettingsLogsSectionModel>()),
                    new RouteMap("SettingsAbout", View: views.FindByViewModel<SettingsAboutSectionModel>()),
                ]),
            ])
        );
    }
}
