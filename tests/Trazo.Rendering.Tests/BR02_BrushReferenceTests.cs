using System.Diagnostics;
using System.Numerics;
using Trazo.Core.Brushes;
using Trazo.Core.Strokes;
using Windows.UI;

namespace Trazo.Rendering.Tests;

/// <summary>
/// BR-02 — Reproducibilidad del motor de pinceles (docs/protocols/BR-02.md).
/// Cada pincel de la biblioteca pinta la misma entrada fija con la misma semilla y se compara
/// con su imagen de referencia. Un cambio en el motor que altere un pincel sin querer falla aquí.
/// </summary>
public class BR02_BrushReferenceTests
{
    public const int Width = 384, Height = 160, Seed = 1234;
    public static readonly Color Ink = Color.FromArgb(255, 30, 60, 140);

    public static IEnumerable<object[]> BrushIds => new BrushLibrary().All.Select(b => new object[] { b.Id });

    /// <summary>Entrada de referencia: curva en S, presión en rampa y valle, inclinación media, 240 Hz.</summary>
    public static List<InputPoint> ReferenceInput() => SyntheticStrokes.Sample(
        t => new Vector2(24 + t * (Width - 48), Height / 2f + MathF.Sin(t * MathF.Tau) * Height * 0.28f),
        durationMs: 900, hz: 240, pressure: t => 0.15f + 0.85f * MathF.Sin(t * MathF.PI), tilt: 0.4f);

    public static float ReferenceSize(BrushPreset b) => Math.Clamp(b.Size, 6, 48);

    internal static (PaintDocument Doc, LayerOperations Ops, BrushEngine Engine) NewDocument()
    {
        var doc = PaintDocument.CreateBlank(Gpu.Device, Width, Height, "BR-02");
        doc.BackgroundVisible = false;
        var ops = new LayerOperations(doc);
        return (doc, ops, new BrushEngine(doc, ops, new TipCache(Gpu.Device)));
    }

    internal static void Paint(BrushEngine engine, BrushPreset brush, ToolKind tool = ToolKind.Brush, float opacity = 1)
    {
        var input = ReferenceInput();
        engine.Begin(input[0], tool, brush, Ink, ReferenceSize(brush), opacity, SymmetryMode.None, viewScale: 1, seed: Seed);
        foreach (var p in input.Skip(1)) engine.Add(p);
        engine.End();
    }

    [Theory]
    [MemberData(nameof(BrushIds))]
    public async Task BrushMatchesReference(string id)
    {
        var brush = new BrushLibrary()[id];
        var (doc, _, engine) = NewDocument();
        using (doc)
        {
            var before = GC.GetTotalMemory(false);
            var sw = Stopwatch.StartNew();
            Paint(engine, brush);
            using var flat = doc.Flatten(includeBackground: false);
            var pixels = flat.GetPixelBytes(); // fuerza a que la GPU termine antes de medir
            sw.Stop();

            var diff = await Golden.CheckAsync(flat, "brushes", id);
            Golden.AppendMetrics("br02-metrics.jsonl", new
            {
                brush = id, ms = sw.Elapsed.TotalMilliseconds, managedBytes = GC.GetTotalMemory(false) - before,
                diff.MaxChannelDiff, diff.MeanChannelDiff, diff.PercentPixelsOver8,
                coverage = pixels.Where((_, i) => i % 4 == 3).Count(a => a > 0) * 100.0 / (Width * Height),
            });
            Assert.True(diff.Within(), $"El pincel «{brush.Name}» cambió respecto de su referencia: {diff}");
        }
    }

    [Fact]
    public void SameStrokeRendersIdentically()
    {
        var brush = new BrushLibrary()["charcoal"]; // aleatoriedad en giro, tamaño y textura
        byte[] Render()
        {
            var (doc, _, engine) = NewDocument();
            using (doc)
            {
                Paint(engine, brush);
                using var flat = doc.Flatten(includeBackground: false);
                return flat.GetPixelBytes();
            }
        }
        Assert.Equal(Render(), Render());
    }

    [Fact]
    public void ToolOpacityCapsStrokeAlpha()
    {
        var (doc, _, engine) = NewDocument();
        using (doc)
        {
            Paint(engine, new BrushLibrary()["monoline"], opacity: 0.5f);
            var max = doc.Layers[0].Surface.GetPixelBytes().Where((_, i) => i % 4 == 3).Max();
            Assert.InRange(max, 120, 135); // ≈ 50 % aunque los sellos se superpongan
        }
    }

    [Fact]
    public void EraserRemovesExactlyWhereThePaintWas()
    {
        var (doc, _, engine) = NewDocument();
        using (doc)
        {
            var brush = new BrushLibrary()["monoline"];
            Paint(engine, brush);
            var painted = doc.Layers[0].Surface.GetPixelBytes();
            Paint(engine, brush, ToolKind.Eraser);
            var erased = doc.Layers[0].Surface.GetPixelBytes();
            // Borrar con la misma máscara: alfa' = a·(1 − b). Donde era opaco queda 0; en el borde
            // antialiasado queda un halo tenue (comportamiento correcto de "destination-out").
            long before = 0, after = 0;
            for (var i = 3; i < painted.Length; i += 4)
            {
                if (painted[i] == 255) Assert.True(erased[i] == 0, $"quedó alfa {erased[i]} en un píxel opaco");
                before += painted[i];
                after += erased[i];
            }
            Assert.True(after < before * 0.06, $"residuo {after * 100.0 / before:0.0} %");
        }
    }
}
