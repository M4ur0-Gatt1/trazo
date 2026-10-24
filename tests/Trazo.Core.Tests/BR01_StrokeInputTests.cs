using System.Numerics;
using Trazo.Core.Brushes;
using Trazo.Core.Geometry;
using Trazo.Core.Strokes;

namespace Trazo.Core.Tests;

/// <summary>
/// BR-01 — Estabilidad y precisión de los trazos (docs/protocols/BR-01.md).
/// Cada prueba corresponde a un criterio de aceptación del protocolo.
/// </summary>
public class BR01_StrokeInputTests
{
    private static BrushPreset Brush(float streamLine = 0, float spacing = 0.1f) => new()
    {
        Id = "br01", Name = "BR-01", Tip = TipShape.Round, Size = 10, Spacing = spacing, MinSizePressure = 0.3f,
        StreamLine = streamLine,
    };

    private static readonly Func<float, Vector2>[] Paths =
    [
        SyntheticStrokes.Line(new(50, 50), new(950, 400)),
        SyntheticStrokes.Circle(new(500, 500), 200),
        SyntheticStrokes.Zigzag(new(50, 300), 900, 200, 6),
        SyntheticStrokes.Spiral(new(500, 500), 20, 300, 3),
    ];

    private static float MaxStep(BrushPreset b, float size) => Math.Max(0.35f, b.SizeAt(size, 1) * b.Spacing);

    // Criterio 1: las muestras se procesan en orden temporal
    [Fact]
    public void RejectsSamplesOutOfTemporalOrder()
    {
        var pts = SyntheticStrokes.Sample(Paths[0], 500, 120);
        var shuffled = pts.Take(10).Concat([pts[3] with { Time = pts[3].Time }]).Concat(pts.Skip(10)).ToList();
        var dabs = StrokeBuilder.Build(Brush(), 10, 1, 1, shuffled, out var b);
        Assert.Equal(1, b.RejectedOutOfOrder);
        // en una línea recta los sellos avanzan siempre hacia el final
        for (var i = 1; i < dabs.Count; i++) Assert.True(dabs[i].X >= dabs[i - 1].X - 1e-3f, $"retroceso en el sello {i}");
    }

    [Fact]
    public void IgnoresNonFiniteSamples()
    {
        var pts = SyntheticStrokes.Sample(Paths[0], 300, 120);
        pts.Insert(5, new InputPoint(float.NaN, 3, 1, 0, pts[4].Time));
        var dabs = StrokeBuilder.Build(Brush(), 10, 1, 1, pts, out _);
        Assert.All(dabs, d => Assert.True(float.IsFinite(d.X) && float.IsFinite(d.Y)));
    }

    // Criterio 2: sin saltos ni discontinuidades, con y sin estabilización
    [Theory]
    [InlineData(0f)]
    [InlineData(0.3f)]
    [InlineData(0.7f)]
    [InlineData(1f)]
    public void NoGapsBetweenConsecutiveDabs(float streamLine)
    {
        var brush = Brush(streamLine);
        foreach (var path in Paths)
        {
            var pts = SyntheticStrokes.Sample(path, 800, 240, t => 0.3f + 0.7f * MathF.Sin(t * MathF.PI));
            var dabs = StrokeBuilder.Build(brush, 10, 1, 7, pts, out _);
            Assert.NotEmpty(dabs);
            var limit = MaxStep(brush, 10) * 1.01f;
            for (var i = 1; i < dabs.Count; i++)
            {
                var gap = Vector2.Distance(new(dabs[i].X, dabs[i].Y), new(dabs[i - 1].X, dabs[i - 1].Y));
                Assert.True(gap <= limit, $"salto de {gap:0.00} px > {limit:0.00} px en el sello {i}");
            }
            // empieza en la primera muestra y termina cerca de la última (el StreamLine alcanza el final)
            Assert.Equal(pts[0].X, dabs[0].X, 3);
            var last = new Vector2(pts[^1].X, pts[^1].Y);
            Assert.True(Vector2.Distance(last, new(dabs[^1].X, dabs[^1].Y)) <= limit);
        }
    }

    // Criterio 3: zoom, giro y desplazamiento no desplazan el trazo respecto de la entrada
    [Theory]
    [InlineData(0.25f, 0f, 0f, 0f)]
    [InlineData(1f, 0.7f, 120f, -40f)]
    [InlineData(4f, -1.2f, -900f, 300f)]
    [InlineData(16f, 3.1f, 10f, 10f)]
    public void ViewTransformDoesNotMoveTheStroke(float scale, float rotation, float ox, float oy)
    {
        var pts = SyntheticStrokes.Sample(Paths[1], 600, 120);
        var reference = StrokeBuilder.Build(Brush(), 10, 1, 3, pts, out _);
        var view = new ViewTransform { Scale = scale, Rotation = rotation, Offset = new(ox, oy) };
        var recording = InputRecording.FromDocument(pts, view);
        var replayed = StrokeBuilder.Build(Brush(), 10, scale, 3, recording.ToDocument(), out _);
        Assert.Equal(reference.Count, replayed.Count);
        for (var i = 0; i < reference.Count; i++)
        {
            Assert.Equal(reference[i].X, replayed[i].X, 0.01f);
            Assert.Equal(reference[i].Y, replayed[i].Y, 0.01f);
        }
    }

    [Fact]
    public void StreamLineRadiusIsConstantOnScreen()
    {
        var b = Brush(0.5f);
        Assert.Equal(13, new StrokeBuilder(b, 10, 1, 1).SmoothingRadius, 3);
        Assert.Equal(6.5f, new StrokeBuilder(b, 10, 2, 1).SmoothingRadius, 3);
    }

    // Criterio 4: la frecuencia de muestreo no cambia la geometría (más allá del error de cuerda)
    [Fact]
    public void SamplingRateDoesNotChangeGeometry()
    {
        foreach (var hz in new[] { 60f, 120f, 240f, 500f })
        {
            var line = StrokeBuilder.Build(Brush(), 10, 1, 1, SyntheticStrokes.Sample(Paths[0], 1000, hz), out _);
            var lineRef = StrokeBuilder.Build(Brush(), 10, 1, 1, SyntheticStrokes.Sample(Paths[0], 1000, 1000), out _);
            Assert.Equal(lineRef.Count, line.Count);
            for (var i = 0; i < line.Count; i++) Assert.Equal(lineRef[i].X, line[i].X, 0.05f);

            // en el círculo el error máximo es la sagita de la cuerda entre muestras
            var circle = StrokeBuilder.Build(Brush(), 10, 1, 1, SyntheticStrokes.Sample(Paths[1], 1000, hz), out _);
            var n = (int)MathF.Round(hz);
            var sagitta = 200 * (1 - MathF.Cos(MathF.PI / n));
            foreach (var d in circle)
            {
                var r = Vector2.Distance(new(d.X, d.Y), new(500, 500));
                Assert.True(r <= 200.001f && r >= 200 - sagitta - 0.01f, $"{hz} Hz: radio {r:0.000}");
            }
        }
    }

    // Criterio 5: la estabilización modifica el resultado de forma controlada y reproducible
    [Fact]
    public void StabilizationReducesTremorMonotonically()
    {
        var noisy = SyntheticStrokes.WithTremor(SyntheticStrokes.Sample(Paths[0], 1500, 240), 2.5f, 42);
        double previous = double.MaxValue;
        foreach (var s in new[] { 0f, 0.25f, 0.5f, 0.75f, 1f })
        {
            var dabs = StrokeBuilder.Build(Brush(s, 0.05f), 10, 1, 1, noisy, out _);
            var deviation = dabs.Average(d => QuickShapeDistance(new(d.X, d.Y), new(50, 50), new(950, 400)));
            Assert.True(deviation <= previous + 1e-6, $"StreamLine {s}: desviación {deviation:0.000} > {previous:0.000}");
            previous = deviation;
        }
    }

    [Fact]
    public void SameSeedGivesIdenticalDabs()
    {
        var b = Brush(0.4f);
        b.Jitter = 0.4f;
        b.Scatter = 0.5f;
        b.RotationRandom = MathF.PI;
        var pts = SyntheticStrokes.Sample(Paths[3], 900, 240);
        var a1 = StrokeBuilder.Build(b, 20, 1, 99, pts, out _);
        var a2 = StrokeBuilder.Build(b, 20, 1, 99, pts, out _);
        var other = StrokeBuilder.Build(b, 20, 1, 100, pts, out _);
        Assert.Equal(a1, a2);
        Assert.NotEqual(a1, other);
    }

    // Respuesta a la presión: continua, sin saltos entre muestras
    [Fact]
    public void PressureIsInterpolatedContinuously()
    {
        var pts = SyntheticStrokes.Sample(Paths[0], 1000, 60, t => t);
        var dabs = StrokeBuilder.Build(Brush(), 40, 1, 1, pts, out _);
        for (var i = 1; i < dabs.Count; i++)
        {
            Assert.True(dabs[i].Size >= dabs[i - 1].Size - 1e-3f, "el tamaño debe crecer con la presión");
            Assert.True(dabs[i].Size - dabs[i - 1].Size < 0.5f, $"salto de tamaño {dabs[i].Size - dabs[i - 1].Size:0.000}");
        }
    }

    [Fact]
    public void RecordingRoundTripsThroughJson()
    {
        var view = new ViewTransform { Scale = 1.5f, Rotation = 0.3f, Offset = new(10, 20) };
        var rec = InputRecording.FromDocument(SyntheticStrokes.Sample(Paths[2], 400, 120, t => t), view, "prueba");
        var back = InputRecording.FromJson(rec.ToJson());
        Assert.Equal(rec.Samples, back.Samples);
        Assert.Equal(rec.ViewScale, back.ViewScale);
    }

    private static float QuickShapeDistance(Vector2 p, Vector2 a, Vector2 b) => QuickShape.DistanceToLine(p, a, b);
}
