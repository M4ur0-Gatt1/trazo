using System.Numerics;
using Trazo.Core.Documents;
using Trazo.Core.Geometry;
using Trazo.Core.Imaging;
using Trazo.Core.IO;

namespace Trazo.Core.Tests;

/// <summary>TR-01 — Transformar (docs/specs/TR-01-transform.md), parte sin GPU.</summary>
public class TR01_TransformTests
{
    private static readonly Vector2[] Quad = [new(10, 20), new(300, 40), new(280, 260), new(30, 200)];

    private static void Near(Vector2 expected, Vector2 actual, float tol, string what = "")
    {
        Assert.True(Vector2.Distance(expected, actual) <= tol, $"{what} esperado {expected}, obtenido {actual}");
    }

    [Fact]
    public void HomographyMapsCornersExactly()
    {
        var h = Homography.FromUnitSquare(Quad)!.Value;
        Near(Quad[0], h.Map(0, 0), 1e-3f);
        Near(Quad[1], h.Map(1, 0), 1e-3f);
        Near(Quad[2], h.Map(1, 1), 1e-3f);
        Near(Quad[3], h.Map(0, 1), 1e-3f);
    }

    [Fact]
    public void HomographyInverseRoundTrips()
    {
        var h = Homography.FromUnitSquare(Quad)!.Value;
        var inv = h.Inverse();
        var rng = new Random(3);
        for (var k = 0; k < 200; k++)
        {
            var uv = new Vector2((float)rng.NextDouble(), (float)rng.NextDouble());
            Near(uv, inv.Map(h.Map(uv)), 1e-4f, "ida y vuelta");
        }
    }

    [Fact]
    public void DegenerateQuadIsRejected()
    {
        Assert.Null(Homography.FromUnitSquare([new(0, 0), new(100, 0), new(200, 0), new(0, 100)]));        // colineales
        Assert.Null(Homography.FromUnitSquare([new(0, 0), new(100, 100), new(100, 0), new(0, 100)]));     // cruzado
        Assert.Null(Homography.FromUnitSquare([new(0, 0), new(100, 0), new(60, 20), new(0, 100)]));        // cóncavo
        Assert.Null(Homography.FromUnitSquare([new(0, 0), new(0.5f, 0), new(0.5f, 0.5f), new(0, 0.5f)])); // < 1 px²
        var s = new TransformState(new PixelRect(0, 0, 100, 100), new Vector2(200));
        s.SetMode(TransformMode.Distort);
        var before = s.Corners();
        Assert.False(s.MovePoint(2, new Vector2(-50, -50)));
        Assert.Equal(before, s.Corners());
    }

    [Fact]
    public void BezierPatchReproducesAffine()
    {
        var a = Matrix3x2.CreateScale(120, 80) * Matrix3x2.CreateRotation(0.4f) * Matrix3x2.CreateTranslation(50, 30);
        var ctrl = new Vector2[16];
        for (var j = 0; j < 4; j++)
            for (var i = 0; i < 4; i++)
                ctrl[j * 4 + i] = Vector2.Transform(new Vector2(i / 3f, j / 3f), a);
        var rng = new Random(5);
        for (var k = 0; k < 100; k++)
        {
            var uv = new Vector2((float)rng.NextDouble(), (float)rng.NextDouble());
            Near(Vector2.Transform(uv, a), BezierPatch.Evaluate(ctrl, uv.X, uv.Y), 1e-3f);
        }
    }

    [Fact]
    public void ModeSwitchKeepsMapping()
    {
        var s = new TransformState(new PixelRect(10, 20, 200, 100), new Vector2(800, 600))
        {
            Matrix = Matrix3x2.CreateScale(1.3f, 0.8f) * Matrix3x2.CreateRotation(0.3f) * Matrix3x2.CreateTranslation(40, -15),
        };
        var samples = Enumerable.Range(0, 25).Select(k => new Vector2(k % 5 / 4f, k / 5 / 4f)).ToArray();
        var expected = samples.Select(uv => s.Map(uv.X, uv.Y)).ToArray();
        foreach (var mode in new[] { TransformMode.Distort, TransformMode.Warp, TransformMode.Freeform, TransformMode.Uniform })
        {
            s.SetMode(mode);
            for (var k = 0; k < samples.Length; k++) Near(expected[k], s.Map(samples[k].X, samples[k].Y), 1e-2f, mode.ToString());
        }
    }

    [Fact]
    public void MagneticsSnapsWithinThreshold()
    {
        float Deg(float d) => d * MathF.PI / 180;
        Assert.Equal(Deg(15), Magnetics.SnapAngle(Deg(13)), 4);
        Assert.Equal(Deg(10), Magnetics.SnapAngle(Deg(10)), 4);        // a 5° de 15°: no ajusta
        Assert.Equal(Deg(-90), Magnetics.SnapAngle(Deg(-88)), 4);
        Assert.Equal(new Vector2(100, 0), Magnetics.LockAxis(new Vector2(100, 5)));   // 2,9°
        Assert.Equal(new Vector2(100, 20), Magnetics.LockAxis(new Vector2(100, 20))); // 11,3°
        Assert.Equal(new Vector2(0, -100), Magnetics.LockAxis(new Vector2(3, -100)));
        Assert.Equal(1f, Magnetics.SnapScale(1.02f));
        Assert.Equal(1.05f, Magnetics.SnapScale(1.05f));
        Assert.Equal(new Vector2(3, -4), Magnetics.SnapCenter(new Vector2(97, 104), new Vector2(100, 100), 8));
        Assert.Equal(Vector2.Zero, Magnetics.SnapCenter(new Vector2(90, 100), new Vector2(100, 100), 8));

        // en la sesión: mover casi en horizontal queda horizontal; girar 14° con dos dedos queda en 15°
        var s = new TransformState(new PixelRect(0, 0, 100, 100), new Vector2(1000));
        s.Anchor([(1, new Vector2(50, 50))]);
        s.Move(new Dictionary<uint, Vector2> { [1] = new(250, 58) });
        Assert.Equal(0, s.Matrix.M32, 4);
        Assert.Equal(200, s.Matrix.M31, 3);
        s.Matrix = Matrix3x2.Identity;
        s.Anchor([(1, new Vector2(0, 0)), (2, new Vector2(100, 0))]);
        s.Move(new Dictionary<uint, Vector2> { [1] = new(0, 0), [2] = new(100 * MathF.Cos(Deg(14)), 100 * MathF.Sin(Deg(14))) });
        Assert.Equal(Deg(15), MathF.Atan2(s.Matrix.M12, s.Matrix.M11), 4);
        s.Magnetic = false;
        s.Matrix = Matrix3x2.Identity;
        s.Anchor([(1, new Vector2(0, 0)), (2, new Vector2(100, 0))]);
        s.Move(new Dictionary<uint, Vector2> { [1] = new(0, 0), [2] = new(100 * MathF.Cos(Deg(14)), 100 * MathF.Sin(Deg(14))) });
        Assert.Equal(Deg(14), MathF.Atan2(s.Matrix.M12, s.Matrix.M11), 4);
    }

    [Theory]
    [InlineData(TransformMode.Freeform)]
    [InlineData(TransformMode.Uniform)]
    public void CornerHandleKeepsOppositeCorner(TransformMode mode)
    {
        var s = new TransformState(new PixelRect(100, 100, 200, 100), new Vector2(1000))
        {
            Matrix = Matrix3x2.CreateRotation(0.5f, new Vector2(200, 150)),
            Magnetic = false,
        };
        s.SetMode(mode);
        var before = s.AffineHandles();
        var topLeft = before[(int)TransformHandle.TopLeft];
        s.BeginHandle(TransformHandle.BottomRight, before[(int)TransformHandle.BottomRight]);
        s.DragHandle(before[(int)TransformHandle.BottomRight] + new Vector2(80, 30));
        s.EndHandle();
        var after = s.AffineHandles();
        Near(topLeft, after[(int)TransformHandle.TopLeft], 1e-3f, "esquina opuesta");
        var w = Vector2.Distance(after[0], after[1]);
        var h = Vector2.Distance(after[0], after[3]);
        if (mode == TransformMode.Uniform) Assert.Equal(2, w / h, 3);           // proporción 200:100 conservada
        else Assert.NotEqual(2, w / h, 2);
        // el asa de un lado solo cambia su eje
        s.BeginHandle(TransformHandle.Right, after[(int)TransformHandle.Right]);
        s.DragHandle(after[(int)TransformHandle.Right] + Vector2.Normalize(after[1] - after[0]) * 50);
        var side = s.AffineHandles();
        Near(after[(int)TransformHandle.Left], side[(int)TransformHandle.Left], 1e-3f, "lado opuesto");
        if (mode == TransformMode.Freeform) Assert.Equal(h, Vector2.Distance(side[0], side[3]), 2);
    }
}

/// <summary>LQ-01 — Licuar (docs/specs/LQ-01-liquify.md).</summary>
public class LQ01_LiquifyTests
{
    private const int W = 64, H = 64;

    private static byte[] Pattern(Func<int, int, byte> alpha)
    {
        var px = new byte[W * H * 4];
        for (var y = 0; y < H; y++)
            for (var x = 0; x < W; x++)
            {
                var a = alpha(x, y);
                var o = (y * W + x) * 4;
                // color premultiplicado que varía con x e y
                px[o] = (byte)(a * x / (W - 1));
                px[o + 1] = (byte)(a * y / (H - 1));
                px[o + 2] = (byte)(a / 2);
                px[o + 3] = a;
            }
        return px;
    }

    private static byte[] Render(byte[] src, LiquifyField f, float amount = 1)
    {
        var dst = (byte[])src.Clone();
        Liquify.Render(src, dst, f, f.Full, amount);
        return dst;
    }

    private static double Area(byte[] px) => Enumerable.Range(0, W * H).Sum(i => px[i * 4 + 3] / 255.0);

    private static Vector2 Centroid(byte[] px)
    {
        double sx = 0, sy = 0, s = 0;
        for (var i = 0; i < W * H; i++)
        {
            var a = px[i * 4 + 3];
            sx += a * (i % W + 0.5);
            sy += a * (i / W + 0.5);
            s += a;
        }
        return new Vector2((float)(sx / s), (float)(sy / s));
    }

    [Fact]
    public void ZeroFieldIsIdentity()
    {
        var src = Pattern((x, y) => (byte)((x * 7 + y * 13) % 256));
        Assert.Equal(src, Render(src, new LiquifyField(W, H)));
    }

    [Fact]
    public void PushTranslatesInPlateau()
    {
        var src = Pattern((_, _) => 255);
        var f = new LiquifyField(W, H);
        f.Dab(LiquifyMode.Push, new Vector2(32, 32), new Vector2(22, 32), 1e5f, 1);
        var dst = Render(src, f);
        for (var y = 0; y < H; y++)
            for (var x = 10; x < W; x++)
                for (var c = 0; c < 4; c++)
                    Assert.Equal(src[(y * W + x - 10) * 4 + c], dst[(y * W + x) * 4 + c]);
        // lo que entra por la izquierda viene de fuera del lienzo: transparente
        Assert.Equal(0, dst[(10 * W + 3) * 4 + 3]);
    }

    [Fact]
    public void PushComposes()
    {
        var twice = new LiquifyField(W, H);
        twice.Dab(LiquifyMode.Push, new Vector2(32, 32), new Vector2(27, 32), 1e4f, 1);
        twice.Dab(LiquifyMode.Push, new Vector2(32, 32), new Vector2(27, 32), 1e4f, 1);
        var once = new LiquifyField(W, H);
        once.Dab(LiquifyMode.Push, new Vector2(32, 32), new Vector2(22, 32), 1e4f, 1);
        for (var k = 0; k < once.Dx.Length; k++)
        {
            Assert.True(MathF.Abs(once.Dx[k] - twice.Dx[k]) < 0.05f, $"nodo {k}: {once.Dx[k]} ≠ {twice.Dx[k]}");
            Assert.True(MathF.Abs(once.Dy[k] - twice.Dy[k]) < 0.05f);
        }
    }

    [Theory]
    [InlineData(LiquifyMode.Push)]
    [InlineData(LiquifyMode.TwirlRight)]
    [InlineData(LiquifyMode.Pinch)]
    public void ComposedFieldEqualsSequentialWarps(LiquifyMode second)
    {
        // definición de la composición: un único campo con los dos toques = deformar dos veces seguidas
        var src = Pattern((x, y) => (byte)(128 + 120 * MathF.Sin(x / 5f) * MathF.Cos(y / 7f)));
        var composed = new LiquifyField(W, H);
        composed.Dab(LiquifyMode.Push, new Vector2(30, 32), new Vector2(20, 28), 20, 1);
        var first = Render(src, composed);
        var alone = new LiquifyField(W, H);
        alone.Dab(second, new Vector2(36, 34), new Vector2(28, 34), 18, 1);
        var sequential = Render(first, alone);
        composed.Dab(second, new Vector2(36, 34), new Vector2(28, 34), 18, 1);
        var once = Render(src, composed);
        // se mide solo donde actúa el segundo toque; remuestrear dos veces suaviza un poco más que una
        var zone = Enumerable.Range(0, W * H).Where(i => Vector2.Distance(new Vector2(i % W + 0.5f, i / W + 0.5f), new Vector2(36, 34)) < 18).ToList();
        var mean = zone.Average(i => Enumerable.Range(0, 3).Average(c => Math.Abs(once[i * 4 + c] - sequential[i * 4 + c])));
        Assert.True(mean < 0.8, $"diferencia media {mean:0.00}"); // correcto: 0,45–0,57; con la composición rota: 1,1–4,5
    }

    [Theory]
    [InlineData(LiquifyMode.Push)]
    [InlineData(LiquifyMode.TwirlRight)]
    [InlineData(LiquifyMode.Pinch)]
    [InlineData(LiquifyMode.Expand)]
    public void DabIsLocal(LiquifyMode mode)
    {
        var src = Pattern((x, y) => (byte)(128 + (x * 31 + y * 17) % 128));
        var f = new LiquifyField(W, H);
        var c = new Vector2(30, 34);
        const float r = 12;
        var rect = f.Dab(mode, c, c - new Vector2(4, 2), r, 1);
        var dst = Render(src, f);
        var changed = 0;
        for (var y = 0; y < H; y++)
            for (var x = 0; x < W; x++)
            {
                var same = Enumerable.Range(0, 4).All(k => src[(y * W + x) * 4 + k] == dst[(y * W + x) * 4 + k]);
                if (same) continue;
                changed++;
                var d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), c);
                Assert.True(d <= r + f.Spacing * 1.5f, $"cambió ({x},{y}) a {d:0.0} px");
                Assert.True(x >= rect.X && x < rect.Right && y >= rect.Y && y < rect.Bottom, "fuera del rectángulo devuelto");
            }
        Assert.True(changed > 20, "el toque no hizo nada");
    }

    [Fact]
    public void PinchShrinksExpandGrows()
    {
        var disk = Pattern((x, y) => (byte)(Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(32)) < 12 ? 255 : 0));
        var area = Area(disk);
        var pinch = new LiquifyField(W, H);
        var expand = new LiquifyField(W, H);
        for (var k = 0; k < 4; k++)
        {
            pinch.Dab(LiquifyMode.Pinch, new Vector2(32), new Vector2(32), 30, 1);
            expand.Dab(LiquifyMode.Expand, new Vector2(32), new Vector2(32), 30, 1);
        }
        Assert.True(Area(Render(disk, pinch)) < area * 0.8, "pellizcar no encogió");
        Assert.True(Area(Render(disk, expand)) > area * 1.2, "expandir no agrandó");
    }

    [Theory]
    [InlineData(LiquifyMode.TwirlRight, 1)]
    [InlineData(LiquifyMode.TwirlLeft, -1)]
    public void TwirlDirection(LiquifyMode mode, int sign)
    {
        // punto a la derecha del centro: a la derecha (horario, y hacia abajo) baja; a la izquierda sube
        var dot = Pattern((x, y) => (byte)(Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(44, 32)) < 3 ? 255 : 0));
        var f = new LiquifyField(W, H);
        for (var k = 0; k < 3; k++) f.Dab(mode, new Vector2(32), new Vector2(32), 28, 1);
        var c = Centroid(Render(dot, f));
        Assert.True(sign * (c.Y - 32) > 2, $"centroide {c}");
    }

    [Fact]
    public void ReconstructRestoresOriginal()
    {
        var src = Pattern((x, y) => (byte)(100 + (x + y) % 100));
        var f = new LiquifyField(W, H);
        f.Dab(LiquifyMode.Push, new Vector2(32, 32), new Vector2(20, 26), 25, 1);
        f.Dab(LiquifyMode.TwirlLeft, new Vector2(32, 32), new Vector2(32, 32), 25, 1);
        Assert.False(f.IsZero);
        for (var k = 0; k < 30; k++) f.Dab(LiquifyMode.Reconstruct, new Vector2(32, 32), new Vector2(32, 32), 1e4f, 1);
        Assert.True(f.Dx.Concat(f.Dy).Max(MathF.Abs) < 0.01f);
        var dst = Render(src, f);
        for (var i = 0; i < src.Length; i++) Assert.True(Math.Abs(src[i] - dst[i]) <= 1, $"byte {i}");
    }

    [Fact]
    public void AdjustScalesField()
    {
        var src = Pattern((x, y) => (byte)(100 + (x * 3 + y) % 150));
        var f = new LiquifyField(W, H);
        f.Dab(LiquifyMode.Push, new Vector2(32, 32), new Vector2(24, 30), 20, 1);
        Assert.Equal(src, Render(src, f, 0));
        Assert.Equal(Render(src, f), Render(src, f, 1));
        Assert.NotEqual(Render(src, f), Render(src, f, 0.5f));
    }
}

/// <summary>DG-01 — Guías de dibujo y Asistente (docs/specs/DG-01-drawing-guides.md).</summary>
public class DG01_GuideTests
{
    private static GuideAssist Assist(DrawingGuide g, Vector2 start)
    {
        var a = new GuideAssist(g, 6);
        a.Begin(start);
        return a;
    }

    [Fact]
    public void GridAssistLocksAxis()
    {
        var g = new DrawingGuide { Kind = GuideKind.Grid, Assist = true };
        var a = Assist(g, new Vector2(100, 100));
        Assert.Equal(new Vector2(110, 100), a.Constrain(new Vector2(110, 103)));
        Assert.Equal(new Vector2(150, 100), a.Constrain(new Vector2(150, 140)));   // ya bloqueado en horizontal
        var v = Assist(g, new Vector2(100, 100));
        Assert.Equal(new Vector2(100, 90), v.Constrain(new Vector2(103, 90)));
        Assert.Equal(new Vector2(100, 300), v.Constrain(new Vector2(160, 300)));
    }

    [Fact]
    public void IsometricAssistUsesThreeAxes()
    {
        var g = new DrawingGuide { Kind = GuideKind.Isometric, Assist = true };
        foreach (var (deg, expected) in new[] { (28f, 30f), (-35f, -30f), (85f, 90f), (205f, 30f) })
        {
            var start = new Vector2(200, 200);
            var a = Assist(g, start);
            var t = deg * MathF.PI / 180;
            a.Constrain(start + new Vector2(MathF.Cos(t), MathF.Sin(t)) * 20);
            var p = a.Constrain(start + new Vector2(MathF.Cos(t), MathF.Sin(t)) * 120 + new Vector2(7, -5));
            var angle = MathF.Atan2(p.Y - start.Y, p.X - start.X) * 180 / MathF.PI;
            // la recta no tiene sentido: 30° y 210° son la misma dirección
            var diff = ((angle - expected) % 180 + 180) % 180;
            Assert.True(diff < 0.01f || diff > 179.99f, $"{deg}° → {angle}°");
        }
    }

    [Fact]
    public void PerspectiveAssistConvergesToVanishingPoint()
    {
        var vp0 = new Vector2(1000, 300);
        var vp1 = new Vector2(-500, 300);
        var g = new DrawingGuide { Kind = GuideKind.Perspective, Assist = true, VanishingPoints = [vp0, vp1] };
        var start = new Vector2(200, 500);
        var a = Assist(g, start);
        var toward = Vector2.Normalize(vp0 - start);
        a.Constrain(start + toward * 20 + new Vector2(0, 3));
        var rng = new Random(9);
        for (var k = 0; k < 50; k++)
        {
            var p = a.Constrain(start + toward * (20 + k * 10) + new Vector2((float)rng.NextDouble() * 20 - 10, (float)rng.NextDouble() * 20 - 10));
            // distancia del punto a la recta inicio → punto de fuga
            var d = MathF.Abs((p.X - start.X) * toward.Y - (p.Y - start.Y) * toward.X);
            Assert.True(d < 1e-3f, $"a {d} px de la recta");
        }
        // con 2 puntos de fuga también se admiten verticales
        var vert = Assist(g, start);
        Assert.Equal(start.X, vert.Constrain(start + new Vector2(1, -40)).X, 3);
    }

    [Fact]
    public void AssistWaitsForThreshold()
    {
        var a = Assist(new DrawingGuide { Kind = GuideKind.Grid, Assist = true }, new Vector2(50, 50));
        Assert.Equal(new Vector2(50, 50), a.Constrain(new Vector2(53, 54)));   // 5 px < 6
        Assert.Null(a.Direction);
        a.Constrain(new Vector2(57, 51));
        Assert.Equal(Vector2.UnitX, a.Direction);
    }

    [Fact]
    public void GridLinesGeometry()
    {
        var lines = new DrawingGuide { Kind = GuideKind.Grid, Size = 20 }.Lines(100, 60);
        Assert.Equal(6, lines.Count);
        Assert.Equal([20f, 40f, 60f, 80f], lines.Where(l => l.A.X == l.B.X).Select(l => l.A.X));
        Assert.Equal([20f, 40f], lines.Where(l => l.A.Y == l.B.Y).Select(l => l.A.Y));
        Assert.All(lines, l => Assert.True(l.A.X >= 0 && l.B.X <= 100 && l.A.Y >= 0 && l.B.Y <= 60));
    }

    [Fact]
    public void IsometricLinesGeometry()
    {
        const float size = 40;
        var lines = new DrawingGuide { Kind = GuideKind.Isometric, Size = size }.Lines(400, 300);
        var slanted = lines.Where(l => l.A.X != l.B.X).ToList();
        Assert.True(slanted.Count > 10);
        var half = size * DrawingGuide.Tan30; // las diagonales cortan las verticales en múltiplos de h/2
        foreach (var (a, b) in slanted)
        {
            Assert.InRange(a.X, -1e-3f, 400.001f);
            Assert.InRange(b.Y, -1e-3f, 300.001f);
            var angle = MathF.Abs(MathF.Atan2(b.Y - a.Y, b.X - a.X) * 180 / MathF.PI);
            Assert.True(MathF.Abs(angle - 30) < 0.01f || MathF.Abs(angle - 150) < 0.01f, $"ángulo {angle}");
            var slope = (b.Y - a.Y) / (b.X - a.X);
            var yAtSize = a.Y + slope * (size - a.X);
            var k = yAtSize / half;
            Assert.True(MathF.Abs(k - MathF.Round(k)) < 1e-3f, $"no pasa por un nodo: {k}");
        }
    }

    [Fact]
    public void PerspectiveLinesGeometry()
    {
        var vps = new[] { new Vector2(100, 150), new Vector2(350, 140) };
        var lines = new DrawingGuide { Kind = GuideKind.Perspective, VanishingPoints = vps }.Lines(400, 300);
        Assert.Equal(121, lines.Count);   // 60 rayos por punto + horizonte
        Assert.Equal(60, lines.Count(l => Vector2.Distance(l.A, vps[0]) < 1e-3f));
        Assert.Equal(60, lines.Count(l => Vector2.Distance(l.A, vps[1]) < 1e-3f));
        var horizon = lines[^1];
        Assert.Equal(0, horizon.A.X, 3);
        Assert.Equal(400, horizon.B.X, 3);
        // sin puntos de fuga no hay nada que dibujar
        Assert.Empty(new DrawingGuide { Kind = GuideKind.Perspective }.Lines(400, 300));
    }

    [Fact]
    public void GuideRoundTripsInManifest()
    {
        var guide = new DrawingGuide
        {
            Kind = GuideKind.Perspective, Size = 64, Thickness = 2, Opacity = 0.4f, Color = "FF4A1C", Assist = true,
            VanishingPoints = [new(10.5f, 20.25f), new(-300, 40)],
        };
        foreach (var g in new[] { guide, null })
        {
            var p = new ProjectData
            {
                Width = 32, Height = 24, Guide = g,
                Layers = [new(new LayerProperties { Id = "a" }, FI01_PersistenceTests.Png(32, 24, 1))],
            };
            var file = Path.Combine(Path.GetTempPath(), $"trazo-guide-{Guid.NewGuid():N}.trazo");
            try
            {
                ProjectSaver.Save(file, p);
                var r = ProjectSaver.Load(file).Project.Guide;
                if (g is null) Assert.Null(r);
                else
                {
                    Assert.NotNull(r);
                    Assert.Equal(g.Kind, r.Kind);
                    Assert.Equal(g.Size, r.Size);
                    Assert.Equal(g.Thickness, r.Thickness);
                    Assert.Equal(g.Opacity, r.Opacity);
                    Assert.Equal(g.Color, r.Color);
                    Assert.True(r.Assist);
                    Assert.Equal(g.VanishingPoints, r.VanishingPoints);
                }
            }
            finally
            {
                foreach (var f in new[] { file, ProjectSaver.BackupPath(file) }) if (File.Exists(f)) File.Delete(f);
            }
        }
    }
}
