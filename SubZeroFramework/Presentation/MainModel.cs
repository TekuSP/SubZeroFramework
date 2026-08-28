using System.Reactive.Disposables;
using System.Reactive.Disposables.Fluent;
using System.Reactive.Linq;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.WinUI;

using DynamicData;

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
    private readonly IShellAccentPainter _shellAccentPainter;

    /// <summary>The profile library, mirrored so the active profile's tint can be looked up on any change.</summary>
    private readonly SourceCache<Models.CoolingProfile, string> _coolingProfiles = new(static profile => profile.Id);

    private string? _activeCoolingProfileId;
    public MainModel(
        IStringLocalizer localizer,
        IOptions<AppConfig> appInfo, INavigator navigator, IServiceProvider serviceProvider, DispatcherQueue dispatcherQueue, SynchronizationContext context, IFrameworkStatusClient frameworkStatusClient, NavigationGuardRegistry navigationGuardRegistry, ICoolingProfileClient coolingProfileClient, IShellAccentPainter shellAccentPainter)
    {
        this.navigator = navigator;
        _shellAccentPainter = shellAccentPainter;
        ServiceProvider = serviceProvider;
        this.dispatcherQueue = dispatcherQueue;
        this.context = context;
        this.frameworkStatusClient = frameworkStatusClient;
        GuardRegistry = navigationGuardRegistry;

        frameworkStatusClient
            .WatchStatus()
            .Sample(TelemetryRateLimits.LiveReadout)
            .ObserveOn(context)
            .Subscribe(SystemStatusChanged)
            .DisposeWith(_subscriptions);

        // The shell's tint. Observed on the UI thread because the painter animates a brush, and both halves
        // are needed to answer one question: which colour, if any, the active profile carries.
        coolingProfileClient
            .WatchCoolingProfiles()
            .ObserveOn(context)
            .Subscribe(changes =>
            {
                _coolingProfiles.Edit(updater => updater.Clone(changes));
                RefreshShellAccent();
            })
            .DisposeWith(_subscriptions);

        coolingProfileClient
            .WatchActiveProfileId()
            .ObserveOn(context)
            .Subscribe(activeProfileId =>
            {
                _activeCoolingProfileId = activeProfileId;
                RefreshShellAccent();
            })
            .DisposeWith(_subscriptions);

        // The startup update check is deliberately NOT started here. Uno hands nested regions their own
        // view-model instance, so the object whose constructor runs is not necessarily the one the page
        // binds to: a check started here updated an instance nothing was watching — the icon never tinted,
        // while clicking the rail item (which goes through the BOUND instance) worked every time.
        // MainPage starts it, on the instance it actually binds.
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

            // NOT forced. This fires when the service comes back and the app leaves the recovery screen —
            // which on a normal cold start is simply "the app finished starting", not a request. Forcing it
            // made the check speak on every launch, including to say "you're up to date", which is exactly
            // what the silent startup path exists to avoid. Only the rail button asks.
            _ = ShowUpdateNoticeAsync(force: false);
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

    /// <summary>
    /// Repaints the shell for whichever profile is selected.
    /// </summary>
    /// <remarks>
    /// No selection means NO tint, deliberately: black has to keep meaning "nothing chosen", or the colour
    /// stops carrying information and becomes decoration. A selection naming a profile that is no longer in
    /// the library counts as no selection for the same reason.
    /// </remarks>
    private void RefreshShellAccent()
    {
        var active = _activeCoolingProfileId is { } id ? _coolingProfiles.Lookup(id) : default;

        _shellAccentPainter.Apply(active.HasValue ? active.Value.AccentColorArgb : null);
    }

    public void Dispose()
    {
        _subscriptions.Dispose();
        _coolingProfiles.Dispose();
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

    private bool _startupUpdateCheckStarted;

    /// <summary>
    /// The shell has been navigated to — the first moment this view model is the one on screen.
    /// </summary>
    /// <remarks>
    /// The NavigationView writes this back through a TwoWay binding once navigation settles, so it fires on
    /// the instance the UI is actually bound to and only after the window is up. That is exactly what the
    /// startup update check needs, and it needs no page-side lifecycle plumbing to get it.
    /// </remarks>
    partial void OnSelectedItemChanged(object? value)
    {
        if (_startupUpdateCheckStarted || value is null)
        {
            return;
        }

        _startupUpdateCheckStarted = true;
        _ = ShowUpdateNoticeAsync(force: false);
    }

    /// <summary>
    /// Handles the rail items that are ERRANDS rather than destinations.
    /// </summary>
    /// <remarks>
    /// Check-for-updates is <c>SelectsOnInvoked="False"</c>, so it never becomes the selected item and
    /// SelectionChanged never fires for it — which is the point. Handling it there instead meant navigating
    /// to a region that does not exist and then going back, and that round trip is what blanked the content
    /// area. ItemInvoked fires for selecting and non-selecting items alike.
    /// </remarks>
    /// <param name="sender">The rail.</param>
    /// <param name="args">The invoked item.</param>
    public void OnNavigationItemInvoked(NavigationView sender, NavigationViewItemInvokedEventArgs args)
    {
        if (string.Equals((args.InvokedItemContainer as NavigationViewItemBase)?.Tag?.ToString(), "CheckForUpdates", StringComparison.OrdinalIgnoreCase))
        {
            _ = ShowUpdateNoticeAsync(force: true);
        }
    }

    /// <summary>
    /// Opens the release page and closes the notice.
    /// </summary>
    /// <param name="sender">The notice.</param>
    /// <param name="args">Unused.</param>
    public void OnUpdateNoticeActionClick(TeachingTip sender, object args)
    {
        // Already validated as an https://github.com/TekuSP/SubZeroFramework/ URL by the client.
        if (UpdateReleaseUrl is { Length: > 0 } url)
        {
            _ = Windows.System.Launcher.LaunchUriAsync(new Uri(url));
        }

        IsUpdateNoticeOpen = false;
    }

    /// <summary>
    /// Runs a check and opens the notice when it has something to say.
    /// </summary>
    /// <param name="force">True when the user pressed the rail button; false for the check at startup.</param>
    private async Task ShowUpdateNoticeAsync(bool force)
    {
        try
        {
            // The check marshals its own bindable writes, so it runs off the UI thread and only its RESULT
            // comes back — enqueuing the whole call would put an HTTP request on the UI thread for no reason.
            var hasNotice = await CheckForUpdatesAsync(force, CancellationToken.None).ConfigureAwait(false);

            // Opening is a property set, not a call into the tip: the TwoWay binding lets XAML do the open
            // when the visual tree is ready for it. Still a bindable write, so still marshalled.
            await dispatcherQueue.EnqueueAsync(() => IsUpdateNoticeOpen = hasNotice);
        }
        catch (Exception exception)
        {
            // Fire-and-forget: an unobserved exception here would take the app down, and an update check is
            // the last thing that should be able to do that.
            System.Diagnostics.Debug.WriteLine($"The update check failed: {exception}");
        }
    }

    // ----- Update notification -----

    /// <summary>Tint for the rail's update icon: amber while a newer release exists, otherwise inherited.</summary>
    /// <remarks>
    /// Assigned at runtime, never in a field initializer — a Brush built off the UI thread fails silently
    /// and takes the whole DataContext down with it. Null means "leave the icon alone", which is what the
    /// rail's other items get.
    /// </remarks>
    [ObservableProperty]
    public partial Microsoft.UI.Xaml.Media.Brush? UpdateIconBrush { get; private set; }

    /// <summary>Title for the update notice.</summary>
    [ObservableProperty]
    public partial string UpdateNoticeTitle { get; private set; } = string.Empty;

    /// <summary>Body for the update notice.</summary>
    [ObservableProperty]
    public partial string UpdateNoticeBody { get; private set; } = string.Empty;

    /// <summary>Label for the notice's action button, or null when there is nowhere to go.</summary>
    /// <remarks>
    /// Null rather than empty: TeachingTip shows the action button whenever the property carries a value, so
    /// only a null actually takes the button away on an up-to-date result.
    /// </remarks>
    [ObservableProperty]
    public partial string? UpdateNoticeActionText { get; private set; }

    /// <summary>
    /// Whether the update notice is showing. Bound TwoWay, so XAML owns the actual open.
    /// </summary>
    /// <remarks>
    /// Setting <c>TeachingTip.IsOpen</c> from code-behind meant fighting the visual tree's readiness: too
    /// early and the tip anchors to an element with no layout and never appears. Through a binding the
    /// framework opens it when it can, and TwoWay means a light-dismiss writes false back here rather than
    /// leaving this stuck true and the tip un-reopenable.
    /// </remarks>
    [ObservableProperty]
    public partial bool IsUpdateNoticeOpen { get; set; }

    /// <summary>The release page to open, already validated as a github.com URL by the client.</summary>
    public string? UpdateReleaseUrl { get; private set; }


    /// <summary>
    /// Runs an update check and prepares the notice copy.
    /// </summary>
    /// <param name="force">True when the user pressed the rail button; false for the check at startup.</param>
    /// <returns>True when there is something to show.</returns>
    /// <remarks>
    /// A forced check ALWAYS produces something to show, including "you are up to date" — a button that
    /// answers silently reads as broken. The startup check only speaks when there is news.
    /// </remarks>
    public async Task<bool> CheckForUpdatesAsync(bool force, CancellationToken cancellationToken)
    {
        var coordinator = ServiceProvider.GetService<Services.Updates.IUpdateNotificationCoordinator>();
        if (coordinator is null)
        {
            return false;
        }

        // ConfigureAwait(false): the fetch has no business holding the UI thread, and nothing below touches
        // a bindable property until the single marshalled block at the end.
        var availability = await coordinator.EvaluateAsync(force, cancellationToken).ConfigureAwait(false);

        // Decided off the UI thread on purpose — these are plain strings, so only the ASSIGNMENT needs
        // marshalling, not the reasoning. An empty title means "nothing to say", which is what a silent
        // startup check produces.
        var (title, body) = DescribeUpdateNotice(availability, force);

        await dispatcherQueue.EnqueueAsync(() =>
        {
            // Every bindable write lives in here. [ObservableProperty] raises PropertyChanged synchronously,
            // so assigning off the UI thread pushes a binding update onto the wrong thread — and the brush
            // below is worse still: AppThemeBrushes reaches into Application.Current.Resources, and a Brush
            // touched off-thread fails silently, taking the assignment with it.
            //
            // The amber tint tracks the FACT, not the notice: it stays after the notice is dismissed, and a
            // check that comes back clean is what clears it.
            //
            // A BRUSH OF ITS OWN, not AppThemeBrushes.Get. That helper hands back the very SolidColorBrush
            // instance App.xaml owns and shares with every {StaticResource StatusWarningBrush} in the app,
            // cached in a static dictionary for the process lifetime. Null means "no news", and the icon's
            // binding turns that into the rail's ordinary colour.
            UpdateIconBrush = availability.IsUpdateAvailable
                ? new SolidColorBrush(Themes.AppThemeBrushes.StatusWarningColor)
                : new SolidColorBrush(Themes.AppThemeBrushes.TextSecondaryColor);

            UpdateReleaseUrl = availability.ReleaseUrl;
            UpdateNoticeActionText = availability.IsUpdateAvailable ? "See what's new" : null;

            if (title.Length > 0)
            {
                UpdateNoticeTitle = title;
                UpdateNoticeBody = body;
            }
        });

        return title.Length > 0;
    }

    /// <summary>
    /// The notice copy for an outcome, or empty strings when there is nothing to say.
    /// </summary>
    /// <param name="availability">What the check found.</param>
    /// <param name="force">True when the user asked; a startup check stays silent unless there is news.</param>
    /// <returns>The title and body, both empty when the notice should not appear.</returns>
    /// <remarks>
    /// Static and string-only so it can run anywhere: keeping the wording out of the marshalled block leaves
    /// that block doing nothing but assigning, which is the part that genuinely needs the UI thread.
    /// </remarks>
    private static (string Title, string Body) DescribeUpdateNotice(Models.UpdateAvailability availability, bool force)
    {
        if (availability.IsUpdateAvailable)
        {
            return (
                $"SubZero {availability.LatestVersion} is available",
                availability.CurrentVersion is { } running
                    ? $"You're on {running}. See what changed on GitHub."
                    : "See what changed on GitHub.");
        }

        // No news, and nobody asked.
        if (!force)
        {
            return (string.Empty, string.Empty);
        }

        if (availability.Status == Models.UpdateCheckStatus.UpToDate)
        {
            return (
                "You're up to date",
                availability.CurrentVersion is { } currentVersion
                    ? $"SubZero {currentVersion} is the newest release."
                    : "No newer release was found.");
        }

        // Unknown: the feed could not be read, or this build carries no version to compare. Saying "up to
        // date" here would assert something the app has no evidence for.
        return (
            "Couldn't check for updates",
            availability.CurrentVersion is null
                ? "This build doesn't report its version, so there's nothing to compare against."
                : "GitHub could not be reached. Check your connection and try again.");
    }
}
