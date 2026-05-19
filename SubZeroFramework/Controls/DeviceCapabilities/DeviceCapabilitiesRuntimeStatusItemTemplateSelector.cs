using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SubZeroFramework.Controls.DeviceCapabilities.Models;

namespace SubZeroFramework.Controls.DeviceCapabilities;

public sealed class DeviceCapabilitiesRuntimeStatusItemTemplateSelector : DataTemplateSelector
{
    public DataTemplate? SuccessTemplate { get; set; }

    public DataTemplate? WarningTemplate { get; set; }

    public DataTemplate? ErrorTemplate { get; set; }

    public DataTemplate? DefaultTemplate { get; set; }

    protected override DataTemplate? SelectTemplateCore(object item)
    {
        return item is DeviceCapabilitiesRuntimeStatusItemModel statusItem
            ? statusItem.StatusTone switch
            {
                DeviceCapabilitiesStatusTone.Success => SuccessTemplate ?? DefaultTemplate,
                DeviceCapabilitiesStatusTone.Warning => WarningTemplate ?? DefaultTemplate,
                DeviceCapabilitiesStatusTone.Error => ErrorTemplate ?? DefaultTemplate,
                _ => DefaultTemplate,
            }
            : base.SelectTemplateCore(item);
    }

    protected override DataTemplate? SelectTemplateCore(object item, DependencyObject container)
        => SelectTemplateCore(item);
}
