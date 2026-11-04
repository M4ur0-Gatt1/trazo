using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Microsoft.Graphics.Canvas.Text;
using Trazo.Rendering;

namespace Trazo.Rendering.Tests;

/// <summary>
/// Regresión del 2026-10-25: con <c>ms-appx:///</c>, Win2D lanzaba ArgumentException en la app sin empaquetar
/// (el instalador .exe) y se perdían la etiqueta de QuickShape y «Añadir texto».
/// </summary>
public class FontRegressionTests
{
    private static string RepoRoot([CallerFilePath] string file = "") => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(file)!, "..", ".."));

    private static double Width(string family)
    {
        using var fmt = new CanvasTextFormat { FontFamily = family, FontSize = 13 };
        using var layout = new CanvasTextLayout(Gpu.Device, "QuickShape creada", fmt, 400, 30);
        return layout.LayoutBounds.Width;
    }

    [Theory]
    [InlineData("Inter.ttf", "Inter")]
    [InlineData("SpaceGrotesk.ttf", "Space Grotesk")]
    public void BundledFontsLoadWithoutPackage(string file, string family)
    {
        var path = Path.Combine(RepoRoot(), "src", "Trazo.App", "Assets", "Fonts", file);
        Assert.True(File.Exists(path));
        // el proceso de pruebas tampoco está empaquetado: la forma antigua falla como fallaba en la app
        Assert.ThrowsAny<ArgumentException>(() => Width($"ms-appx:///Assets/Fonts/{file}#{family}"));
        // la forma nueva funciona y usa de verdad la fuente (mide distinto que la de reserva)
        var mine = Width(CanvasFonts.Family(path, family));
        Assert.NotEqual(Width("Segoe UI"), mine, 1);
    }

    /// <summary>
    /// Regresión del 2026-11-04 (la app se cerraba sola, 0xc000027b): resolver una tipografía propia lee el archivo
    /// con una llamada que, en el hilo de la interfaz, procesa mensajes. Un formato resuelto con
    /// <see cref="CanvasFonts.RealizeAsync"/> no vuelve a leer el archivo: sigue funcionando aunque ya no exista.
    /// </summary>
    [Fact]
    public async Task RealizedFormatNeverReadsTheFontFileAgain()
    {
        var dir = Path.Combine(Path.GetTempPath(), "trazo-font-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var copy = Path.Combine(dir, "Inter.ttf");
        File.Copy(Path.Combine(RepoRoot(), "src", "Trazo.App", "Assets", "Fonts", "Inter.ttf"), copy);
        using var fmt = await CanvasFonts.RealizeAsync(Gpu.Device, () => new CanvasTextFormat { FontFamily = CanvasFonts.Family(copy, "Inter"), FontSize = 13 });
        double Measure(CanvasTextFormat f)
        {
            using var layout = new CanvasTextLayout(Gpu.Device, "Línea creada", f, 400, 30);
            return layout.LayoutBounds.Width;
        }
        var before = Measure(fmt);
        Directory.Delete(dir, true);
        Assert.False(File.Exists(copy));
        // sin el archivo, un formato nuevo ya no encuentra la fuente; el resuelto mide igual que antes
        using var fresh = new CanvasTextFormat { FontFamily = CanvasFonts.Family(copy, "Inter"), FontSize = 13 };
        Assert.ThrowsAny<Exception>(() => Measure(fresh));
        Assert.Equal(before, Measure(fmt), 0.5); // Inter (≈79 px); con Segoe UI serían ≈110
        Assert.NotEqual(Width("Segoe UI"), before, 1);
    }

    /// <summary>
    /// Ningún código de la app resuelve <c>CanvasFonts.Ui</c> o <c>CanvasFonts.Display</c> en el hilo de la interfaz:
    /// solo dentro de <c>CanvasFonts.RealizeAsync</c> (o en la autoprueba, que no recibe entrada).
    /// </summary>
    [Fact]
    public void BundledFontsAreOnlyRealizedOffTheUiThread()
    {
        var offenders = Directory.EnumerateFiles(Path.Combine(RepoRoot(), "src", "Trazo.App"), "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .Where(f => !Path.GetFileName(f).Contains("SelfTest"))
            .SelectMany(f => File.ReadAllLines(f).Select((line, i) => (f, i, line)))
            .Where(x => Regex.IsMatch(x.line, @"CanvasFonts\.(Ui|Display)\b") && !x.line.Contains("RealizeAsync("))
            .Select(x => $"{Path.GetFileName(x.f)}:{x.i + 1}")
            .ToList();
        Assert.Empty(offenders);
    }

    [Fact]
    public void NoWin2DFontUsesMsAppx()
    {
        var offenders = Directory.EnumerateFiles(Path.Combine(RepoRoot(), "src"), "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .Where(f => Regex.IsMatch(File.ReadAllText(f), "FontFamily\\s*=\\s*\"ms-appx"))
            .ToList();
        Assert.Empty(offenders);
    }
}
