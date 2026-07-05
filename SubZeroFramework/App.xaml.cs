using System.Diagnostics.CodeAnalysis;

using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;

using LiveChartsCore.SkiaSharpView;

using Microsoft.Extensions.Options;

using SubZeroFramework.Controls.DeviceCapabilities.Models;
using SubZeroFramework.Controls.DeviceCapabilities.Models.Categories;
using SubZeroFramework.Controls.FanCurveProfiles.Models.Modes;
using SubZeroFramework.Presentation.MenuItems.Dashboard;
using SubZeroFramework.Presentation.MenuItems.DeviceCapabilities;
using SubZeroFramework.Presentation.MenuItems.DeviceCapabilities.Categories;
using SubZeroFramework.Presentation.MenuItems.FanCurveProfiles;
using SubZeroFramework.Presentation.MenuItems.FanCurveProfiles.Modes;
using SubZeroFramework.Presentation.MenuItems.Modules;
using SubZeroFramework.Presentation.MenuItems.Modules.Layouts;
using SubZeroFramework.Controls.Settings.Models.Sections;
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
                    // Configure log levels for different categories of logging
                    logBuilder
                        .SetMinimumLevel(
                            context.HostingEnvironment.IsDevelopment() ?
                                LogLevel.Information :
                                LogLevel.Warning)

                        // Default filters for core Uno Platform namespaces
                        .CoreLogLevel(LogLevel.Warning);

                    // Uno Platform namespace filter groups
                    // Uncomment individual methods to see more detailed logging
                    //// Generic Xaml events
                    logBuilder.XamlLogLevel(LogLevel.Debug);
                    //// Layout specific messages
                    logBuilder.XamlLayoutLogLevel(LogLevel.Debug);
                    //// Storage messages
                    //logBuilder.StorageLogLevel(LogLevel.Debug);
                    //// Binding related messages
                    logBuilder.XamlBindingLogLevel(LogLevel.Debug);
                    //// Binder memory references tracking
                    logBuilder.BinderMemoryReferenceLogLevel(LogLevel.Debug);
                    //// DevServer and HotReload related
                    logBuilder.HotReloadCoreLogLevel(LogLevel.Information);
                    //// Debug JS interop
                    //logBuilder.WebAssemblyLogLevel(LogLevel.Debug);
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
                    services.AddSingleton<IFrameworkServiceControlClient, LocalFrameworkServiceControlClient>();
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

                    // Settings section bodies bridge to the displayed page model the same way (see SettingsAccessor).
                    services.AddSingleton<SettingsAccessor>();

                    // Client-only settings: launch behavior + alert opt-ins persist next to the display units.
                    services.AddSingleton<ILocalClientSettingsStore, LocalClientSettingsStore>();
                    services.AddSingleton<IStartupRegistrationService, WindowsStartupRegistrationService>();
                    services.AddSingleton<ThermalAlertMonitor>();
                })
                .UseNavigation(RegisterRoutes)
            );

        Logger = builder.Log();

        MainWindow = builder.Window;

        MainWindow.Title = $"SubZero Framework Edition";

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

        // Client-only launch behavior + alert opt-ins (Settings → Startup & alerts).
        Host.Services.GetRequiredService<ThermalAlertMonitor>().Start();

        if (Host.Services.GetRequiredService<ILocalClientSettingsStore>().StartMinimized)
        {
            (MainWindow.AppWindow?.Presenter as OverlappedPresenter)?.Minimize();
        }
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
            new ViewMap<DeviceCapabilitiesNetworkCategoryView, DeviceCapabilitiesNetworkCategoryModel>(),
            new ViewMap<DeviceCapabilitiesSystemProfileCategoryView, DeviceCapabilitiesSystemProfileCategoryModel>(),
            // Instance detail bodies: resolved by DATA navigation — the category pickers pass the live card model.
            new DataViewMap<DeviceCapabilitiesCpuPackageDetailView, DeviceCapabilitiesCpuPackageDetailModel, DeviceCapabilitiesCpuPackageCardModel>(),
            new DataViewMap<DeviceCapabilitiesMemoryModuleDetailView, DeviceCapabilitiesMemoryModuleDetailModel, DeviceCapabilitiesMemoryModuleCardModel>(),
            new DataViewMap<DeviceCapabilitiesStorageDriveDetailView, DeviceCapabilitiesStorageDriveDetailModel, DeviceCapabilitiesStorageDriveCardModel>(),
            new DataViewMap<DeviceCapabilitiesGraphicsAdapterDetailView, DeviceCapabilitiesGraphicsAdapterDetailModel, DeviceCapabilitiesGraphicsCardGroupModel>(),
            new DataViewMap<DeviceCapabilitiesGraphicsMonitorDetailView, DeviceCapabilitiesGraphicsMonitorDetailModel, DeviceCapabilitiesMonitorCardModel>(),
            new DataViewMap<DeviceCapabilitiesNetworkAdapterDetailView, DeviceCapabilitiesNetworkAdapterDetailModel, DeviceCapabilitiesNetworkAdapterCardModel>(),
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
            new ViewMap<PowerTelemetryPage, PowerTelemetryModel>(),
            new ViewMap<ThermalTelemetryPage, ThermalTelemetryModel>(),
            new ViewMap<WarningIssuesPage, WarningIssuesModel>(),
            new ViewMap<SettingsPage, SettingsModel>(),
            new ViewMap<SettingsServiceSectionView, SettingsServiceSectionModel>(),
            new ViewMap<SettingsUnitsSectionView, SettingsUnitsSectionModel>(),
            new ViewMap<SettingsStartupSectionView, SettingsStartupSectionModel>(),
            new ViewMap<SettingsLicensesSectionView, SettingsLicensesSectionModel>(),
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
                    new RouteMap("SettingsAbout", View: views.FindByViewModel<SettingsAboutSectionModel>()),
                ]),
            ])
        );
    }
}
