using System.ComponentModel;

using SubZeroFramework.Controls.DeviceCapabilities.Models.Categories;

namespace SubZeroFramework.Presentation.MenuItems.DeviceCapabilities.Categories;

/// <summary>Onboard devices category body, resolved by the category navigation sub-region. DataContext is the <see cref="DeviceCapabilitiesOnboardCategoryModel"/>.</summary>
public sealed partial class DeviceCapabilitiesOnboardCategoryView : UserControl, INotifyPropertyChanged
{
    public DeviceCapabilitiesOnboardCategoryView()
    {
        this.InitializeComponent();
        DataContextChanged += (_, args) =>
        {
            if (args.NewValue is DeviceCapabilitiesOnboardCategoryModel model)
            {
                ViewModel = model;
            }
        };
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    [System.Diagnostics.CodeAnalysis.SuppressMessage("SubZeroFramework.Mvvm", "SZF0009:Avoid direct PropertyChanged event invocation", Justification = "Navigation sets DataContext; the CLR ViewModel property feeds compiled x:Bind without a dependency property.")]
    public DeviceCapabilitiesOnboardCategoryModel ViewModel
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
