using System.Numerics;
using Trazo.Core.Brushes;
using Trazo.Core.Strokes;

namespace Trazo.Core.Tests;

/// <summary>BR-03 (grano) — parte independiente de la GPU (docs/specs/BR-03-grain.md).</summary>
public class BR03_GrainTests
{
    public static IEnumerable<object[]> Kinds => Enum.GetValues<GrainKind>().Select(k => new object[] { k });

    [Theory]
    [MemberData(nameof(Kinds))]
    public void GrainIsDeterministicAndUsesFullRange(GrainKind kind)
    {
        var a = GrainGenerator.Generate(kind);
        Assert.Equal(a, GrainGenerator.Generate(kind));
        Assert.Equal(GrainGenerator.Size * GrainGenerator.Size, a.Length);
        Assert.True(a.Max() - a.Min() > 150, $"{kind}: rango {a.Min()}–{a.Max()}");
    }

    // BR-03.2: el mosaico se repite sin costuras (la columna 255 enlaza con la 0 igual que dos columnas vecinas)
    [Theory]
    [MemberData(nameof(Kinds))]
    public void GrainTilesSeamlessly(GrainKind kind)
    {
        if (kind == GrainKind.Noise) return; // ruido blanco: sin correlación entre vecinos por definición
        var g = GrainGenerator.Generate(kind);
        const int n = GrainGenerator.Size;
        double Edge(int x0, int x1, bool vertical)
        {
            double sum = 0;
            for (var i = 0; i < n; i++)
                sum += vertical ? Math.Abs(g[i * n + x0] - g[i * n + x1]) : Math.Abs(g[x0 * n + i] - g[x1 * n + i]);
            return sum / n;
        }
        // la costura (255 → 0) no debe ser más brusca que el paso más brusco entre columnas/filas vecinas del interior
        foreach (var vertical in new[] { true, false })
        {
            var worstInterior = Enumerable.Range(0, n - 1).Max(x => Edge(x, x + 1, vertical));
            var seam = Edge(n - 1, 0, vertical);
            Assert.True(seam <= worstInterior * 1.05 + 1, $"{kind}: costura {(vertical ? "vertical" : "horizontal")} {seam:0.0} > máximo interior {worstInterior:0.0}");
        }
    }

    [Fact]
    public void BrightnessAndContrastAreMonotonic()
    {
        double Mean(byte[] b) => b.Average(x => (double)x);
        double Std(byte[] b) { var m = Mean(b); return Math.Sqrt(b.Average(x => (x - m) * (x - m))); }
        Assert.True(Mean(GrainGenerator.Generate(GrainKind.Paper, brightness: 0.3f)) > Mean(GrainGenerator.Generate(GrainKind.Paper)));
        Assert.True(Std(GrainGenerator.Generate(GrainKind.Paper, contrast: 0.5f)) < Std(GrainGenerator.Generate(GrainKind.Paper)));
    }

    [Theory]
    [InlineData(0f, 0f, 1f)]
    [InlineData(1f, 0f, 0f)]
    [InlineData(1f, 1f, 1f)]
    [InlineData(0.5f, 0f, 0.5f)]
    public void DepthBlendsGrainIntoAlpha(float depth, float grain, float expected) =>
        Assert.Equal(expected, new GrainSettings { Mode = GrainMode.Texturized, Depth = depth }.AlphaFactor(grain), 4);

    [Fact]
    public void PeriodFollowsSizeOnlyWhenMoving()
    {
        var moving = new GrainSettings { Mode = GrainMode.Moving, FollowSize = true, Scale = 1 };
        var textured = moving with { Mode = GrainMode.Texturized };
        Assert.Equal(2 * moving.Period(20), moving.Period(40), 3);
        Assert.Equal(textured.Period(20), textured.Period(40), 3);
        Assert.Equal(GrainGenerator.Size, textured.Period(20), 3);
    }
}

/// <summary>BR-05 (afinado) — docs/specs/BR-05-taper.md.</summary>
public class BR05_TaperTests
{
    private const float Diameter = 10;

    private static BrushPreset Brush(TaperSettings pressure, TaperSettings? touch = null) => new()
    {
        Id = "taper", Name = "taper", Tip = TipShape.Round, Size = Diameter, Spacing = 0.1f, MinSizePressure = 1,
        StreamLine = 0, PressureTaper = pressure, TouchTaper = touch ?? TaperSettings.None,
    };

    private static List<InputPoint> Line(float length, float hz = 240) =>
        SyntheticStrokes.Sample(SyntheticStrokes.Line(new(0, 0), new(length, 0)), length * 2, hz);

    private static readonly TaperSettings Both = new() { Start = 5, End = 8, TipSize = 0.1f, TipOpacity = 0.2f, Shape = 0.5f };

    // BR-05.1: rampa de tamaño al inicio y al final, de la punta al tamaño completo
    [Fact]
    public void SizeRampsUpAtStartAndDownAtEnd()
    {
        var dabs = StrokeBuilder.Build(Brush(Both), Diameter, 1, 1, Line(400), out _);
        Assert.Equal(Diameter * 0.1f, dabs[0].Size, 2);
        Assert.Equal(Diameter * 0.1f, dabs[^1].Size, 1);
        var mid = dabs.Where(d => d.Distance > 60 && d.Distance < 300).ToList();
        Assert.All(mid, d => Assert.Equal(Diameter, d.Size, 3));
        // monótono en cada rampa
        var start = dabs.Where(d => d.Distance <= Both.Start * Diameter).ToList();
        for (var i = 1; i < start.Count; i++) Assert.True(start[i].Size >= start[i - 1].Size - 1e-4f);
        var end = dabs.Where(d => d.Distance >= dabs[^1].Distance - Both.End * Diameter).ToList();
        for (var i = 1; i < end.Count; i++) Assert.True(end[i].Size <= end[i - 1].Size + 1e-4f);
    }

    // BR-05.2: longitudes en diámetros: el afinado mide Start×D y End×D
    [Fact]
    public void TaperLengthIsMeasuredInDiameters()
    {
        var dabs = StrokeBuilder.Build(Brush(Both), Diameter, 1, 1, Line(400), out _);
        var firstFull = dabs.First(d => d.Size >= Diameter - 1e-3f).Distance;
        var total = dabs[^1].Distance;
        var lastFull = dabs.Last(d => d.Size >= Diameter - 1e-3f).Distance;
        Assert.InRange(firstFull, Both.Start * Diameter - 1.5f, Both.Start * Diameter + 1.5f);
        Assert.InRange(total - lastFull, Both.End * Diameter - 1.5f, Both.End * Diameter + 1.5f);
    }

    [Fact]
    public void OpacityTapersToTipOpacity()
    {
        var dabs = StrokeBuilder.Build(Brush(Both), Diameter, 1, 1, Line(400), out _);
        Assert.Equal(0.2f, dabs[0].Alpha, 2);
        Assert.Equal(1f, dabs[dabs.Count / 2].Alpha, 3);
    }

    // BR-05.3: trazo más corto que los dos afinados: manda el extremo más cercano, sin superar el tamaño completo
    [Fact]
    public void ShortStrokeUsesNearestEnd()
    {
        var dabs = StrokeBuilder.Build(Brush(Both), Diameter, 1, 1, Line(60), out _);
        var total = dabs[^1].Distance;
        var taper = Both;
        foreach (var d in dabs)
        {
            var f = taper.Factor(d.Distance, total - d.Distance, Diameter);
            Assert.Equal(Diameter * taper.SizeMultiplier(f), d.Size, 3);
            Assert.True(d.Size < Diameter);
        }
    }

    // BR-05.4: dedo/ratón usa el afinado táctil; lápiz, el de presión
    [Fact]
    public void TouchAndPressureUseTheirOwnTaper()
    {
        var touch = new TaperSettings { Start = 3, End = 3, TipSize = 0 };
        var b = Brush(TaperSettings.None, touch);
        var pen = StrokeBuilder.Build(b, Diameter, 1, 1, Line(200), out _, hasPressure: true);
        var finger = StrokeBuilder.Build(b, Diameter, 1, 1, Line(200), out _, hasPressure: false);
        Assert.Equal(Diameter, pen[0].Size, 3);
        Assert.True(finger[0].Size < 1);
    }

    // BR-05.5: la cola provisional no se emite hasta levantar el lápiz y se emite toda, afinada
    [Fact]
    public void TailIsHeldUntilEnd()
    {
        var b = new StrokeBuilder(Brush(Both), Diameter, 1, 1);
        var output = new List<Dab>();
        foreach (var p in Line(400)) b.Add(p, output);
        Assert.NotEmpty(b.PendingTail);
        var tailLength = b.PendingTail[^1].Distance - b.PendingTail[0].Distance;
        Assert.InRange(tailLength, Both.End * Diameter - 1.5f, Both.End * Diameter + 1.5f);
        Assert.All(output, d => Assert.True(d.Distance < b.PendingTail[0].Distance));
        var before = output.Count;
        b.End(output);
        Assert.Empty(b.PendingTail);
        Assert.True(output.Count > before);
        for (var i = 1; i < output.Count; i++) Assert.True(output[i].Distance >= output[i - 1].Distance, "orden de sellos");
    }

    [Fact]
    public void NoTaperLeavesStrokeUnchanged()
    {
        var plain = StrokeBuilder.Build(Brush(TaperSettings.None), Diameter, 1, 1, Line(300), out var b);
        Assert.Empty(b.PendingTail);
        Assert.All(plain, d => Assert.Equal(Diameter, d.Size, 3));
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(0.5f)]
    [InlineData(1f)]
    public void ShapeControlsProfileCurvature(float shape)
    {
        var t = Both with { Shape = shape };
        var half = t.Factor(t.Start * Diameter / 2, float.PositiveInfinity, Diameter);
        if (shape == 0) Assert.True(half < 0.5f);       // cóncavo: punta larga
        else if (shape == 1) Assert.True(half > 0.5f);  // convexo: punta roma
        else Assert.Equal(0.5f, half, 3);               // lineal
    }

    [Fact]
    public void QuickShapeGetsBothTapers()
    {
        var b = new StrokeBuilder(Brush(Both), Diameter, 1, 1);
        var sink = new List<Dab>();
        foreach (var p in Line(400).Select(p => p with { Y = MathF.Sin(p.X) * 2 })) b.Add(p, sink);
        Assert.True(b.TrySnapShape(1));
        var dabs = new List<Dab>();
        b.ShapeDabs(dabs);
        Assert.Equal(Diameter * 0.1f, dabs[0].Size, 2);
        Assert.Equal(Diameter * 0.1f, dabs[^1].Size, 1);
    }

    [Fact]
    public void DistanceIsMonotonicAlongStroke()
    {
        var dabs = StrokeBuilder.Build(Brush(Both), Diameter, 1, 1,
            SyntheticStrokes.Sample(SyntheticStrokes.Spiral(new Vector2(300, 300), 20, 200, 2), 800, 240), out _);
        for (var i = 1; i < dabs.Count; i++) Assert.True(dabs[i].Distance > dabs[i - 1].Distance);
    }
}
