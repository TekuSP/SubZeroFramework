using SubZeroFramework.Controls.Dashboard.Models;

namespace SubZeroFramework.Presentation.MenuItems.Dashboard;

/// <summary>Names a profile, and — unless renaming — chooses how it looks.</summary>
public sealed partial class FanProfileNameDialog : ContentDialog
{
    public FanProfileNameDialog(FanProfileNameDialogModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);

        ViewModel = viewModel;
        this.InitializeComponent();

        // The only thing there is to do here is type, so the caret starts where the typing goes.
        Opened += (_, _) => NameField.Focus(FocusState.Programmatic);
    }

    public FanProfileNameDialogModel ViewModel { get; }

    // Tag rather than DataContext: an ItemsControl template's buttons are easiest to read back this way, and
    // it keeps the selection logic in the model where the rest of it lives.
    private void OnIconClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is ProfileIconModel icon)
        {
            ViewModel.SelectIcon(icon);
        }
    }
}
