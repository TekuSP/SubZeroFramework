using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Markup;

namespace Material.Icons.UNO;

public class GeometryConverter : IValueConverter
{
    public object? Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is string str)
        {
            var geometry = (Geometry)XamlReader.Load(
                "<Geometry xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'>"
                + str + "</Geometry>");
            return geometry;
        }

        return null;
    }

    public object? ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}
