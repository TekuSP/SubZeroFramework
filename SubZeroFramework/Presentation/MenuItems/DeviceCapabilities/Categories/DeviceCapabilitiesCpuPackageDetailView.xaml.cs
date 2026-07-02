using System.ComponentModel;

using SubZeroFramework.Controls.DeviceCapabilities.Models.Categories;

namespace SubZeroFramework.Presentation.MenuItems.DeviceCapabilities.Categories;

/// <summary>CPU package detail body, resolved by data navigation on the category's inner sub-region. DataContext is the <see cref="DeviceCapabilitiesCpuPackageDetailModel"/>.</summary>
public sealed partial class DeviceCapabilitiesCpuPackageDetailView : UserControl, INotifyPropertyChanged
{
    public DeviceCapabilitiesCpuPackageDetailView()
    {
        this.InitializeComponent();
        DataContextChanged += (_, args) =>
        {
            if (args.NewValue is DeviceCapabilitiesCpuPackageDetailModel model)
            {
                ViewModel = model;
            }
        };
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    [System.Diagnostics.CodeAnalysis.SuppressMessage("SubZeroFramework.Mvvm", "SZF0009:Avoid direct PropertyChanged event invocation", Justification = "Navigation sets DataContext; the CLR ViewModel property feeds compiled x:Bind without a dependency property.")]
    public DeviceCapabilitiesCpuPackageDetailModel ViewModel
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
