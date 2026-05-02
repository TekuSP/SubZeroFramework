using System;
using Microsoft.UI.Xaml.Markup;
using Material.Icons;
using Microsoft.UI.Xaml;

namespace Material.Icons.UNO;

[MarkupExtensionReturnType(ReturnType = typeof(MaterialIcon))]
public class MaterialIconExt : MarkupExtension
{
    public MaterialIconExt()
    { }

    public MaterialIconExt(MaterialIconKind kind)
    {
        Kind = kind;
    }

    public MaterialIconExt(MaterialIconKind kind, double size)
    {
        Kind = kind;
        Size = size;
    }

    // [ConstructorArgument("kind")]
    public MaterialIconKind Kind { get; set; }

    // [ConstructorArgument("size")]
    public double? Size { get; set; }

    protected override object ProvideValue(IXamlServiceProvider serviceProvider)
    {
        var result = new MaterialIcon { Kind = Kind };

        if (Size.HasValue)
        {
            result.Height = Size.Value;
            result.Width = Size.Value;
        }

        return result;
    }
}
