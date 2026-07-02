using System.ComponentModel;

namespace SubZeroFramework.Presentation.MenuItems.Modules.Layouts;

/// <summary>Framework 12 modules layout body (fixed input cover, own chassis art).</summary>
public sealed partial class ModulesFw12View : UserControl, INotifyPropertyChanged
{
    public ModulesFw12View()
    {
        this.InitializeComponent();
        DataContextChanged += (_, args) =>
        {
            if (args.NewValue is ModulesFw12Model model)
            {
                ViewModel = model;
            }
        };
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    [System.Diagnostics.CodeAnalysis.SuppressMessage("SubZeroFramework.Mvvm", "SZF0009:Avoid direct PropertyChanged event invocation", Justification = "Navigation sets DataContext; the CLR ViewModel property feeds compiled x:Bind without a dependency property.")]
    public ModulesFw12Model ViewModel
    {
        get => field;
        set
        {
            if (field == value) return;
            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ViewModel)));
        }
    } = default!;
}
