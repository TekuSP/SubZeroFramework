using System.ComponentModel;

using SubZeroFramework.Controls.Dashboard.Models;

namespace SubZeroFramework.Presentation.MenuItems.Dashboard;

public sealed partial class DashboardPage : Page, INotifyPropertyChanged
{
    public DashboardPage()
    {
        this.InitializeComponent();
        DataContextChanged += DataContextChanged_Handler;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    [System.Diagnostics.CodeAnalysis.SuppressMessage("SubZeroFramework.Mvvm", "SZF0009:Avoid direct PropertyChanged event invocation", Justification = "Page exposes ViewModel as a CLR property (not a DependencyProperty) to support compiled x:Bind; direct PropertyChanged invocation is required to push DataContext updates.")]
    public DashboardModel ViewModel
    {
        get => field;
        set
        {
            if (field == value) return;
            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ViewModel)));
        }
    } = default!;

    private void DataContextChanged_Handler(FrameworkElement sender, DataContextChangedEventArgs args)
    {
        if (args.NewValue is DashboardModel model)
        {
            // The page owns the dialogs, because a ContentDialog needs a XamlRoot and a view model has none.
            if (ViewModel is not null)
            {
                ViewModel.ProfileActionRequested -= OnProfileActionRequested;
            }

            ViewModel = model;
            ViewModel.ProfileActionRequested += OnProfileActionRequested;
        }
    }

    /// <summary>Turns a card's request into the dialog that answers it.</summary>
    private async void OnProfileActionRequested(object? sender, ProfileCardActionEventArgs args)
    {
        try
        {
            switch (args.Action)
            {
                case ProfileCardAction.Add:
                    await CreateProfileAsync().ConfigureAwait(true);
                    break;

                case ProfileCardAction.Rename when args.Profile is { } renaming:
                    await RenameProfileAsync(renaming).ConfigureAwait(true);
                    break;

                case ProfileCardAction.Edit when args.Profile is { } editing:
                    await EditProfileAsync(editing).ConfigureAwait(true);
                    break;

                case ProfileCardAction.Delete when args.Profile is { } deleting:
                    await DeleteProfileAsync(deleting).ConfigureAwait(true);
                    break;

                default:
                    break;
            }
        }
        catch (Exception exception)
        {
            // An async void handler means anything escaping here takes the app down, and a profile dialog is
            // not worth that.
            System.Diagnostics.Debug.WriteLine($"The profile action failed: {exception}");
        }
    }

    // Tag carries the card model because the item template binds it there.
    private async void OnProfileClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not FanProfileCardModel card || _dialogOpen)
        {
            return;
        }

        // The plus card is not a profile: clicking it makes one rather than applying anything.
        if (card.IsAddCard)
        {
            await CreateProfileAsync().ConfigureAwait(true);
            return;
        }

        var failures = await ViewModel.ApplyProfileAsync(card.Profile).ConfigureAwait(true);
        if (failures.Count == 0)
        {
            return;
        }

        // Named rather than counted. "Adaptive needs driving sensors on the Right fan" is something the user
        // can act on; "3 fans failed" sends them hunting.
        await ShowDialogAsync(new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = $"“{card.Name}” was only partly applied",
            Content = string.Join(Environment.NewLine, failures),
            CloseButtonText = "Close",
        }).ConfigureAwait(true);
    }

    /// <summary>Keeps the hand-made changes by writing them into the profile that is selected.</summary>
    /// <summary>Makes a new profile, with every fan on Auto.</summary>
    /// <remarks>
    /// From AUTO rather than from whatever the fans happen to be doing, so making a profile is a deliberate
    /// act from a known baseline. A profile becomes anything else the other way round: select it, change the
    /// fans by hand, then save those changes into it.
    /// </remarks>
    private async Task CreateProfileAsync()
    {
        if (_dialogOpen || XamlRoot is null)
        {
            return;
        }

        var captured = ViewModel.CreateAutoSetup(string.Empty);

        var model = new FanProfileNameDialogModel(ExistingNames());

        if (await ShowDialogAsync(new FanProfileNameDialog(model) { XamlRoot = XamlRoot }).ConfigureAwait(true) == ContentDialogResult.Primary
            && model.IsValid)
        {
            await ViewModel.SaveProfileAsync(captured with
            {
                Name = model.TrimmedName,
                IconName = model.SelectedIconName,
                AccentColorArgb = model.SelectedAccentArgb,
            }).ConfigureAwait(true);
        }
    }

    /// <summary>The name, and nothing else.</summary>
    private async Task RenameProfileAsync(CoolingProfile profile)
    {
        if (_dialogOpen || XamlRoot is null)
        {
            return;
        }

        var model = new FanProfileNameDialogModel(
            // Its OWN name excluded, so re-confirming an unchanged name is not reported as a collision.
            ExistingNames(profile.Id),
            ProfileDialogMode.Rename,
            profile);

        if (await ShowDialogAsync(new FanProfileNameDialog(model) { XamlRoot = XamlRoot }).ConfigureAwait(true) == ContentDialogResult.Primary
            && model.IsValid)
        {
            await ViewModel.RenameProfileAsync(profile.Id, model.TrimmedName).ConfigureAwait(true);
        }
    }

    /// <summary>Name, icon and colour. What the profile DOES is fixed when it is created.</summary>
    private async Task EditProfileAsync(CoolingProfile profile)
    {
        if (_dialogOpen || XamlRoot is null)
        {
            return;
        }

        var model = new FanProfileNameDialogModel(
            ExistingNames(profile.Id),
            ProfileDialogMode.Edit,
            profile);

        if (await ShowDialogAsync(new FanProfileNameDialog(model) { XamlRoot = XamlRoot }).ConfigureAwait(true) != ContentDialogResult.Primary
            || !model.IsValid)
        {
            return;
        }

        // Fans deliberately untouched: `with` carries them over unchanged, so editing appearance can never
        // rewrite the setup the user saved.
        await ViewModel.SaveProfileAsync(profile with
        {
            Name = model.TrimmedName,
            IconName = model.SelectedIconName,
            AccentColorArgb = model.SelectedAccentArgb,
        }).ConfigureAwait(true);
    }

    /// <summary>Confirms, then deletes. The one action here that destroys something.</summary>
    private async Task DeleteProfileAsync(CoolingProfile profile)
    {
        if (_dialogOpen || XamlRoot is null)
        {
            return;
        }

        var confirm = await ShowDialogAsync(new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = $"Delete “{profile.Name}”?",
            Content = "Are you sure you want to permanently delete this profile?",
            PrimaryButtonText = "Yes, delete",
            CloseButtonText = "Cancel",

            // Close is the default so Enter cannot delete a profile by reflex.
            DefaultButton = ContentDialogButton.Close,
        }).ConfigureAwait(true);

        if (confirm == ContentDialogResult.Primary)
        {
            await ViewModel.DeleteProfileAsync(profile.Id).ConfigureAwait(true);
        }
    }

    /// <summary>Names already taken, so the dialog can refuse a duplicate.</summary>
    /// <param name="exceptProfileId">A profile whose own name should not count against it.</param>
    private IReadOnlyCollection<string> ExistingNames(string? exceptProfileId = null)
        => [.. ViewModel.Profiles
            .Where(card => !card.IsAddCard && !string.Equals(card.Id, exceptProfileId, StringComparison.Ordinal))
            .Select(card => card.Name)];

    private bool _dialogOpen;

    /// <summary>
    /// Shows a dialog, guarding against a second one.
    /// </summary>
    /// <remarks>
    /// WinUI throws if a dialog opens while another is showing, and every entry point here is an async void
    /// handler — so an escaping exception would take the process with it rather than merely misbehaving.
    /// </remarks>
    private async Task<ContentDialogResult> ShowDialogAsync(ContentDialog dialog)
    {
        _dialogOpen = true;

        try
        {
            return await dialog.ShowAsync();
        }
        catch (Exception exception)
        {
            System.Diagnostics.Debug.WriteLine($"The profile dialog could not be shown: {exception}");
            return ContentDialogResult.None;
        }
        finally
        {
            _dialogOpen = false;
        }
    }
}
