using Windows.UI;

namespace Trazo.Rendering;

/// <summary>Colores básicos sin depender de WinUI (la librería no usa XAML).</summary>
public static class KnownColors
{
    public static readonly Color Transparent = Color.FromArgb(0, 0, 0, 0);
    public static readonly Color White = Color.FromArgb(255, 255, 255, 255);
    public static readonly Color Black = Color.FromArgb(255, 0, 0, 0);
}
