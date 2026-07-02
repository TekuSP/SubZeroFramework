using System.ComponentModel;

using Microsoft.UI.Xaml.Input;

using SubZeroFramework.Controls.Modules;

namespace SubZeroFramework.Presentation.MenuItems.Modules.Layouts;

/// <summary>Framework 16 modules layout body, resolved by the Modules page's layout sub-region. DataContext is
/// the <see cref="ModulesFw16Model"/>; slot/deck card taps drive the page model's selection.</summary>
public sealed partial class ModulesFw16View : UserControl, INotifyPropertyChanged
{
    public ModulesFw16View()
    {
        this.InitializeComponent();
        DataContextChanged += (_, args) =>
        {
            if (args.NewValue is ModulesFw16Model model)
            {
                ViewModel = model;
            }
        };
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    [System.Diagnostics.CodeAnalysis.SuppressMessage("SubZeroFramework.Mvvm", "SZF0009:Avoid direct PropertyChanged event invocation", Justification = "Navigation sets DataContext; the CLR ViewModel property feeds compiled x:Bind without a dependency property.")]
    public ModulesFw16Model ViewModel
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

    private void OnDeckCardTapped(object sender, TappedRoutedEventArgs e)
    {
        if ((sender as ModuleDeckCardView)?.Model is { } model)
        {
            ViewModel.Page.SelectDeckCard(model);
        }
    }

    private void OnBayTapped(object sender, TappedRoutedEventArgs e) => ViewModel.Page.SelectBay();
}
