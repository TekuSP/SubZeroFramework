using System.ComponentModel;

using SubZeroFramework.Controls.DeviceCapabilities.Models.Categories;

namespace SubZeroFramework.Presentation.MenuItems.DeviceCapabilities.Categories;

/// <summary>Storage drive detail body, resolved by data navigation on the category's inner sub-region. DataContext is the <see cref="DeviceCapabilitiesStorageDriveDetailModel"/>.</summary>
public sealed partial class DeviceCapabilitiesStorageDriveDetailView : UserControl, INotifyPropertyChanged
{
    public DeviceCapabilitiesStorageDriveDetailView()
    {
        this.InitializeComponent();
        DataContextChanged += (_, args) =>
        {
            if (args.NewValue is DeviceCapabilitiesStorageDriveDetailModel model)
            {
                ViewModel = model;
            }
        };
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    [System.Diagnostics.CodeAnalysis.SuppressMessage("SubZeroFramework.Mvvm", "SZF0009:Avoid direct PropertyChanged event invocation", Justification = "Navigation sets DataContext; the CLR ViewModel property feeds compiled x:Bind without a dependency property.")]
    public DeviceCapabilitiesStorageDriveDetailModel ViewModel
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
