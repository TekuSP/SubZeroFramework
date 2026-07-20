using System.Reactive.Disposables;
using System.Reactive.Disposables.Fluent;
using System.Reactive.Linq;

using CommunityToolkit.Mvvm.ComponentModel;

using Microsoft.UI.Dispatching;

using SubZeroFramework.Services;
using SubZeroFramework.Services.Navigation;

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
        IOptions<AppConfig> appInfo, INavigator navigator, IServiceProvider serviceProvider, DispatcherQueue dispatcherQueue, SynchronizationContext context, IFrameworkStatusClient frameworkStatusClient, NavigationGuardRegistry navigationGuardRegistry)
    {
        this.navigator = navigator;
        ServiceProvider = serviceProvider;
        this.dispatcherQueue = dispatcherQueue;
        this.context = context;
        this.frameworkStatusClient = frameworkStatusClient;
        GuardRegistry = navigationGuardRegistry;

        frameworkStatusClient
            .WatchStatus()
            .ObserveOn(context)
            .Subscribe(SystemStatusChanged)
            .DisposeWith(_subscriptions);
    }

    /// <summary>Last observed health, so redirects fire on a transition rather than on every emission.</summary>
    private bool? _wasWorking;

    private void SystemStatusChanged(FrameworkSystemStatus status)
    {
        bool isWorking = status.IsGrpcActive
            && status.IsLibraryAvailable
            && status.IsFrameworkDevice == true
            && !status.RequiresElevation
            && string.IsNullOrEmpty(status.LastError);

        // Redirect only when health actually FLIPS, never on every emission: the status stream re-emits on
        // each reconnect attempt (2 s), so a per-emission redirect repeatedly ejected anyone who had
        // deliberately navigated elsewhere while the service was down.
        var healthChanged = _wasWorking != isWorking;
        _wasWorking = isWorking;

        if (!isWorking)
        {
            IsDashboardEnabled = false;
            IsThermalTelemetryEnabled = false;
            IsPowerTelemetryEnabled = false;
            IsFanCurveProfilesEnabled = false;
            IsDeviceCapabilitiesEnabled = false;
            IsModulesEnabled = false;
            IsWarningIssuesEnabled = true;

            // Settings is exempt: Display units / Licenses / About work with no service at all, and the
            // Service pane is where the user installs or uninstalls one — bouncing them out of it would
            // make the documented "install via Settings → Service" flow impossible.
            if (healthChanged
                && SelectedItem is NavigationViewItemBase bs
                && bs.Tag?.ToString() is not ("WarningIssues" or "Settings"))
            {
                // A forced redirect. It sets SelectedItem directly (no ItemInvoked), so the unsaved-changes
                // guard — which only fires on user taps — does not block this bailout.
                navigator.NavigateRouteAsync(this, "/Main/WarningIssues");
            }

            return;
        }

        if (healthChanged && SelectedItem is NavigationViewItemBase bs2 && bs2.Tag?.ToString() == "WarningIssues")
        {
            navigator.NavigateRouteAsync(this, "/Main/Dashboard");
        }

        //For now enable all capabilities
        IsDashboardEnabled = true;
        IsThermalTelemetryEnabled = true;
        IsPowerTelemetryEnabled = true;
        IsFanCurveProfilesEnabled = true;
        IsDeviceCapabilitiesEnabled = true;
        // Pre-release: the Modules page is not production-ready (FFI slot-reporting gaps); keep the tab
        // disabled until it ships.
        IsModulesEnabled = false;
        IsWarningIssuesEnabled = false;
    }

    public void Dispose()
    {
        _subscriptions.Dispose();
    }

    /// <summary>The shell's unsaved-changes registry — read by MainPage's selection guard.</summary>
    public NavigationGuardRegistry GuardRegistry { get; }

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
    public partial bool IsModulesEnabled { get; set; }

    [ObservableProperty]
    public partial bool IsWarningIssuesEnabled { get; set; }

    public bool IsWarningIssuesSelected => SelectedItem is NavigationViewItemBase item
        && string.Equals(item.Tag?.ToString(), "WarningIssues", StringComparison.Ordinal);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsWarningIssuesSelected))]
    public partial object? SelectedItem { get; set; }
}
