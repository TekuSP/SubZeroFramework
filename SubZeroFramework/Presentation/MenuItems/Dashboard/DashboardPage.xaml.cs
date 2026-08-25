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
            ViewModel = model;
        }
    }

    // Tag carries the card model because ItemsRepeater x:Bind templates have no DataContext.
    private async void OnProfileClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not FanProfileCardModel card || _dialogOpen)
        {
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

    private async void OnSaveAsProfileClick(object sender, RoutedEventArgs e)
    {
        if (_dialogOpen || XamlRoot is null)
        {
            return;
        }

        // Captured BEFORE the dialog opens. The fans keep moving behind it, and saving whatever they happen
        // to be doing when the user finishes typing is not what they pressed the button to save.
        var captured = ViewModel.CaptureCurrentSetup(string.Empty);

        var model = new FanProfileNameDialogModel(
            new FanProfileCardModel(captured, ViewModel.UnitFormattingService).Description,
            [.. ViewModel.Profiles.Select(profile => profile.Name)]);

        var dialog = new FanProfileNameDialog(model) { XamlRoot = XamlRoot };

        if (await ShowDialogAsync(dialog).ConfigureAwait(true) == ContentDialogResult.Primary && model.IsValid)
        {
            ViewModel.SaveProfile(captured with { Name = model.TrimmedName });
        }
    }

    private async void OnManageProfilesClick(object sender, RoutedEventArgs e)
    {
        if (_dialogOpen || XamlRoot is null)
        {
            return;
        }

        var model = ViewModel.CreateManageProfilesModel();

        await ShowDialogAsync(new FanProfileManageDialog(model) { XamlRoot = XamlRoot }).ConfigureAwait(true);

        // Renames are held until the list closes; this is that moment.
        model.CommitRenames();
    }

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
