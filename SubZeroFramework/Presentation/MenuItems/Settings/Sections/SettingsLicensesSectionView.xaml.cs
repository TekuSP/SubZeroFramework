using System.ComponentModel;

using SubZeroFramework.Controls.Settings.Models.Sections;

namespace SubZeroFramework.Presentation.MenuItems.Settings.Sections;

/// <summary>Licenses section body, resolved by the section navigation sub-region. DataContext is the <see cref="SettingsLicensesSectionModel"/>.</summary>
public sealed partial class SettingsLicensesSectionView : UserControl, INotifyPropertyChanged
{
    public SettingsLicensesSectionView()
    {
        this.InitializeComponent();
        DataContextChanged += (_, args) =>
        {
            if (args.NewValue is SettingsLicensesSectionModel model)
            {
                ViewModel = model;
            }
        };
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    [System.Diagnostics.CodeAnalysis.SuppressMessage("SubZeroFramework.Mvvm", "SZF0009:Avoid direct PropertyChanged event invocation", Justification = "Navigation sets DataContext; the CLR ViewModel property feeds compiled x:Bind without a dependency property.")]
    public SettingsLicensesSectionModel ViewModel
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
