using System.Diagnostics.CodeAnalysis;

using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;

using LiveChartsCore.SkiaSharpView;

using Microsoft.Extensions.Options;

using SubZeroFramework.Presentation.MenuItems.Dashboard;
using SubZeroFramework.Presentation.MenuItems.DeviceCapabilities;
using SubZeroFramework.Presentation.MenuItems.FanCurveProfiles;
using SubZeroFramework.Presentation.MenuItems.PowerTelemetry;
using SubZeroFramework.Presentation.MenuItems.Settings;
using SubZeroFramework.Presentation.MenuItems.ThermalTelemetry;
using SubZeroFramework.Presentation.MenuItems.WarningsIssues;
using SubZeroFramework.Services;

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
                    services.AddSingleton<FrameworkGrpcChannelFactory>();
                    services.AddSingleton<IFrameworkStatusClient, GrpcFrameworkStatusClient>();
                    services.AddSingleton<IFrameworkTelemetryClient, GrpcFrameworkTelemetryClient>();
                    services.AddSingleton<IFanCapabilityClient, GrpcFanCapabilityClient>();
                    services.AddSingleton<IFanControlStateClient, GrpcFanControlStateClient>();
                    services.AddSingleton<IFanStateClient, GrpcFanStateClient>();
                    services.AddSingleton<IFanTelemetryClient, FanTelemetryClient>();
                    services.AddSingleton<ITemperatureTelemetryClient, TemperatureTelemetryClient>();
                    services.AddSingleton<IBatteryTelemetryClient, BatteryTelemetryClient>();
                    services.AddSingleton<IFrameworkFanControlClient, GrpcFrameworkFanControlClient>();
                    services.AddSingleton<IHardwareInfoClient, GrpcHardwareInfoClient>();
                    services.AddSingleton<IFrameworkServiceControlClient, LocalFrameworkServiceControlClient>();
                    services.AddSingleton<DispatcherQueue>(DispatcherQueue.GetForCurrentThread());
                    services.AddSingleton<SynchronizationContext>(SynchronizationContext.Current!);
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
            new ViewMap<FanCurveProfilesPage, FanCurveProfilesModel>(),
            new ViewMap<PowerTelemetryPage, PowerTelemetryModel>(),
            new ViewMap<ThermalTelemetryPage, ThermalTelemetryModel>(),
            new ViewMap<WarningIssuesPage, WarningIssuesModel>(),
            new ViewMap<SettingsPage, SettingsModel>()
        );

        routes.Register(
            new RouteMap("", View: views.FindByViewModel<ShellModel>()),
            new RouteMap("Main", View: views.FindByViewModel<MainModel>(),
            Nested:
            [
                new RouteMap("Dashboard", View: views.FindByViewModel<DashboardModel>()),
                new RouteMap("DeviceCapabilities",  View: views.FindByViewModel<DeviceCapabilitiesModel>()),
                new RouteMap("FanCurveProfiles",  View: views.FindByViewModel<FanCurveProfilesModel>()),
                new RouteMap("PowerTelemetry",  View: views.FindByViewModel<PowerTelemetryModel>()),
                new RouteMap("ThermalTelemetry",  View: views.FindByViewModel<ThermalTelemetryModel>()),
                new RouteMap("WarningIssues",  View: views.FindByViewModel<WarningIssuesModel>()),
                new RouteMap("Settings",  View: views.FindByViewModel<SettingsModel>()),
            ])
        );
    }
}
