using System.ComponentModel;

using SubZeroFramework.Controls.Settings.Models.Sections;

namespace SubZeroFramework.Presentation.MenuItems.Settings.Sections;

/// <summary>Startup &amp; alerts section body, resolved by the section navigation sub-region. DataContext is the <see cref="SettingsStartupSectionModel"/>.</summary>
public sealed partial class SettingsStartupSectionView : UserControl, INotifyPropertyChanged
{
    public SettingsStartupSectionView()
    {
        this.InitializeComponent();
        DataContextChanged += (_, args) =>
        {
            if (args.NewValue is SettingsStartupSectionModel model)
            {
                ViewModel = model;
            }
        };
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    [System.Diagnostics.CodeAnalysis.SuppressMessage("SubZeroFramework.Mvvm", "SZF0009:Avoid direct PropertyChanged event invocation", Justification = "Navigation sets DataContext; the CLR ViewModel property feeds compiled x:Bind without a dependency property.")]
    public SettingsStartupSectionModel ViewModel
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
