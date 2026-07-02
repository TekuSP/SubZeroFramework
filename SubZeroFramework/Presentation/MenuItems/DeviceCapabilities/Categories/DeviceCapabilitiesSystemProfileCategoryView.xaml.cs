using System.ComponentModel;

using SubZeroFramework.Controls.DeviceCapabilities.Models.Categories;

namespace SubZeroFramework.Presentation.MenuItems.DeviceCapabilities.Categories;

/// <summary>System profile category body, resolved by the category navigation sub-region. DataContext is the <see cref="DeviceCapabilitiesSystemProfileCategoryModel"/>.</summary>
public sealed partial class DeviceCapabilitiesSystemProfileCategoryView : UserControl, INotifyPropertyChanged
{
    public DeviceCapabilitiesSystemProfileCategoryView()
    {
        this.InitializeComponent();
        DataContextChanged += (_, args) =>
        {
            if (args.NewValue is DeviceCapabilitiesSystemProfileCategoryModel model)
            {
                ViewModel = model;
            }
        };
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    [System.Diagnostics.CodeAnalysis.SuppressMessage("SubZeroFramework.Mvvm", "SZF0009:Avoid direct PropertyChanged event invocation", Justification = "Navigation sets DataContext; the CLR ViewModel property feeds compiled x:Bind without a dependency property.")]
    public DeviceCapabilitiesSystemProfileCategoryModel ViewModel
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
