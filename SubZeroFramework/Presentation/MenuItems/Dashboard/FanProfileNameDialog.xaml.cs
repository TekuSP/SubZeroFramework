using SubZeroFramework.Controls.Dashboard.Models;

namespace SubZeroFramework.Presentation.MenuItems.Dashboard;

/// <summary>Names a new profile, and shows the setup it is about to capture.</summary>
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
}
