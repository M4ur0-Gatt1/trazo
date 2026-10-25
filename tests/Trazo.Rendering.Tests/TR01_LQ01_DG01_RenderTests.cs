using System.Numerics;
using System.Security.Cryptography;
using Microsoft.Graphics.Canvas;
using Trazo.Core.Brushes;
using Trazo.Core.Geometry;
using Trazo.Core.Imaging;
using Trazo.Core.Selection;
using Trazo.Core.Strokes;
using Trazo.Rendering;
using Windows.UI;

namespace Trazo.Rendering.Tests;

/// <summary>TR-01, LQ-01 y DG-01 con el motor real (WARP).</summary>
public class TR01_LQ01_DG01_RenderTests
{
    private const int W = BR02_BrushReferenceTests.Width, H = BR02_BrushReferenceTests.Height;
    private static readonly BrushLibrary Lib = new();

    private static (PaintDocument Doc, LayerOperations Ops, BrushEngine Engine) NewDoc() => BR02_BrushReferenceTests.NewDocument();

    /// <summary>Rellena la capa activa con una función BGRA premultiplicada dentro de un rectángulo.</summary>
    private static void Pattern(PaintDocument doc, PixelRect r, Func<int, int, (byte B, byte G, byte R, byte A)> f)
    {
        var px = new byte[W * H * 4];
        for (var y = r.Y; y < r.Bottom; y++)
            for (var x = r.X; x < r.Right; x++)
            {
                var (b, g, rr, a) = f(x, y);
                var o = (y * W + x) * 4;
                px[o] = b;
                px[o + 1] = g;
                px[o + 2] = rr;
                px[o + 3] = a;
            }
        doc.ActiveLayer.Surface.SetPixelBytes(px);
        doc.ActiveLayer.Touch();
    }

    private static readonly PixelRect Content = new(60, 30, 240, 100);

    // degradado suave con manchas grandes: sin altas frecuencias, para comparar interpolaciones distintas
    private static (byte, byte, byte, byte) Smooth(int x, int y) =>
        ((byte)(x * 255 / W), (byte)(y * 255 / H), (byte)(128 + 100 * MathF.Sin(x / 17f) * MathF.Cos(y / 13f)), 255);

    // cuadrícula de líneas cada 16 px sobre fondo claro (para las referencias visuales)
    private static (byte, byte, byte, byte) GridPattern(int x, int y) =>
        (x - Content.X) % 16 < 2 || (y - Content.Y) % 16 < 2 ? ((byte)40, (byte)40, (byte)200, (byte)255) : ((byte)230, (byte)220, (byte)210, (byte)255);

    private static byte[] Render(PaintDocument doc, Func<List<IDisposable>, ICanvasImage> image)
    {
        var owned = new List<IDisposable>();
        using var rt = new CanvasRenderTarget(doc.Device, W, H, 96);
        using (var ds = rt.CreateDrawingSession())
        {
            ds.Clear(Color.FromArgb(0, 0, 0, 0));
            ds.DrawImage(image(owned));
        }
        owned.ForEach(o => o.Dispose());
        return rt.GetPixelBytes();
    }

    private static string Hash(byte[] b) => Convert.ToHexString(SHA256.HashData(b))[..16];

    /// <summary>Diferencia media y máxima sobre los píxeles opacos en las dos imágenes (sin los bordes, que dependen del antialias).</summary>
    private static (double Mean, int Max, int Count) InteriorDiff(byte[] a, byte[] b)
    {
        long sum = 0;
        int max = 0, n = 0;
        for (var y = 1; y < H - 1; y++)
            for (var x = 1; x < W - 1; x++)
            {
                var opaque = true;
                foreach (var (dx, dy) in new[] { (0, 0), (1, 0), (-1, 0), (0, 1), (0, -1) })
                {
                    var o = ((y + dy) * W + x + dx) * 4 + 3;
                    if (a[o] < 255 || b[o] < 255) opaque = false;
                }
                if (!opaque) continue;
                n++;
                for (var c = 0; c < 3; c++)
                {
                    var d = Math.Abs(a[(y * W + x) * 4 + c] - b[(y * W + x) * 4 + c]);
                    sum += d;
                    max = Math.Max(max, d);
                }
            }
        return (sum / (3.0 * Math.Max(1, n)), max, n);
    }

    // ---------- TR-01 ----------

    [Theory]
    [InlineData(TransformMode.Distort)]
    [InlineData(TransformMode.Warp)]
    public void IdentityDeformationIsLossless(TransformMode mode)
    {
        var (doc, _, _) = NewDoc();
        using (doc)
        {
            Pattern(doc, Content, (x, y) => ((byte)(x * 7 % 256), (byte)(y * 11 % 256), (byte)((x ^ y) % 256), 255));
            var original = doc.ActiveLayer.Surface.GetPixelBytes();
            using var t = TransformSession.Start(doc, doc.ActiveLayer)!;
            t.SetMode(mode);
            Assert.False(t.State.IsAffine);
            var px = Render(doc, t.Preview);
            var max = Enumerable.Range(0, px.Length).Max(i => Math.Abs(px[i] - original[i]));
            Assert.True(max <= 1, $"diferencia máxima {max}");
        }
    }

    [Fact]
    public void MeshMatchesAffinePath()
    {
        var (doc, _, _) = NewDoc();
        using (doc)
        {
            Pattern(doc, Content, Smooth);
            var m = Matrix3x2.CreateRotation(0.35f, new Vector2(180, 80)) * Matrix3x2.CreateScale(0.8f, new Vector2(180, 80));
            using var affine = TransformSession.Start(doc, doc.ActiveLayer)!;
            affine.Matrix = m;
            var a = Render(doc, affine.Preview);
            using var mesh = TransformSession.Start(doc, doc.ActiveLayer)!;
            mesh.Matrix = m;
            mesh.SetMode(TransformMode.Distort);
            Assert.False(mesh.State.IsAffine);
            var b = Render(doc, mesh.Preview);
            var d = InteriorDiff(a, b);
            Assert.True(d.Count > 10000, $"solo {d.Count} píxeles comparables");
            Assert.True(d.Mean < 1.5 && d.Max <= 16, $"media {d.Mean:0.00}, máxima {d.Max}");
        }
    }

    [Fact]
    public void PerspectiveMatchesReference()
    {
        var (doc, _, _) = NewDoc();
        using (doc)
        {
            Pattern(doc, Content, Smooth);
            var src = doc.ActiveLayer.Surface.GetPixelBytes();
            using var t = TransformSession.Start(doc, doc.ActiveLayer)!;
            t.SetMode(TransformMode.Distort);
            Vector2[] quad = [new(90, 20), new(330, 45), new(300, 125), new(40, 150)];
            Assert.True(t.State.SetCorners(quad));
            var mesh = Render(doc, t.Preview);

            // referencia exacta en la CPU: cada píxel toma el origen por la homografía inversa (bilineal)
            var inv = Homography.FromUnitSquare(quad)!.Value.Inverse();
            var reference = new byte[W * H * 4];
            for (var y = 0; y < H; y++)
                for (var x = 0; x < W; x++)
                {
                    var uv = inv.Map(x + 0.5, y + 0.5);
                    if (uv.X < 0 || uv.X > 1 || uv.Y < 0 || uv.Y > 1) continue;
                    var sx = Content.X + uv.X * Content.Width - 0.5f;
                    var sy = Content.Y + uv.Y * Content.Height - 0.5f;
                    sx = Math.Clamp(sx, Content.X, Content.Right - 1.001f);
                    sy = Math.Clamp(sy, Content.Y, Content.Bottom - 1.001f);
                    int x0 = (int)sx, y0 = (int)sy;
                    float fx = sx - x0, fy = sy - y0;
                    for (var c = 0; c < 4; c++)
                    {
                        float S(int xx, int yy) => src[(yy * W + xx) * 4 + c];
                        var v = (S(x0, y0) * (1 - fx) + S(x0 + 1, y0) * fx) * (1 - fy) + (S(x0, y0 + 1) * (1 - fx) + S(x0 + 1, y0 + 1) * fx) * fy;
                        reference[(y * W + x) * 4 + c] = (byte)MathF.Round(v);
                    }
                }
            var d = InteriorDiff(mesh, reference);
            Assert.True(d.Count > 10000, $"solo {d.Count} píxeles comparables");
            Assert.True(d.Mean < 2, $"media {d.Mean:0.00}, máxima {d.Max}");
        }
    }

    [Fact]
    public void WarpHasNoSeams()
    {
        var (doc, _, _) = NewDoc();
        using (doc)
        {
            // semitransparente: un hueco dejaría alfa 0 y un solape, alfa > 128
            Pattern(doc, Content, (_, _) => (64, 32, 16, 128));
            using var t = TransformSession.Start(doc, doc.ActiveLayer)!;
            t.SetMode(TransformMode.Warp);
            var pts = t.State.HandlePoints();
            t.State.MovePoint(5, pts[5] + new Vector2(25, 15));
            t.State.MovePoint(10, pts[10] + new Vector2(-20, 10));
            t.State.MovePoint(3, pts[3] + new Vector2(30, -20));
            var px = Render(doc, t.Preview);
            var interior = 0;
            for (var y = 1; y < H - 1; y++)
                for (var x = 1; x < W - 1; x++)
                {
                    var neighbours = new[] { (1, 0), (-1, 0), (0, 1), (0, -1), (1, 1), (-1, -1), (1, -1), (-1, 1) }
                        .All(n => px[((y + n.Item2) * W + x + n.Item1) * 4 + 3] > 0);
                    if (!neighbours) continue;
                    interior++;
                    var a = px[(y * W + x) * 4 + 3];
                    Assert.True(Math.Abs(a - 128) <= 1, $"costura o solape en ({x},{y}): alfa {a}");
                }
            Assert.True(interior > 15000, $"solo {interior} píxeles interiores");
        }
    }

    [Fact]
    public void DistortMovesOnlySelection()
    {
        var (doc, ops, _) = NewDoc();
        using (doc)
        {
            Pattern(doc, new PixelRect(0, 0, W, H), Smooth);
            var before = doc.ActiveLayer.Surface.GetPixelBytes();
            var sel = SelectionMask.Rectangle(W, H, 40, 40, 140, 120);
            ops.SetSelection(sel, "sel");
            var t = TransformSession.Start(doc, doc.ActiveLayer)!;
            t.SetMode(TransformMode.Distort);
            Vector2[] quad = [new(220, 30), new(330, 50), new(320, 140), new(200, 120)];
            Assert.True(t.State.SetCorners(quad));
            t.Apply(ops);
            t.Dispose();
            var after = doc.ActiveLayer.Surface.GetPixelBytes();
            for (var y = 0; y < H; y++)
                for (var x = 0; x < W; x++)
                {
                    var o = (y * W + x) * 4;
                    var inSel = x >= 40 && x < 140 && y >= 40 && y < 120;
                    var inDest = x >= 195 && x < 335 && y >= 25 && y < 145;
                    if (inSel && !inDest) Assert.Equal(0, after[o + 3]);                         // levantado
                    else if (!inSel && !inDest)
                        for (var c = 0; c < 4; c++) Assert.Equal(before[o + c], after[o + c]);    // intacto
                }
            // la selección se deformó con el contenido
            var b = doc.Selection!.Bounds;
            Assert.InRange(b.X, 198, 202);
            Assert.InRange(b.Right, 328, 332);
            Assert.InRange(b.Y, 28, 32);
            Assert.InRange(b.Bottom, 138, 142);
        }
    }

    [Fact]
    public void TransformModesUndoRedo()
    {
        var (doc, ops, _) = NewDoc();
        using (doc)
        {
            Pattern(doc, Content, Smooth);
            var h0 = Hash(doc.ActiveLayer.Surface.GetPixelBytes());
            foreach (var mode in new[] { TransformMode.Distort, TransformMode.Warp })
            {
                using var t = TransformSession.Start(doc, doc.ActiveLayer)!;
                t.SetMode(mode);
                var pts = t.State.HandlePoints();
                t.State.MovePoint(0, pts[0] + new Vector2(30, 10));
                t.Apply(ops);
            }
            var h2 = Hash(doc.ActiveLayer.Surface.GetPixelBytes());
            Assert.NotEqual(h0, h2);
            doc.History.Undo();
            doc.History.Undo();
            Assert.Equal(h0, Hash(doc.ActiveLayer.Surface.GetPixelBytes()));
            doc.History.Redo();
            doc.History.Redo();
            Assert.Equal(h2, Hash(doc.ActiveLayer.Surface.GetPixelBytes()));
        }
    }

    [Theory]
    [InlineData(TransformMode.Distort)]
    [InlineData(TransformMode.Warp)]
    public async Task DistortAndWarpMatchReference(TransformMode mode)
    {
        var (doc, ops, _) = NewDoc();
        using (doc)
        {
            Pattern(doc, Content, GridPattern);
            using var t = TransformSession.Start(doc, doc.ActiveLayer)!;
            t.SetMode(mode);
            var pts = t.State.HandlePoints();
            if (mode == TransformMode.Distort)
            {
                Vector2[] quad = [new(110, 15), new(270, 15), new(370, 150), new(10, 150)];
                Assert.True(t.State.SetCorners(quad));
            }
            else
            {
                // dobla el centro hacia abajo y las esquinas superiores hacia dentro
                foreach (var i in new[] { 5, 6 }) t.State.MovePoint(i, pts[i] + new Vector2(0, 45));
                t.State.MovePoint(0, pts[0] + new Vector2(20, 10));
                t.State.MovePoint(3, pts[3] + new Vector2(-20, 10));
            }
            t.Apply(ops);
            var diff = await Golden.CheckAsync(doc.ActiveLayer.Surface, "transform", mode.ToString().ToLowerInvariant());
            Assert.True(diff.Within(), diff.ToString());
        }
    }

    // ---------- LQ-01 ----------

    private static void Swirl(LiquifySession s)
    {
        s.Mode = LiquifyMode.Push;
        s.Size = 80;
        s.Strength = 1;
        s.BeginStroke(new Vector2(120, 80), 1);
        for (var k = 1; k <= 20; k++) s.ContinueStroke(new Vector2(120 + k * 4, 80 + k), 1);
        s.EndStroke();
        s.Mode = LiquifyMode.TwirlRight;
        s.BeginStroke(new Vector2(240, 80), 1);
        s.DabAt(new Vector2(240, 80), 1);
        s.EndStroke();
    }

    [Fact]
    public void LiquifyApplyMatchesPreview()
    {
        var (doc, ops, _) = NewDoc();
        using (doc)
        {
            Pattern(doc, Content, GridPattern);
            var src = doc.ActiveLayer.Surface.GetPixelBytes();
            using var s = LiquifySession.Start(doc, doc.ActiveLayer);
            Swirl(s);
            var preview = Render(doc, s.Preview);
            // la GPU tiene exactamente lo que calcula la CPU para el mismo campo
            var cpu = (byte[])src.Clone();
            Liquify.Render(src, cpu, s.Field, s.Field.Full);
            Assert.Equal(Hash(cpu), Hash(s.Result.GetPixelBytes()));
            s.Apply(ops);
            Assert.Equal(Hash(preview), Hash(doc.ActiveLayer.Surface.GetPixelBytes()));
            Assert.NotEqual(Hash(src), Hash(preview));
        }
    }

    [Fact]
    public void LiquifyRespectsSelectionAndUndo()
    {
        var (doc, ops, _) = NewDoc();
        using (doc)
        {
            Pattern(doc, Content, GridPattern);
            var before = doc.ActiveLayer.Surface.GetPixelBytes();
            var sel = SelectionMask.Rectangle(W, H, 100, 0, 200, H);
            ops.SetSelection(sel, "sel");
            using var s = LiquifySession.Start(doc, doc.ActiveLayer);
            Swirl(s);
            s.Apply(ops);
            var after = doc.ActiveLayer.Surface.GetPixelBytes();
            var changedInside = 0;
            for (var i = 0; i < W * H; i++)
            {
                var same = Enumerable.Range(0, 4).All(c => before[i * 4 + c] == after[i * 4 + c]);
                if (sel.Data[i] == 0) Assert.True(same, $"cambió ({i % W},{i / W}) fuera de la selección");
                else if (!same) changedInside++;
            }
            Assert.True(changedInside > 500);
            doc.History.Undo();
            Assert.Equal(Hash(before), Hash(doc.ActiveLayer.Surface.GetPixelBytes()));
            doc.History.Redo();
            Assert.Equal(Hash(after), Hash(doc.ActiveLayer.Surface.GetPixelBytes()));
        }
    }

    [Fact]
    public async Task LiquifyMatchesReference()
    {
        var (doc, ops, _) = NewDoc();
        using (doc)
        {
            Pattern(doc, Content, GridPattern);
            using var s = LiquifySession.Start(doc, doc.ActiveLayer);
            Swirl(s);
            s.Mode = LiquifyMode.Pinch;
            s.Size = 60;
            s.DabAt(new Vector2(90, 60), 1);
            s.Mode = LiquifyMode.Expand;
            s.DabAt(new Vector2(270, 105), 1);
            s.Apply(ops);
            var diff = await Golden.CheckAsync(doc.ActiveLayer.Surface, "liquify", "scene");
            Assert.True(diff.Within(), diff.ToString());
        }
    }

    // ---------- DG-01 ----------

    [Fact]
    public void AssistedStrokeIsStraight()
    {
        var (doc, _, engine) = NewDoc();
        using (doc)
        {
            var guide = new DrawingGuide { Kind = GuideKind.Grid, Assist = true };
            var brush = Lib.All.First(b => b.Id == "monoline");
            var start = new InputPoint(30, 80, 0.5f);
            engine.Begin(start, ToolKind.Brush, brush, Color.FromArgb(255, 0, 0, 0), 6, 1, SymmetryMode.None, 1, seed: 1, assist: new GuideAssist(guide, 6));
            var rng = new Random(4);
            // el primer movimiento decide la dirección; después el pulso tiembla y se desvía
            for (var k = 1; k <= 60; k++)
                engine.Add(new InputPoint(30 + k * 5, 80 + (k < 3 ? 1 : (float)(rng.NextDouble() * 16 - 8) + k * 0.15f), 0.5f));
            engine.End();
            var px = doc.ActiveLayer.Surface.GetPixelBytes();
            var rows = Enumerable.Range(0, H).Where(y => Enumerable.Range(0, W).Any(x => px[(y * W + x) * 4 + 3] > 0)).ToList();
            var cols = Enumerable.Range(0, W).Where(x => Enumerable.Range(0, H).Any(y => px[(y * W + x) * 4 + 3] > 0)).ToList();
            Assert.True(rows.Max() - rows.Min() <= 8, $"filas {rows.Min()}–{rows.Max()}");
            Assert.True(cols.Max() - cols.Min() > 250, "el trazo no avanzó");
            Assert.InRange((rows.Min() + rows.Max()) / 2.0, 78, 82);
        }
    }
}
