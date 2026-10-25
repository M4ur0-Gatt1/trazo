using Microsoft.Graphics.Canvas;
using Trazo.Core.Brushes;
using Trazo.Core.IO;
using Trazo.Core.Strokes;
using Trazo.Rendering;
using Windows.UI;

namespace Trazo.Rendering.Tests;

/// <summary>RC-01 — Recuperación tras fallos, con el motor real (WARP).</summary>
public class RC01_RenderTests
{
    private const int W = BR02_BrushReferenceTests.Width, H = BR02_BrushReferenceTests.Height;

    private static void Stroke(BrushEngine engine, float y, Color color)
    {
        var brush = new BrushLibrary()["monoline"];
        engine.Begin(new InputPoint(20, y), ToolKind.Brush, brush, color, 10, 1, SymmetryMode.None, 1, seed: 1);
        for (var k = 1; k <= 30; k++) engine.Add(new InputPoint(20 + k * 10, y));
        engine.End();
    }

    [Fact]
    public async Task CrashKeepsLastSavedWork()
    {
        using var tmp = new TempFolder();
        var file = tmp.File("obra.trazo");
        var (doc, _, engine) = BR02_BrushReferenceTests.NewDocument();
        byte[] saved;
        using (doc)
        {
            Stroke(engine, 50, Color.FromArgb(255, 200, 30, 30));
            await ProjectSaver.SaveAsync(file, await doc.ToProjectDataAsync());
            saved = doc.ActiveLayer.Surface.GetPixelBytes();
            // trabajo posterior que no llegó a guardarse antes del «cierre»
            Stroke(engine, 110, Color.FromArgb(255, 30, 30, 200));
        }

        // al reabrir en otro dispositivo (como tras perder la GPU) está lo guardado y nada más
        using var other = new CanvasDevice(forceSoftwareRenderer: true);
        var result = ProjectSaver.Load(file);
        Assert.False(result.Recovered);
        using var reopened = await PaintDocument.FromProjectDataAsync(other, result.Project, file);
        Assert.Same(other, reopened.Device);
        var px = reopened.ActiveLayer.Surface.GetPixelBytes();
        // PNG guarda sin premultiplicar: ± 1 nivel por redondeo (FI-01.6)
        var maxDiff = Enumerable.Range(0, px.Length).Max(i => Math.Abs(px[i] - saved[i]));
        Assert.True(maxDiff <= 1, $"diferencia máxima {maxDiff}");
        Assert.Contains(Enumerable.Range(0, W), x => px[(50 * W + x) * 4 + 3] == 255);   // el trazo guardado
        Assert.DoesNotContain(Enumerable.Range(0, W), x => px[(110 * W + x) * 4 + 3] > 0); // el no guardado
        _ = H;
    }
}
