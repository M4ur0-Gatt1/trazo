using Microsoft.UI.Xaml.Markup;
using Trazo.Core.Localization;

namespace Trazo.App.Services;

/// <summary>Texto traducido en XAML (I18N-01): <c>Text="{t:Loc S='Capas'}"</c>. Se traduce al cargar la vista.</summary>
[MarkupExtensionReturnType(ReturnType = typeof(string))]
public sealed partial class Loc : MarkupExtension
{
    public string S { get; set; } = "";

    protected override object ProvideValue() => Tr.T(S);
}
