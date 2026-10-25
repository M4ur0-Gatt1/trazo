namespace Trazo.Rendering;

/// <summary>
/// Tipografías propias para Win2D. Win2D no resuelve <c>ms-appx:///</c> en una app sin empaquetar (lanza
/// ArgumentException; el instalador .exe lo es), así que se usa la ruta del archivo como URI <c>file:///</c>,
/// que funciona igual empaquetada o no. XAML sí resuelve <c>ms-appx</c> y no necesita esto.
/// </summary>
public static class CanvasFonts
{
    /// <summary>Familia para <c>CanvasTextFormat.FontFamily</c> a partir de un archivo de fuente.</summary>
    public static string Family(string fontFile, string familyName) => new Uri(Path.GetFullPath(fontFile)).AbsoluteUri + "#" + familyName;

    private static string Asset(string file) => Path.Combine(AppContext.BaseDirectory, "Assets", "Fonts", file);

    /// <summary>Inter, la tipografía de la interfaz (junto al ejecutable).</summary>
    public static string Ui => Family(Asset("Inter.ttf"), "Inter");

    /// <summary>Space Grotesk, la tipografía de los títulos (junto al ejecutable).</summary>
    public static string Display => Family(Asset("SpaceGrotesk.ttf"), "Space Grotesk");
}
