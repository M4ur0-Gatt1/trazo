using System.Numerics;
using System.Text.Json;
using Trazo.Core.Brushes;
using Trazo.Core.Strokes;

namespace Trazo.Core.Tests;

/// <summary>BR-06 — Curvas de dinámica y opacidad (docs/specs/BR-06-dynamics.md).</summary>
public class BR06_DynamicsTests
{
    [Fact]
    public void EmptyCurveIsIdentity()
    {
        for (var x = 0f; x <= 1; x += 0.05f) Assert.Equal(x, DynamicsCurve.Identity.Evaluate(x), 6);
    }

    [Fact]
    public void CurvePassesThroughControlPoints()
    {
        var c = DynamicsCurve.From(new(0, 0.1f), new(0.3f, 0.6f), new(0.7f, 0.7f), new(1, 1));
        foreach (var p in c.Points) Assert.Equal(p.Y, c.Evaluate(p.X), 5);
    }

    [Fact]
    public void CurveIsMonotoneWithoutOvershoot()
    {
        // datos monótonos con un salto brusco: una spline cúbica normal oscilaría
        var c = DynamicsCurve.From(new(0, 0), new(0.4f, 0.05f), new(0.5f, 0.95f), new(1, 1));
        var prev = -1f;
        for (var i = 0; i <= 1000; i++)
        {
            var x = i / 1000f;
            var y = c.Evaluate(x);
            Assert.True(y >= prev - 1e-6f, $"no monótona en {x}");
            prev = y;
            // dentro de cada tramo no se sale del rango de sus extremos
            var k = c.Points.Count - 2;
            while (k > 0 && x < c.Points[k].X) k--;
            var lo = Math.Min(c.Points[k].Y, c.Points[k + 1].Y);
            var hi = Math.Max(c.Points[k].Y, c.Points[k + 1].Y);
            Assert.InRange(y, lo - 1e-5f, hi + 1e-5f);
        }
    }

    [Fact]
    public void CurveClampsAndValidates()
    {
        var c = DynamicsCurve.From(new(0.2f, 0.3f), new(0.8f, 0.9f));
        Assert.Equal(0.3f, c.Evaluate(-5), 5);
        Assert.Equal(0.3f, c.Evaluate(0.1f), 5);
        Assert.Equal(0.9f, c.Evaluate(2), 5);
        Assert.Throws<ArgumentException>(() => DynamicsCurve.From(new(0.5f, 0), new(0.5f, 1)));       // x repetida
        Assert.Throws<ArgumentException>(() => DynamicsCurve.From(new(0.6f, 0), new(0.2f, 1)));       // desordenada
        Assert.Throws<ArgumentException>(() => DynamicsCurve.From(new(0, 0), new(1, 1.5f)));          // fuera de rango
        Assert.Throws<ArgumentException>(() => DynamicsCurve.From(new CurvePoint(0.5f, 0.5f)));       // un solo punto
        // una curva inválida que llegara de un archivo se trata como identidad
        var bad = new DynamicsCurve { Points = [new(0.6f, 0), new(0.2f, 1)] };
        Assert.Equal(0.4f, bad.Evaluate(0.4f), 5);
        // «with» no conserva la caché de la curva anterior
        var changed = c with { Points = [new(0, 1), new(1, 0)] };
        Assert.Equal(0.75f, changed.Evaluate(0.25f), 2);
    }

    [Fact]
    public void IdentityCurvesKeepPreviousDynamics()
    {
        var b = new BrushPreset { Id = "t", Name = "t", Tip = TipShape.Round, MinSizePressure = 0.3f, Flow = 0.7f, FlowPressure = 0.6f };
        for (var p = 0f; p <= 1; p += 0.1f)
        {
            Assert.Equal(40 * (0.3f + 0.7f * p), b.SizeAt(40, p), 5);
            Assert.Equal(0.7f * (1 - 0.6f + 0.6f * p), b.FlowAt(p), 5);
            Assert.Equal(1, b.OpacityAt(p));
        }
        Assert.False(b.HasOpacityCap);
    }

    [Fact]
    public void SizeCurveShapesSize()
    {
        var b = new BrushPreset { Id = "t", Name = "t", Tip = TipShape.Round, MinSizePressure = 0.2f, SizeCurve = DynamicsCurve.From(new(0, 0), new(0.5f, 0.9f), new(1, 1)) };
        Assert.Equal(50 * (0.2f + 0.8f * 0.9f), b.SizeAt(50, 0.5f), 4);
        var dabs = StrokeBuilder.Build(b, 50, 1, 1, [new InputPoint(0, 0, 0.5f), new InputPoint(200, 0, 0.5f)], out _);
        Assert.All(dabs, d => Assert.Equal(b.SizeAt(50, 0.5f), d.Size, 3));
    }

    [Fact]
    public void OpacityPressureSetsDabCap()
    {
        var b = new BrushPreset { Id = "t", Name = "t", Tip = TipShape.Round, Flow = 0.8f, OpacityPressure = 1, OpacityCurve = DynamicsCurve.From(new(0, 0), new(1, 1)) };
        var dabs = StrokeBuilder.Build(b, 20, 1, 1, [new InputPoint(0, 0, 0.3f), new InputPoint(100, 0, 0.3f)], out _);
        Assert.All(dabs, d =>
        {
            Assert.Equal(0.3f, d.Opacity, 4);
            Assert.Equal(0.8f, d.Alpha, 4);   // el flujo no cambia
        });
        Assert.True(b.HasOpacityCap);
    }

    [Fact]
    public void OpacityCapTakesMaximumNotSum()
    {
        var tip = TipGenerator.Generate(TipShape.Round);
        var cap = new OpacityCap(120, 80);
        var d = new Dab(60, 40, 30, 1, 0, Opacity: 0.3f);
        for (var i = 0; i < 20; i++) cap.Stamp(d, tip, TipGenerator.DefaultSize);   // 20 sellos en el mismo sitio
        Assert.InRange(cap.Data[40 * 120 + 60], 75, 77);                             // 0,3 × 255, no 20 × 0,3
        cap.Stamp(d with { Opacity = 0.6f }, tip, TipGenerator.DefaultSize);
        Assert.InRange(cap.Data[40 * 120 + 60], 152, 154);                           // uno más fuerte sí sube el tope
        cap.Stamp(d with { Opacity = 0.1f }, tip, TipGenerator.DefaultSize);
        Assert.InRange(cap.Data[40 * 120 + 60], 152, 154);                           // y uno más débil no lo baja
    }

    [Fact]
    public void OpacityCapIsLocalAndReportsItsRectangle()
    {
        var tip = TipGenerator.Generate(TipShape.Round);
        var cap = new OpacityCap(200, 100);
        var r = cap.Stamp(new Dab(50, 50, 40, 1, 0.7f, Opacity: 1), tip, TipGenerator.DefaultSize);
        for (var y = 0; y < 100; y++)
            for (var x = 0; x < 200; x++)
            {
                if (cap.Data[y * 200 + x] == 0) continue;
                Assert.True(x >= r.X && x < r.Right && y >= r.Y && y < r.Bottom, $"({x},{y}) fuera del rectángulo");
                Assert.True(MathF.Sqrt((x + 0.5f - 50) * (x + 0.5f - 50) + (y + 0.5f - 50) * (y + 0.5f - 50)) <= 21, "fuera del diámetro");
            }
        // la punta redonda no depende del giro
        var other = new OpacityCap(200, 100);
        other.Stamp(new Dab(50, 50, 40, 1, 0, Opacity: 1), tip, TipGenerator.DefaultSize);
        // en el borde el muestreo puntual de una máscara de 256 px reducida a 40 px depende de dónde caen las
        // muestras (igual que en la GPU); por dentro debe coincidir y el área total, casi exacta
        var interior = Enumerable.Range(0, cap.Data.Length)
            .Where(i => MathF.Sqrt(MathF.Pow(i % 200 + 0.5f - 50, 2) + MathF.Pow(i / 200 + 0.5f - 50, 2)) < 17);
        Assert.True(interior.All(i => Math.Abs(cap.Data[i] - other.Data[i]) <= 2), "el interior depende del giro");
        double Sum(OpacityCap c) => c.Data.Sum(v => (double)v);
        Assert.InRange(Sum(cap) / Sum(other), 0.98, 1.02);
    }

    [Fact]
    public void CurveRoundTripsThroughJson()
    {
        var b = new BrushPreset { Id = "t", Name = "t", Tip = TipShape.Round };
        var c = DynamicsCurve.From(new(0, 0.1f), new(0.4f, 0.8f), new(1, 1));
        var json = JsonSerializer.Serialize(c);
        var back = JsonSerializer.Deserialize<DynamicsCurve>(json)!;
        Assert.Equal(c, back);
        Assert.Equal(c.Evaluate(0.25f), back.Evaluate(0.25f));
        var wet = new WetMixSettings { Pull = 0.4f, Charge = 0.7f, Dilution = 0.2f, Attack = 0.6f };
        Assert.Equal(wet, JsonSerializer.Deserialize<WetMixSettings>(JsonSerializer.Serialize(wet)));
        _ = b;
    }
}

/// <summary>BR-04 — Mezcla húmeda (docs/specs/BR-04-wet-mix.md).</summary>
public class BR04_WetMixTests
{
    private static readonly Vector3 Red = new(1, 0, 0);
    private static readonly Vector3 Blue = new(0, 0, 1);

    private static List<Dab> Line(int n, float step = 5) =>
        Enumerable.Range(0, n).Select(i => new Dab(i * step, 0, 20, 0.8f, 0, i * step)).ToList();

    [Fact]
    public void DefaultsAreInactive()
    {
        Assert.False(WetMixSettings.Off.IsActive);
        Assert.False(new BrushPreset { Id = "t", Name = "t", Tip = TipShape.Round }.Wet.IsActive);
        // los pinceles sin mezcla húmeda producen los mismos sellos de siempre (sin color propio)
        var b = new BrushLibrary()["roundbrush"];
        var dabs = StrokeBuilder.Build(b, 30, 1, 1, [new InputPoint(0, 0), new InputPoint(300, 0)], out _);
        Assert.All(dabs, d => Assert.Null(d.Color));
    }

    [Fact]
    public void NoPullKeepsBrushColor()
    {
        var m = new WetMixer(new WetMixSettings { Pull = 0, Charge = 0.9f }, Red, 20, _ => new CanvasSample(Blue, 1));
        Assert.All(Line(50).Select(m.Process), d => Assert.Equal(Red, d.Color));
    }

    [Fact]
    public void PullConvergesGeometrically()
    {
        var full = new WetMixer(new WetMixSettings { Pull = 1 }, Red, 20, _ => new CanvasSample(Blue, 1));
        Assert.Equal(Blue, full.Process(Line(1)[0]).Color);
        var half = new WetMixer(new WetMixSettings { Pull = 0.5f }, Red, 20, _ => new CanvasSample(Blue, 1));
        var dabs = Line(8).Select(half.Process).ToList();
        for (var i = 0; i < dabs.Count; i++)
        {
            var dist = Vector3.Distance(dabs[i].Color!.Value, Blue);
            Assert.Equal(Vector3.Distance(Red, Blue) * MathF.Pow(0.5f, i + 1), dist, 5);
        }
        // un lienzo semitransparente arrastra en proporción a su alfa
        var faint = new WetMixer(new WetMixSettings { Pull = 1 }, Red, 20, _ => new CanvasSample(Blue, 0.25f));
        Assert.Equal(Vector3.Lerp(Red, Blue, 0.25f), faint.Process(Line(1)[0]).Color!.Value);
    }

    [Fact]
    public void TransparentCanvasDoesNotTint()
    {
        var m = new WetMixer(new WetMixSettings { Pull = 1, Charge = 0.8f }, Red, 20, _ => default);
        Assert.All(Line(40).Select(m.Process), d => Assert.Equal(Red, d.Color));
    }

    [Fact]
    public void ChargeDecaysExponentially()
    {
        var s = new WetMixSettings { Charge = 0.6f };
        var l = s.DryLength(20);
        Assert.Equal(12 * 20 * 0.6f / 0.4f, l, 3);
        var m = new WetMixer(s, Red, 20, _ => default);
        foreach (var d in Line(200, 2))
        {
            var o = m.Process(d);
            var expected = 0.8f * MathF.Exp(-d.Distance / l);
            Assert.True(MathF.Abs(o.Alpha - expected) <= expected * 0.01f + 1e-6f, $"a {d.Distance} px: {o.Alpha} ≠ {expected}");
        }
        var full = new WetMixer(new WetMixSettings { Charge = 1, Pull = 0.5f }, Red, 20, _ => default);
        Assert.All(Line(100, 10).Select(full.Process), d => Assert.Equal(0.8f, d.Alpha, 5));
    }

    [Fact]
    public void DilutionAndAttackScaleAlpha()
    {
        foreach (var (dil, att) in new[] { (0f, 1f), (1f, 1f), (0.5f, 0.5f), (0.2f, 0f) })
        {
            var s = new WetMixSettings { Dilution = dil, Attack = att };
            var m = new WetMixer(s, Red, 20, _ => default);
            var d = m.Process(new Dab(0, 0, 20, 0.5f, 0));
            Assert.Equal(0.5f * (1 - 0.8f * dil) * (0.2f + 0.8f * att), d.Alpha, 5);
        }
    }

    [Fact]
    public void PeekDoesNotAdvance()
    {
        var m = new WetMixer(new WetMixSettings { Pull = 0.5f }, Red, 20, _ => new CanvasSample(Blue, 1));
        var dab = Line(1)[0];
        for (var i = 0; i < 10; i++) Assert.Equal(Red, m.Peek(dab).Color);
        Assert.Equal(Red, m.Reservoir);
        m.Process(dab);
        Assert.NotEqual(Red, m.Reservoir);
    }
}
