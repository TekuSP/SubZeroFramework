using System.ComponentModel;

namespace SubZeroFramework.Presentation.MenuItems.Settings.Sections;

/// <summary>Service lifecycle section body, resolved by the section navigation sub-region. DataContext is the <see cref="SettingsServiceSectionModel"/>.</summary>
public sealed partial class SettingsServiceSectionView : UserControl, INotifyPropertyChanged
{
    public SettingsServiceSectionView()
    {
        this.InitializeComponent();
        DataContextChanged += (_, args) =>
        {
            if (args.NewValue is SettingsServiceSectionModel model)
            {
                ViewModel = model;
            }
        };
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    [System.Diagnostics.CodeAnalysis.SuppressMessage("SubZeroFramework.Mvvm", "SZF0009:Avoid direct PropertyChanged event invocation", Justification = "Navigation sets DataContext; the CLR ViewModel property feeds compiled x:Bind without a dependency property.")]
    public SettingsServiceSectionModel ViewModel
    {
        get => field;
        set
        {
            if (field == value) return;
            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ViewModel)));
        }
    } = default!;

    /// <summary>
    /// Confirms the fan-settings factory reset before running it. The confirmation lives here rather than in
    /// the ViewModel because a <see cref="ContentDialog"/> needs a XamlRoot, which only a visual has — the
    /// same reason <see cref="UnsavedChangesPrompt"/> takes one.
    /// </summary>
    private async void OnResetFanSettingsClick(object sender, RoutedEventArgs e)
    {
        if (XamlRoot is not { } xamlRoot)
        {
            return;
        }

        var dialog = new ContentDialog
        {
            Title = "Reset fan settings to factory defaults?",
            Content = "Every fan goes back to the controller's automatic mode, and all saved fan settings are deleted: curve profiles in every slot, the active profile per fan, \"Applies to\" links, and manual or max overrides. This can't be undone.",
            PrimaryButtonText = "Reset fan settings",
            CloseButtonText = "Cancel",
            // Enter must not confirm an unrecoverable wipe — default to cancelling.
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = xamlRoot,
        };

        AccentDialogPalette.Apply(dialog);

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        await ViewModel.ResetFanSettingsCommand.ExecuteAsync(null);
    }

    /// <summary>
    /// Confirms uninstalling before it runs. On an installed build this removes the whole application, which
    /// is a much larger action than the button used to perform, so it is never done on a single click.
    /// </summary>
    /// <remarks>
    /// Windows Installer will not ask again: <c>/x</c> preselects the remove action, which suppresses the
    /// installer's own maintenance confirmation and goes straight to the progress UI. This dialog is
    /// therefore the only confirmation the user gets.
    /// </remarks>
    private async void OnUninstallClick(object sender, RoutedEventArgs e)
    {
        if (XamlRoot is not { } xamlRoot)
        {
            return;
        }

        var uninstallsApplication = ViewModel.IsApplicationInstalledByInstaller;

        var dialog = new ContentDialog
        {
            Title = uninstallsApplication ? "Uninstall SubZero?" : "Remove the background service?",
            Content = uninstallsApplication
                // Says plainly what survives: the package deliberately keeps machine settings so a reinstall
                // does not lose fan profiles, and implying a clean wipe here would be untrue.
                ? "Windows Installer will remove SubZero and its background service, and this app will close so its files can be deleted. Your saved fan profiles and settings are kept in case you reinstall."
                : "This removes the background service only. Fan control and telemetry stop working until it is installed again; the app itself stays.",
            PrimaryButtonText = uninstallsApplication ? "Uninstall SubZero" : "Remove service",
            CloseButtonText = "Cancel",
            // Enter must not trigger a removal.
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = xamlRoot,
        };

        AccentDialogPalette.Apply(dialog);

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        await ViewModel.UninstallServiceCommand.ExecuteAsync(null);
    }
}
