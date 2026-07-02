using System.ComponentModel;

using Microsoft.UI.Xaml.Input;

using SubZeroFramework.Controls.Modules;

namespace SubZeroFramework.Presentation.MenuItems.Modules.Layouts;

/// <summary>Framework Desktop modules layout body: front slots + the static rear-I/O catalog.</summary>
public sealed partial class ModulesFwDesktopView : UserControl, INotifyPropertyChanged
{
    public ModulesFwDesktopView()
    {
        this.InitializeComponent();
        DataContextChanged += (_, args) =>
        {
            if (args.NewValue is ModulesFwDesktopModel model)
            {
                ViewModel = model;
            }
        };
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    [System.Diagnostics.CodeAnalysis.SuppressMessage("SubZeroFramework.Mvvm", "SZF0009:Avoid direct PropertyChanged event invocation", Justification = "Navigation sets DataContext; the CLR ViewModel property feeds compiled x:Bind without a dependency property.")]
    public ModulesFwDesktopModel ViewModel
    {
        get => field;
        set
        {
            if (field == value) return;
            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ViewModel)));
        }
    } = default!;

    private void OnSlotCardTapped(object sender, TappedRoutedEventArgs e)
    {
        if ((sender as ModuleSlotCardView)?.Model is { } model)
        {
            ViewModel.Page.SelectSlot(model);
        }
    }

    private void OnRearPortSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (RearPortPicker.SelectedItem is ModulesRearPortInfo port)
        {
            ViewModel.SelectedRearPort = port;
        }
    }
}
