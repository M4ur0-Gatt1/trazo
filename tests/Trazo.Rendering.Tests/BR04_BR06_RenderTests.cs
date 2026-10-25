using System.Security.Cryptography;
using Trazo.Core.Brushes;
using Trazo.Core.Strokes;
using Trazo.Rendering;
using Windows.UI;

namespace Trazo.Rendering.Tests;

/// <summary>BR-04 (mezcla húmeda) y BR-06 (opacidad y flujo) con el motor real (WARP).</summary>
public class BR04_BR06_RenderTests
{
    private const int W = BR02_BrushReferenceTests.Width, H = BR02_BrushReferenceTests.Height;
    private static readonly Color Red = Color.FromArgb(255, 220, 30, 40);

    private static byte[] Paint(PaintDocument doc, BrushEngine engine, BrushPreset brush, Color color, float size, IEnumerable<InputPoint> pts)
    {
        var list = pts.ToList();
        engine.Begin(list[0], ToolKind.Brush, brush, color, size, 1, SymmetryMode.None, 1, seed: 1);
        foreach (var p in list.Skip(1)) engine.Add(p);
        engine.End();
        return doc.ActiveLayer.Surface.GetPixelBytes();
    }

    // ida y vuelta sobre la misma recta: muchos sellos superpuestos
    private static IEnumerable<InputPoint> Scrub(float pressure)
    {
        for (var pass = 0; pass < 6; pass++)
            for (var k = 0; k <= 40; k++)
            {
                var t = pass % 2 == 0 ? k : 40 - k;
                yield return new InputPoint(60 + t * 6, 80, pressure);
            }
    }

    [Fact]
    public void OpacityCapsAccumulationButFlowBuildsUp()
    {
        var round = new BrushLibrary()["roundbrush"];
        var capped = round.Clone();
        capped.Flow = 1;
        capped.FlowPressure = 0;
        capped.OpacityPressure = 1;
        capped.StreamLine = 0;
        var flowing = round.Clone();
        flowing.Flow = 1;
        flowing.FlowPressure = 1;
        flowing.OpacityPressure = 0;
        flowing.StreamLine = 0;

        int MaxAlpha(BrushPreset b)
        {
            var (doc, _, engine) = BR02_BrushReferenceTests.NewDocument();
            using (doc)
            {
                var px = Paint(doc, engine, b, Red, 30, Scrub(0.3f));
                return Enumerable.Range(0, W * H).Max(i => px[i * 4 + 3]);
            }
        }
        var cap = MaxAlpha(capped);
        var flow = MaxAlpha(flowing);
        Assert.InRange(cap, (int)(0.28 * 255), (int)(0.32 * 255));   // 30 % ± 2 %
        Assert.True(flow > 0.9 * 255, $"el flujo no se acumuló: {flow}");
    }

    private static (PaintDocument, BrushEngine) CanvasWithBlueBand()
    {
        var (doc, _, engine) = BR02_BrushReferenceTests.NewDocument();
        var px = new byte[W * H * 4];
        for (var y = 0; y < H; y++)
            for (var x = 150; x < 230; x++)
            {
                var o = (y * W + x) * 4;
                px[o] = 220; // azul (BGRA)
                px[o + 1] = 60;
                px[o + 2] = 20;
                px[o + 3] = 255;
            }
        doc.ActiveLayer.Surface.SetPixelBytes(px);
        doc.ActiveLayer.Touch();
        return (doc, engine);
    }

    private static BrushPreset Wet(float pull)
    {
        var b = new BrushLibrary()["roundbrush"].Clone();
        b.Flow = 1;
        b.FlowPressure = 0;
        b.StreamLine = 0;
        b.Wet = new WetMixSettings { Pull = pull, Charge = 1, Attack = 1 };
        return b;
    }

    private static IEnumerable<InputPoint> Across() => Enumerable.Range(0, 69).Select(k => new InputPoint(20 + k * 5, 80));

    [Fact]
    public void WetStrokePicksUpCanvasColor()
    {
        (byte B, byte R) After(float pull)
        {
            var (doc, engine) = CanvasWithBlueBand();
            using (doc)
            {
                var px = Paint(doc, engine, Wet(pull), Red, 24, Across());
                var o = (80 * W + 300) * 4;   // 70 px después de cruzar la banda azul
                Assert.Equal(255, px[o + 3]);
                return (px[o], px[o + 2]);
            }
        }
        var dry = After(0);
        var wet = After(0.4f);
        Assert.True(dry.B < 50, $"sin arrastre el trazo debería seguir rojo: B={dry.B}");
        Assert.True(wet.B > 90 && wet.R < dry.R, $"con arrastre el trazo debería salir azulado: B={wet.B} R={wet.R}");
    }

    [Fact]
    public void WetStrokeIsDeterministic()
    {
        string Run()
        {
            var (doc, engine) = CanvasWithBlueBand();
            using (doc) return Convert.ToHexString(SHA256.HashData(Paint(doc, engine, Wet(0.5f), Red, 24, Across())));
        }
        Assert.Equal(Run(), Run());
    }
}
