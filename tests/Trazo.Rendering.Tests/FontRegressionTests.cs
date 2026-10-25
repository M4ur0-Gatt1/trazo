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
