using System.Reactive.Linq;

using CommunityToolkit.Mvvm.ComponentModel;

using Hardware.Info;

using Microsoft.UI.Dispatching;

using SubZeroFramework.Presentation.MenuItems.WarningsIssues;
using SubZeroFramework.Services;

namespace SubZeroFramework.Presentation;

public partial class MainModel : ObservableObject, IDisposable
{
    public readonly INavigator navigator;
    private readonly DispatcherQueue dispatcherQueue;
    private readonly SynchronizationContext context;
    private readonly IHardwareInfo hwInfo;
    private readonly IFrameworkDataProvider frameworkDataProvider;
    private readonly IDisposable frameworkStatusProvider;
    public MainModel(
        IStringLocalizer localizer,
        IOptions<AppConfig> appInfo, INavigator navigator, IServiceProvider serviceProvider, DispatcherQueue dispatcherQueue, SynchronizationContext context, IHardwareInfo hwInfo, IFrameworkDataProvider frameworkDataProvider)
    {
        this.navigator = navigator;
        ServiceProvider = serviceProvider;
        this.dispatcherQueue = dispatcherQueue;
        this.context = context;
        this.hwInfo = hwInfo;
        this.frameworkDataProvider = frameworkDataProvider;

        frameworkStatusProvider = frameworkDataProvider.SystemStatus.ObserveOn(context).Subscribe(SystemStatusChanged);
    }

    private void SystemStatusChanged(FrameworkSystemStatus status)
    {
        bool isWorking = status.IsLibraryAvailable && status.IsFrameworkDevice == true;
        if (!isWorking || true) //Okay, we have a problem
        {
            //We have fatal problem
            IsDashboardEnabled = false;
            IsThermalTelemetryEnabled = false;
            IsPowerTelemetryEnabled = false;
            IsFanCurveProfilesEnabled = false;
            IsDeviceCapabilitiesEnabled = false;
            IsWarningIssuesEnabled = true;

            if (SelectedItem is NavigationViewItemBase bs && bs.Tag?.ToString() != "WarningIssues")
            {
                navigator.NavigateRouteAsync(this, "/Main/WarningIssues");
            }

            return;
        }

        if (SelectedItem is NavigationViewItemBase bs2 && bs2.Tag?.ToString() == "WarningIssues")
        {
            navigator.NavigateRouteAsync(this, "/Main/Dashboard");
        }

        //For now enable all capabilities
        IsDashboardEnabled = true;
        IsThermalTelemetryEnabled = true;
        IsPowerTelemetryEnabled = true;
        IsFanCurveProfilesEnabled = true;
        IsDeviceCapabilitiesEnabled = true;
        IsWarningIssuesEnabled = false;
    }

    public void Dispose()
    {
        frameworkStatusProvider.Dispose();
    }

    [ObservableProperty]
    public partial IServiceProvider ServiceProvider { get; set; }

    [ObservableProperty]
    public partial bool IsDashboardEnabled { get; set; }

    [ObservableProperty]
    public partial bool IsThermalTelemetryEnabled { get; set; }

    [ObservableProperty]
    public partial bool IsPowerTelemetryEnabled { get; set; }

    [ObservableProperty]
    public partial bool IsFanCurveProfilesEnabled { get; set; }

    [ObservableProperty]
    public partial bool IsDeviceCapabilitiesEnabled { get; set; }

    [ObservableProperty]
    public partial bool IsWarningIssuesEnabled { get; set; }

    [ObservableProperty]
    public partial object SelectedItem { get; set; }
}
