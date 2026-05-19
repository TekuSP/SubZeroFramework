using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

using SubZeroFramework.Controls.Fans.Models;

namespace SubZeroFramework.Controls.Fans;

public sealed class FanAdvancedInfoCardTemplateSelector : DataTemplateSelector
{
    public DataTemplate? FrameworkLaptop12Template { get; set; }

    public DataTemplate? FrameworkLaptop13Template { get; set; }

    public DataTemplate? FrameworkLaptop16Template { get; set; }

    public DataTemplate? FrameworkDesktopTemplate { get; set; }

    protected override DataTemplate? SelectTemplateCore(object item)
    {
        return item switch
        {
            FrameworkLaptop12FanAdvancedInfoCardModel => FrameworkLaptop12Template,
            FrameworkLaptop13FanAdvancedInfoCardModel => FrameworkLaptop13Template,
            FrameworkLaptop16FanAdvancedInfoCardModel => FrameworkLaptop16Template,
            FrameworkDesktopFanAdvancedInfoCardModel => FrameworkDesktopTemplate,
            _ => base.SelectTemplateCore(item),
        };
    }

    protected override DataTemplate? SelectTemplateCore(object item, DependencyObject container)
        => SelectTemplateCore(item);
}
