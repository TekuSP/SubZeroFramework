using System.ComponentModel;

namespace SubZeroFramework.Presentation.MenuItems.Settings.Sections;

/// <summary>Logs section body, resolved by the section navigation sub-region. DataContext is the <see cref="SettingsLogsSectionModel"/>.</summary>
public sealed partial class SettingsLogsSectionView : UserControl, INotifyPropertyChanged
{
    public SettingsLogsSectionView()
    {
        this.InitializeComponent();
        DataContextChanged += (_, args) =>
        {
            if (args.NewValue is SettingsLogsSectionModel model)
            {
                ViewModel = model;
            }
        };
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    [System.Diagnostics.CodeAnalysis.SuppressMessage("SubZeroFramework.Mvvm", "SZF0009:Avoid direct PropertyChanged event invocation", Justification = "Navigation sets DataContext; the CLR ViewModel property feeds compiled x:Bind without a dependency property.")]
    public SettingsLogsSectionModel ViewModel
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
