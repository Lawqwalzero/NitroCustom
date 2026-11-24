using System.Windows.Markup;

namespace NitroCustom.App;

/// <summary>XAML text in two languages: <c>Text="{local:Tr 'text in Russian', 'text in English'}"</c>.</summary>
[MarkupExtensionReturnType(typeof(string))]
public sealed class TrExtension(string ru, string en) : MarkupExtension
{
    public override object ProvideValue(IServiceProvider serviceProvider) => Core.Loc.T(ru, en);
}
