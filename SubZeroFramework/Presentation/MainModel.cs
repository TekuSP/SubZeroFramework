using System.Reactive.Disposables;
using System.Reactive.Disposables.Fluent;
using System.Reactive.Linq;

using CommunityToolkit.Mvvm.ComponentModel;

using Microsoft.UI.Dispatching;

using SubZeroFramework.Services;

namespace SubZeroFramework.Presentation;

public partial class MainModel : ObservableObject, IDisposable
{
    public readonly INavigator navigator;
    private readonly CompositeDisposable _subscriptions = [];
    private readonly DispatcherQueue dispatcherQueue;
    private readonly SynchronizationContext context;
    private readonly IFrameworkStatusClient frameworkStatusClient;
    public MainModel(
        IStringLocalizer localizer,
        IOptions<AppConfig> appInfo, INavigator navigator, IServiceProvider serviceProvider, DispatcherQueue dispatcherQueue, SynchronizationContext context, IFrameworkStatusClient frameworkStatusClient)
    {
        this.navigator = navigator;
        ServiceProvider = serviceProvider;
        this.dispatcherQueue = dispatcherQueue;
        this.context = context;
        this.frameworkStatusClient = frameworkStatusClient;

        frameworkStatusClient
            .WatchStatus()
            .ObserveOn(context)
            .Subscribe(SystemStatusChanged)
            .DisposeWith(_subscriptions);
    }

    private void SystemStatusChanged(FrameworkSystemStatus status)
    {
        bool isWorking = status.IsGrpcActive
            && status.IsLibraryAvailable
            && status.IsFrameworkDevice == true
            && !status.RequiresElevation
            && string.IsNullOrEmpty(status.LastError);

        if (!isWorking)
        {
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
        _subscriptions.Dispose();
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
    public partial object? SelectedItem { get; set; }
}
