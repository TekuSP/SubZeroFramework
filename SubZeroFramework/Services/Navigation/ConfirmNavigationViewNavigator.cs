using Microsoft.UI.Xaml.Controls;

using Uno.Extensions.Navigation;
using Uno.Extensions.Navigation.Navigators;
using Uno.Extensions.Navigation.Regions;

using SubZeroFramework.Presentation;

namespace SubZeroFramework.Services.Navigation;

/// <summary>
/// Custom NavigationView region navigator that intercepts a user-initiated top-level page change BEFORE it
/// is forwarded to the content region, so an unsaved-changes ContentDialog can veto the switch.
///
/// This overrides <see cref="ControlNavigator.CoreNavigateAsync"/> — NOT <c>RegionCanNavigate</c>, which does
/// NOT cancel (Uno forwards the request to child regions regardless of its result). <c>CoreNavigateAsync</c>
/// is the single choke point the request passes through before it reaches the content region: if we don't
/// call <c>base</c>, the content region never navigates, so the page CONTENT never switches (no flicker). On
/// "Stay" we only restore the rail highlight (one bounce; the tap already moved the selection).
///
/// Registered under the "ConfirmNav" navigator key; <c>MainNavigationView</c> opts in via
/// <c>uen:Region.Navigator="ConfirmNav"</c>. A user rail tap arrives with <c>request.Sender == Region.View</c>
/// (the NavigationView); programmatic navigations (service-down redirect, initial route) arrive with a
/// ViewModel sender, so they bypass the guard automatically.
/// </summary>
public sealed class ConfirmNavigationViewNavigator : NavigationViewNavigator
{
    private readonly NavigationGuardRegistry _guards;
    private readonly IDispatcher _dispatcher;

    // The page key + rail item that last actually navigated (i.e. the page currently shown). Used to know
    // what we are LEAVING and to restore the highlight on cancel.
    private string? _committedKey;
    private NavigationViewItemBase? _committedItem;

    public ConfirmNavigationViewNavigator(
        ILogger<NavigationViewNavigator> logger,
        IDispatcher dispatcher,
        IRegion region,
        IRouteResolver resolver,
        RegionControlProvider controlProvider,
        NavigationGuardRegistry guards)
        : base(logger, dispatcher, region, resolver, controlProvider)
    {
        _dispatcher = dispatcher;
        _guards = guards;
    }

    protected override async Task<NavigationResponse?> CoreNavigateAsync(NavigationRequest request)
    {
        var targetKey = request.Route?.Base;

        // Only a user rail tap (Sender == the NavigationView) can be guarded; programmatic navigations
        // (Sender == a ViewModel) flow straight through.
        var userInitiated = ReferenceEquals(request.Sender, Region.View);

        if (userInitiated
            && !string.IsNullOrEmpty(targetKey) // "Project Github" (no Region.Name) just opens a browser — never a real leave.
            && _committedKey is { } leavingKey
            && !string.Equals(targetKey, leavingKey, StringComparison.Ordinal)
            && _guards.ResolveGuard(leavingKey) is { HasUnsavedChanges: true } guard)
        {
            var confirmed = await _dispatcher.ExecuteAsync(async ct => await ConfirmDiscardAsync());
            if (!confirmed)
            {
                // Stay: never forward to the content region (the page content is untouched). Restore the
                // rail highlight to the page we stayed on — that re-selection navigates back to the current
                // page (target == leavingKey), which skips this guard as a no-op.
                await _dispatcher.ExecuteAsync(async ct =>
                {
                    if (Region.View is NavigationView view && _committedItem is not null)
                    {
                        view.SelectedItem = _committedItem;
                    }
                });

                return new NavigationResponse(Route.Empty);
            }

            await guard.DiscardUnsavedChangesAsync();
        }

        var response = await base.CoreNavigateAsync(request);

        // Record the now-current page for the next leave check / highlight restore.
        if (!string.IsNullOrEmpty(targetKey))
        {
            _committedKey = targetKey;
        }

        if (Region.View is NavigationView nv)
        {
            _committedItem = nv.SelectedItem as NavigationViewItemBase;
        }

        return response;
    }

    private async Task<bool> ConfirmDiscardAsync()
    {
        if (Region.View?.XamlRoot is not { } xamlRoot)
        {
            return true; // No visual root to prompt on — allow the navigation rather than trap the user.
        }

        return await UnsavedChangesPrompt.ConfirmDiscardAsync(xamlRoot);
    }
}
