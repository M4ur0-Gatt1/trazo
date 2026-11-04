using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Text;

namespace Trazo.Rendering;

/// <summary>
/// Tipografías propias para Win2D. Win2D no resuelve <c>ms-appx:///</c> en una app sin empaquetar (lanza
/// ArgumentException; el instalador .exe lo es), así que se usa la ruta del archivo como URI <c>file:///</c>,
/// que funciona igual empaquetada o no. XAML sí resuelve <c>ms-appx</c> y no necesita esto.
/// <para>
/// <b>Solo se resuelven fuera del hilo de la interfaz</b> (<see cref="RealizeAsync"/>). Para leer el archivo, Win2D
/// llama de forma síncrona a <c>StorageFile.GetFileFromPathAsync</c> (una llamada entre apartamentos COM). En el hilo
/// de la interfaz, esa espera procesa mensajes: si llega un movimiento del lápiz mientras se dibuja un fotograma,
/// WinUI detecta la reentrada y cierra la app (0xc000027b, regresión del 2026-11-04 con la etiqueta de QuickShape).
/// </para>
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

    /// <summary>Tipografía del sistema: se resuelve sin leer archivos, sirve en cualquier hilo.</summary>
    public const string System = "Segoe UI";

    /// <summary>
    /// Crea el formato y lo resuelve en un hilo del grupo. Win2D guarda el formato resuelto en el propio objeto, y
    /// mientras no se cambien sus propiedades se puede usar después en el hilo de la interfaz sin volver a leer el
    /// archivo de la fuente.
    /// </summary>
    public static Task<CanvasTextFormat> RealizeAsync(ICanvasResourceCreator device, Func<CanvasTextFormat> create) => Task.Run(() =>
    {
        var format = create();
        try
        {
            using var layout = new CanvasTextLayout(device, " ", format, 1, 1);
            return format;
        }
        catch
        {
            format.Dispose();
            throw;
        }
    });
}
