using System.Numerics;
using Trazo.Core.Brushes;
using Trazo.Core.Strokes;
using Windows.UI;

namespace Trazo.Rendering.Tests;

/// <summary>
/// BR-03 (grano) y BR-05 (afinado) con el motor real sobre WARP
/// (docs/specs/BR-03-grain.md, docs/specs/BR-05-taper.md).
/// </summary>
public class BR03_BR05_GrainTaperRenderTests
{
    private const int W = 384, H = 160;
    private static readonly Color Ink = BR02_BrushReferenceTests.Ink;

    private static BrushPreset Solid(GrainSettings? grain = null, TaperSettings? taper = null, TaperSettings? touch = null) => new()
    {
        Id = "test", Name = "prueba", Tip = TipShape.Round, Size = 40, MaxSize = 200, Spacing = 0.05f, MinSizePressure = 1,
        StreamLine = 0, Grain = grain ?? GrainSettings.Off, PressureTaper = taper ?? TaperSettings.None, TouchTaper = touch ?? TaperSettings.None,
    };

    private static List<InputPoint> Line(float x0, float x1, float y = H / 2f) =>
        SyntheticStrokes.Sample(SyntheticStrokes.Line(new(x0, y), new(x1, y)), 600, 240);

    /// <summary>Pinta y devuelve los bytes BGRA de la capa.</summary>
    private static byte[] Paint(BrushPreset brush, float size, IReadOnlyList<InputPoint> input, bool hasPressure = true, int seed = 1)
    {
        var (doc, _, engine) = BR02_BrushReferenceTests.NewDocument();
        using (doc)
        {
            engine.Begin(input[0], ToolKind.Brush, brush, Ink, size, 1, SymmetryMode.None, 1, seed, hasPressure);
            foreach (var p in input.Skip(1)) engine.Add(p);
            engine.End();
            return doc.Layers[0].Surface.GetPixelBytes();
        }
    }

    private static byte A(byte[] px, int x, int y) => px[(y * W + x) * 4 + 3];

    /// <summary>Alfas de la franja central del trazo (lejos de los bordes de la punta).</summary>
    private static List<double> Core(byte[] px, int x0, int x1) =>
        (from y in Enumerable.Range(H / 2 - 6, 12) from x in Enumerable.Range(x0, x1 - x0) select (double)A(px, x, y)).ToList();

    private static double Mean(List<double> v) => v.Average();
    private static double Std(List<double> v) { var m = v.Average(); return Math.Sqrt(v.Average(x => (x - m) * (x - m))); }

    // ---------- BR-03: grano ----------

    [Theory]
    [InlineData(GrainMode.Texturized)]
    [InlineData(GrainMode.Moving)]
    public void ZeroDepthIsIdenticalToNoGrain(GrainMode mode)
    {
        var plain = Paint(Solid(), 40, Line(40, 340));
        var zero = Paint(Solid(new GrainSettings { Mode = mode, Depth = 0 }), 40, Line(40, 340));
        Assert.Equal(plain, zero);
    }

    [Fact]
    public void GrainAddsMeasurableTexture()
    {
        var plain = Core(Paint(Solid(), 40, Line(40, 340)), 100, 280);
        var grained = Core(Paint(Solid(new GrainSettings { Mode = GrainMode.Texturized, Kind = GrainKind.Paper, Depth = 0.8f, Scale = 0.25f }), 40, Line(40, 340)), 100, 280);
        Assert.True(Std(plain) < 1, $"sin grano debería ser uniforme (σ = {Std(plain):0.00})");
        Assert.True(Std(grained) > 20, $"el grano debería añadir variación (σ = {Std(grained):0.00})");
    }

    [Fact]
    public void DepthMonotonicallyReducesCoverage()
    {
        var previous = double.MaxValue;
        foreach (var depth in new[] { 0f, 0.25f, 0.5f, 0.75f, 1f })
        {
            var m = Mean(Core(Paint(Solid(new GrainSettings { Mode = GrainMode.Texturized, Depth = depth, Scale = 0.25f }), 40, Line(40, 340)), 100, 280));
            Assert.True(m <= previous + 0.5, $"Depth {depth}: media {m:0.0} > {previous:0.0}");
            previous = m;
        }
    }

    // Texturized: el patrón está anclado al lienzo, no al trazo
    [Fact]
    public void TexturizedGrainIsAnchoredToCanvas()
    {
        var g = Solid(new GrainSettings { Mode = GrainMode.Texturized, Depth = 0.9f, Scale = 0.25f });
        var a = Paint(g, 40, Line(30, 260));
        var b = Paint(g, 40, Line(130, 360)); // otro trazo, desplazado 100 px
        // en la zona que ambos cubren con alfa saturado, el patrón coincide píxel a píxel
        var same = 0;
        var total = 0;
        foreach (var y in Enumerable.Range(H / 2 - 6, 12))
        foreach (var x in Enumerable.Range(160, 80))
        {
            total++;
            if (Math.Abs(A(a, x, y) - A(b, x, y)) <= 1) same++;
        }
        Assert.True(same >= total * 0.99, $"solo {same * 100 / total} % de píxeles coinciden");
    }

    /// <summary>
    /// Un único sello en (150, 80) y otro en (157, 80); se compara la zona común
    /// (a) sin desplazar: coincide si el patrón está anclado al lienzo;
    /// (b) desplazando 7 px: coincide si el patrón viaja con el sello.
    /// Un solo sello evita que la geometría de los solapes entre sellos contamine la medida.
    /// </summary>
    private static (double Unshifted, double Shifted) CompareShifted(GrainSettings grain)
    {
        const int dx = 7;
        var brush = Solid(grain);
        var a = Paint(brush, 60, [new InputPoint(150, H / 2f)]);
        var b = Paint(brush, 60, [new InputPoint(150 + dx, H / 2f)]);
        double unshifted = 0, shifted = 0, n = 0;
        foreach (var y in Enumerable.Range(H / 2 - 10, 20))
        foreach (var x in Enumerable.Range(142, 16))
        {
            n++;
            if (Math.Abs(A(a, x, y) - A(b, x, y)) <= 2) unshifted++;
            if (Math.Abs(A(a, x, y) - A(b, x + dx, y)) <= 2) shifted++;
        }
        return (unshifted / n, shifted / n);
    }

    // Moving con Movement 0: el patrón viaja con el trazo (el segundo trazo es el primero desplazado)
    [Fact]
    public void MovingGrainTravelsWithTheStroke()
    {
        var (unshifted, shifted) = CompareShifted(new GrainSettings { Mode = GrainMode.Moving, Depth = 1, Movement = 0, Scale = 0.25f });
        Assert.True(shifted > 0.95, $"desplazado coincide {shifted:P0}");
        Assert.True(unshifted < 0.6, $"sin desplazar coincide {unshifted:P0}: el grano parece anclado al lienzo");
    }

    // Movement 1 y Texturized: el patrón queda anclado al lienzo
    [Theory]
    [InlineData(GrainMode.Moving)]
    [InlineData(GrainMode.Texturized)]
    public void FullMovementAndTexturizedAnchorGrainToCanvas(GrainMode mode)
    {
        var (unshifted, shifted) = CompareShifted(new GrainSettings { Mode = mode, Depth = 1, Movement = 1, Scale = 0.25f });
        Assert.True(unshifted > 0.95, $"{mode}: sin desplazar coincide {unshifted:P0}");
        Assert.True(shifted < 0.6, $"{mode}: desplazado coincide {shifted:P0}: el grano parece viajar con el trazo");
    }

    // ---------- BR-05: afinado ----------

    private static int Thickness(byte[] px, int x) => Enumerable.Range(0, H).Count(y => A(px, x, y) > 64);

    [Fact]
    public void TaperNarrowsBothEnds()
    {
        var taper = new TaperSettings { Start = 3, End = 3, TipSize = 0.05f };
        var px = Paint(Solid(taper: taper), 30, Line(40, 340));
        var middle = Thickness(px, 190);
        Assert.InRange(middle, 26, 34);
        Assert.True(Thickness(px, 46) < middle / 3, $"inicio {Thickness(px, 46)} px vs centro {middle} px");
        Assert.True(Thickness(px, 334) < middle / 3, $"final {Thickness(px, 334)} px vs centro {middle} px");
    }

    [Fact]
    public void TouchTaperAppliesOnlyWithoutPressure()
    {
        var b = Solid(touch: new TaperSettings { Start = 3, End = 3, TipSize = 0.05f });
        var pen = Paint(b, 30, Line(40, 340), hasPressure: true);
        var finger = Paint(b, 30, Line(40, 340), hasPressure: false);
        Assert.True(Thickness(pen, 50) > 20);
        Assert.True(Thickness(finger, 50) < 10);
    }

    // La cola provisional se ve mientras se dibuja (sin afinar) y se afina al levantar el lápiz
    [Fact]
    public void TailIsVisibleWhileDrawingAndTaperedOnRelease()
    {
        var (doc, _, engine) = BR02_BrushReferenceTests.NewDocument();
        using (doc)
        {
            var input = Line(40, 340);
            engine.Begin(input[0], ToolKind.Brush, Solid(taper: new TaperSettings { End = 4, TipSize = 0.05f }), Ink, 30, 1, SymmetryMode.None, 1, 1);
            foreach (var p in input.Skip(1)) engine.Add(p);

            var owned = new List<IDisposable>();
            byte[] preview;
            using (var rt = new Microsoft.Graphics.Canvas.CanvasRenderTarget(Gpu.Device, W, H, 96))
            {
                using (var ds = rt.CreateDrawingSession())
                {
                    ds.Clear(Color.FromArgb(0, 0, 0, 0));
                    ds.DrawImage(engine.Preview(owned)!);
                }
                preview = rt.GetPixelBytes();
            }
            owned.ForEach(o => o.Dispose());
            Assert.True(Thickness(preview, 330) > 20, "la cola provisional debe verse a tamaño completo antes de levantar");

            engine.End();
            var final = doc.Layers[0].Surface.GetPixelBytes();
            Assert.True(Thickness(final, 336) < 10, "al levantar el lápiz el final debe quedar afinado");
            Assert.Equal(Thickness(preview, 150), Thickness(final, 150));
        }
    }

    // ---------- Referencias visuales ----------

    public static IEnumerable<object[]> Scenarios =>
    [
        ["grain-texturized-paper"], ["grain-texturized-canvas"], ["grain-texturized-charcoal"],
        ["grain-moving-m0"], ["grain-moving-m1"], ["taper-pressure"], ["taper-touch"],
    ];

    [Theory]
    [MemberData(nameof(Scenarios))]
    public async Task ScenarioMatchesReference(string name)
    {
        var tex = new GrainSettings { Mode = GrainMode.Texturized, Depth = 0.85f, Scale = 0.35f };
        var mov = new GrainSettings { Mode = GrainMode.Moving, Depth = 1, Scale = 0.35f, Kind = GrainKind.Charcoal, Rotation = 1 };
        var taper = new TaperSettings { Start = 2, End = 5, TipSize = 0.05f, TipOpacity = 0.6f };
        var (brush, hasPressure) = name switch
        {
            "grain-texturized-paper" => (Solid(tex), true),
            "grain-texturized-canvas" => (Solid(tex with { Kind = GrainKind.Canvas }), true),
            "grain-texturized-charcoal" => (Solid(tex with { Kind = GrainKind.Charcoal }), true),
            "grain-moving-m0" => (Solid(mov with { Movement = 0 }), true),
            "grain-moving-m1" => (Solid(mov with { Movement = 1 }), true),
            "taper-pressure" => (Solid(taper: taper), true),
            _ => (Solid(touch: taper), false),
        };
        // el grano en movimiento solo se aprecia con flujo bajo: con flujo 1 y sellos densos se satura (ver la especificación)
        if (brush.Grain.Mode == GrainMode.Moving) brush.Flow = 0.2f;
        var (doc, _, engine) = BR02_BrushReferenceTests.NewDocument();
        using (doc)
        {
            var input = BR02_BrushReferenceTests.ReferenceInput();
            engine.Begin(input[0], ToolKind.Brush, brush, Ink, 36, 1, SymmetryMode.None, 1, BR02_BrushReferenceTests.Seed, hasPressure);
            foreach (var p in input.Skip(1)) engine.Add(p);
            engine.End();
            using var flat = doc.Flatten(includeBackground: false);
            var diff = await Golden.CheckAsync(flat, "grain-taper", name);
            Assert.True(diff.Within(), $"{name}: {diff}");
        }
    }
}
