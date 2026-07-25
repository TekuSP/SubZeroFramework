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
            Content = "Every fan goes back to the controller's automatic mode, and all saved fan settings are deleted: curve profiles in every slot, the active profile per fan, \"Applies to\" links, CPU boost, and manual or max overrides. This can't be undone.",
            PrimaryButtonText = "Reset fan settings",
            CloseButtonText = "Cancel",
            // Enter must not confirm an unrecoverable wipe — default to cancelling.
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = xamlRoot,
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        await ViewModel.ResetFanSettingsCommand.ExecuteAsync(null);
    }
}
