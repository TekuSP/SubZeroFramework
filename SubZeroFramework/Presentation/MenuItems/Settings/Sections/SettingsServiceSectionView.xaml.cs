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
}
