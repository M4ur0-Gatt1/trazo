using System.Numerics;
using System.Text;
using Trazo.Core.Brushes;
using Trazo.Core.Colors;
using Trazo.Core.Documents;
using Trazo.Core.History;
using Trazo.Core.Imaging;
using Trazo.Core.IO;
using Trazo.Core.Strokes;

namespace Trazo.Core.Tests;

public class ColorTests
{
    [Theory]
    [InlineData("#ff0000", 255, 0, 0)]
    [InlineData("0f0", 0, 255, 0)]
    [InlineData("#1E90FF", 30, 144, 255)]
    public void ParsesHex(string hex, byte r, byte g, byte b)
    {
        Assert.True(Rgba.TryParseHex(hex, out var c));
        Assert.Equal(new Rgba(r, g, b), c);
    }

    [Fact]
    public void RejectsInvalidHex() => Assert.False(Rgba.TryParseHex("#12", out _));

    [Fact]
    public void HsvRoundTrips()
    {
        for (var r = 0; r < 256; r += 51)
        for (var g = 0; g < 256; g += 51)
        for (var b = 0; b < 256; b += 51)
        {
            var c = new Rgba((byte)r, (byte)g, (byte)b);
            Assert.Equal(c, c.ToHsv().ToRgba());
        }
    }
}

public class BrushTests
{
    [Fact]
    public void LibraryHasUniqueIdsAndAllCategories()
    {
        var lib = new BrushLibrary();
        Assert.Equal(6, lib.Categories.Count);
        Assert.Equal(lib.All.Count(), lib.All.Select(b => b.Id).Distinct().Count());
        Assert.All(lib.All, b => Assert.False(string.IsNullOrEmpty(b.Category)));
    }

    [Theory]
    [InlineData(TipShape.Round)]
    [InlineData(TipShape.Soft)]
    [InlineData(TipShape.Airbrush)]
    [InlineData(TipShape.Pencil)]
    [InlineData(TipShape.Chalk)]
    [InlineData(TipShape.Flat)]
    [InlineData(TipShape.Spray)]
    [InlineData(TipShape.Bristle)]
    public void TipsAreDeterministicAndNonEmpty(TipShape shape)
    {
        var a = TipGenerator.Generate(shape, 64);
        var b = TipGenerator.Generate(shape, 64);
        Assert.Equal(a, b);
        Assert.Contains(a, v => v > 0);
        Assert.Equal(0, a[0]); // esquina siempre transparente
    }

    [Fact]
    public void RoundTipIsOpaqueAtCenter() => Assert.Equal(255, TipGenerator.Generate(TipShape.Round, 64)[32 * 64 + 32]);

    [Fact]
    public void TintPremultiplies()
    {
        var bgra = TipGenerator.Tint([0, 128, 255], 255, 0, 0);
        Assert.Equal([0, 0, 0, 0, 0, 0, 128, 128, 0, 0, 255, 255], bgra);
    }
}

public class StrokeTests
{
    private static BrushPreset Brush(float spacing = 0.1f) => new()
    {
        Id = "t", Name = "t", Tip = TipShape.Round, Spacing = spacing, MinSizePressure = 1, Size = 10,
    };

    [Fact]
    public void StampsAreEvenlySpaced()
    {
        var s = new StrokeStamper(Brush(0.1f), 10, rng: new Random(1));
        var dabs = new List<Dab>();
        s.LineTo(new InputPoint(0, 0), dabs);
        s.LineTo(new InputPoint(100, 0), dabs);
        Assert.Equal(101, dabs.Count); // un sello por píxel (10 px × 0.1)
        for (var i = 1; i < dabs.Count; i++) Assert.Equal(1, dabs[i].X - dabs[i - 1].X, 3);
    }

    [Fact]
    public void SpacingCarriesAcrossSegments()
    {
        var s = new StrokeStamper(Brush(0.5f), 10, rng: new Random(1));
        var dabs = new List<Dab>();
        s.LineTo(new InputPoint(0, 0), dabs);
        for (var x = 1; x <= 20; x++) s.LineTo(new InputPoint(x, 0), dabs);
        Assert.Equal([0f, 5, 10, 15, 20], dabs.Select(d => MathF.Round(d.X, 3)));
    }

    [Fact]
    public void PressureScalesSize()
    {
        var b = Brush();
        b.MinSizePressure = 0.2f;
        Assert.Equal(2, b.SizeAt(10, 0), 3);
        Assert.Equal(10, b.SizeAt(10, 1), 3);
    }

    [Fact]
    public void SmootherWaitsUntilRadiusIsExceeded()
    {
        var s = new StreamLineSmoother(10);
        Assert.NotNull(s.Push(new InputPoint(0, 0)));
        Assert.Null(s.Push(new InputPoint(5, 0)));
        var p = s.Push(new InputPoint(25, 0));
        Assert.NotNull(p);
        Assert.Equal(15, p!.Value.X, 3);
    }

    [Fact]
    public void QuarterSymmetryMakesFourDabs()
    {
        var list = new List<Dab>();
        Symmetry.Expand(new Dab(10, 20, 5, 1, 0), SymmetryMode.Quadrant, 50, 50, list);
        Assert.Equal(4, list.Count);
        Assert.Contains(list, d => d.X == 90 && d.Y == 80);
    }

    [Fact]
    public void RadialSymmetryMakesEightDabs()
    {
        var list = new List<Dab>();
        Symmetry.Expand(new Dab(60, 50, 5, 1, 0), SymmetryMode.Radial, 50, 50, list);
        Assert.Equal(Symmetry.RadialSegments, list.Count);
    }
}

public class QuickShapeTests
{
    [Fact]
    public void DetectsWobblyLine()
    {
        var pts = Enumerable.Range(0, 50).Select(i => new InputPoint(i * 10, 100 + MathF.Sin(i) * 3)).ToList();
        var s = QuickShape.Detect(pts);
        Assert.Equal(ShapeKind.Line, s!.Kind);
        s.Adjust(new Vector2(600, 300));
        Assert.Equal(new Vector2(600, 300), s.Path()[^1]);
    }

    [Fact]
    public void DetectsCircle()
    {
        var pts = Enumerable.Range(0, 60).Select(i =>
        {
            var t = i / 60f * MathF.Tau * 0.97f;
            return new InputPoint(200 + 100 * MathF.Cos(t), 200 + 100 * MathF.Sin(t));
        }).ToList();
        Assert.Equal(ShapeKind.Circle, QuickShape.Detect(pts)!.Kind);
    }

    [Fact]
    public void DetectsEllipse()
    {
        var pts = Enumerable.Range(0, 60).Select(i =>
        {
            var t = i / 60f * MathF.Tau * 0.97f;
            return new InputPoint(200 + 150 * MathF.Cos(t), 200 + 60 * MathF.Sin(t));
        }).ToList();
        Assert.Equal(ShapeKind.Ellipse, QuickShape.Detect(pts)!.Kind);
    }

    [Fact]
    public void DetectsPolyline()
    {
        var pts = new List<InputPoint>();
        for (var i = 0; i <= 20; i++) pts.Add(new InputPoint(i * 10, 0));
        for (var i = 1; i <= 20; i++) pts.Add(new InputPoint(200, i * 10));
        var s = QuickShape.Detect(pts)!;
        Assert.Equal(ShapeKind.Polyline, s.Kind);
        Assert.Equal(3, s.Points.Count);
    }

    [Fact]
    public void IgnoresTinyStrokes() =>
        Assert.Null(QuickShape.Detect([new(0, 0), new(1, 1), new(2, 1), new(3, 2)]));
}

public class FloodFillTests
{
    // Imagen 10×10 blanca con un marco negro de 1 px en x=2..7, y=2..7
    private static byte[] Framed()
    {
        var px = new byte[10 * 10 * 4];
        for (var y = 0; y < 10; y++)
        for (var x = 0; x < 10; x++)
        {
            var edge = (x is 2 or 7 && y is >= 2 and <= 7) || (y is 2 or 7 && x is >= 2 and <= 7);
            var v = edge ? (byte)0 : (byte)255;
            var i = (y * 10 + x) * 4;
            px[i] = px[i + 1] = px[i + 2] = v;
            px[i + 3] = 255;
        }
        return px;
    }

    [Fact]
    public void FillsOnlyInsideTheFrame()
    {
        var f = FloodFill.Compute(Framed(), 10, 10, 4, 4, 10)!;
        Assert.Equal(255, f.Mask[4 * 10 + 4]);
        Assert.Equal(255, f.Mask[2 * 10 + 4]); // dilatación cubre el borde
        Assert.Equal(0, f.Mask[0]);
        Assert.Equal(new PixelRect(2, 2, 6, 6), f.Bounds);
    }

    [Fact]
    public void OutOfBoundsReturnsNull() => Assert.Null(FloodFill.Compute(Framed(), 10, 10, 20, 1, 0));

    [Fact]
    public void ContentBoundsFindsOpaquePixels()
    {
        var px = new byte[8 * 8 * 4];
        px[(3 * 8 + 5) * 4 + 3] = 255;
        px[(6 * 8 + 1) * 4 + 3] = 10;
        Assert.Equal(new PixelRect(1, 3, 5, 4), FloodFill.ContentBounds(px, 8, 8));
    }
}

public class UndoStackTests
{
    [Fact]
    public void UndoRedoAndRedoIsClearedOnPush()
    {
        var value = 0;
        var stack = new UndoStack();
        stack.Execute(new DelegateAction("a", () => value = 0, () => value = 1));
        stack.Execute(new DelegateAction("b", () => value = 1, () => value = 2));
        Assert.Equal(2, value);
        stack.Undo();
        Assert.Equal(1, value);
        stack.Redo();
        Assert.Equal(2, value);
        stack.Undo();
        stack.Execute(new DelegateAction("c", () => value = 1, () => value = 3));
        Assert.False(stack.CanRedo);
        Assert.Equal("c", stack.NextUndoLabel);
    }

    [Fact]
    public void RespectsStepAndMemoryLimits()
    {
        var stack = new UndoStack(maxSteps: 3, maxBytes: 250);
        for (var i = 0; i < 5; i++) stack.Push(new DelegateAction($"{i}", () => { }, () => { }, 100));
        Assert.Equal(2, stack.UndoCount);
        Assert.Equal(200, stack.Bytes);
    }

    private sealed class Counter(int n) : IMergeableAction
    {
        public int N { get; private set; } = n;
        public string Label => "contador";
        public long Bytes => 0;
        public void Undo() { }
        public void Redo() { }
        public bool TryMerge(IUndoableAction next)
        {
            if (next is not Counter c) return false;
            N = c.N;
            return true;
        }
    }

    [Fact]
    public void MergesConsecutiveActions()
    {
        var stack = new UndoStack();
        stack.Push(new Counter(1));
        stack.Push(new Counter(2));
        Assert.Equal(1, stack.UndoCount);
    }
}

public class TrazoFileTests
{
    private static readonly byte[] FakePng = [0x89, 0x50, 0x4E, 0x47, 1, 2, 3];

    [Fact]
    public void V2RoundTrip()
    {
        var p = new ProjectData
        {
            Name = "Prueba", Width = 800, Height = 600, BackgroundColor = "#112233", BackgroundVisible = false, ActiveLayer = 1,
            Layers =
            [
                new(new LayerProperties { Name = "Fondo" }, FakePng),
                new(new LayerProperties { Name = "Tinta", Opacity = 0.5f, Blend = BlendMode.Multiply, AlphaLock = true, Clip = true }, FakePng),
            ],
            ThumbnailPng = FakePng,
        };
        using var ms = new MemoryStream();
        TrazoFile.Write(ms, p);
        ms.Position = 0;
        var r = TrazoFile.Read(ms);
        Assert.Equal("Prueba", r.Name);
        Assert.Equal((800, 600), (r.Width, r.Height));
        Assert.False(r.BackgroundVisible);
        Assert.Equal(1, r.ActiveLayer);
        Assert.Equal(p.Layers[1].Properties with { }, r.Layers[1].Properties);
        Assert.Equal(FakePng, r.Layers[0].Png);
        ms.Position = 0;
        Assert.Equal(FakePng, TrazoFile.ReadThumbnail(ms));
    }

    [Fact]
    public void ReadsWebV1Format()
    {
        var b64 = Convert.ToBase64String(FakePng);
        var json = $$"""
        {"format":"trazo","version":1,"name":"Web","width":300,"height":200,"bg":{"color":"#ffffff","visible":true},"active":0,
         "layers":[{"id":"a","name":"Capa 1","opacity":0.8,"blend":"screen","visible":true,"alphaLock":false,"clip":false,"png":"data:image/png;base64,{{b64}}"}]}
        """;
        var r = TrazoFile.Read(new MemoryStream(Encoding.UTF8.GetBytes(json)));
        Assert.Equal("Web", r.Name);
        Assert.Equal(BlendMode.Screen, r.Layers[0].Properties.Blend);
        Assert.Equal(0.8f, r.Layers[0].Properties.Opacity);
        Assert.Equal(FakePng, r.Layers[0].Png);
    }

    [Fact]
    public void RejectsGarbage() =>
        Assert.Throws<InvalidDataException>(() => TrazoFile.Read(new MemoryStream("hola"u8.ToArray())));

    [Fact]
    public void CanvasFitKeepsWithinLimits()
    {
        var (w, h) = CanvasLimits.Fit(20000, 20000);
        Assert.True(w <= CanvasLimits.MaxSide && (long)w * h <= CanvasLimits.MaxArea);
        Assert.Equal("Capa 3", CanvasLimits.NextLayerName(["Capa 1", "Capa 2"]));
    }
}
