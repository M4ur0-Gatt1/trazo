using Microsoft.Graphics.Canvas;
using Trazo.Core.Brushes;
using Trazo.Core.Colors;
using Trazo.Core.Imaging;
using Trazo.Core.IO;
using Trazo.Rendering;
using Windows.Graphics.DirectX;

namespace Trazo.Rendering.Tests;

/// <summary>AD-02 — Ajustes nuevos con el motor real (WARP), comparados con la tabla del núcleo aplicada en la CPU.</summary>
public class AD02_AdjustmentRenderTests
{
    private const int W = 64, H = 48;

    /// <summary>Imagen de prueba sin premultiplicar: degradados en los tres canales y alfa de 0 a 255 por filas.</summary>
    private static byte[] Source()
    {
        var px = new byte[W * H * 4];
        for (var y = 0; y < H; y++)
            for (var x = 0; x < W; x++)
            {
                var i = (y * W + x) * 4;
                px[i] = (byte)(x * 4);
                px[i + 1] = (byte)(255 - x * 3);
                px[i + 2] = (byte)((x * 7 + y * 5) % 256);
                px[i + 3] = (byte)(y < 8 ? 255 : y * 255 / (H - 1));
            }
        return px;
    }

    private static Adjustment Get(string id) => Adjustment.All.First(a => a.Id == id);

    /// <summary>Aplica el ajuste en la GPU y devuelve RGBA sin premultiplicar.</summary>
    private static byte[] Run(Adjustment a, AdjustInputs inputs, byte[] straight, int w = W, int h = H)
    {
        var bgra = new byte[straight.Length];
        Psd.ToPremultipliedBgra(straight, bgra);
        using var bmp = CanvasBitmap.CreateFromBytes(Gpu.Device, bgra, w, h, DirectXPixelFormat.B8G8R8A8UIntNormalized, 96, CanvasAlphaMode.Premultiplied);
        using var rt = new CanvasRenderTarget(Gpu.Device, w, h, 96);
        var owned = new List<IDisposable>();
        using (var ds = rt.CreateDrawingSession())
        {
            ds.Clear(KnownColors.Transparent);
            ds.DrawImage(a.Build(bmp, inputs, owned));
        }
        owned.ForEach(o => o.Dispose());
        var outStraight = new byte[straight.Length];
        Psd.ToStraightRgba(rt.GetPixelBytes(), outStraight);
        return outStraight;
    }

    private static AdjustInputs Inputs(Adjustment a) => new(a.Params.ToDictionary(p => p.Key, p => p.Default));

    /// <summary>
    /// Compara con la referencia. Con alfa bajo el color sin premultiplicar pierde precisión al guardarse en 8 bits
    /// premultiplicados, así que la tolerancia crece como 255/alfa (la misma pérdida que ya tiene la imagen de entrada).
    /// </summary>
    private static void AssertClose(byte[] expected, byte[] actual, string what)
    {
        var worst = 0.0;
        for (var i = 0; i < expected.Length; i += 4)
        {
            var a = expected[i + 3];
            Assert.Equal(a, actual[i + 3]); // el alfa no cambia
            if (a < 16) continue;
            var tol = 2 + 255.0 / a;
            for (var c = 0; c < 3; c++)
            {
                var d = Math.Abs(expected[i + c] - actual[i + c]);
                worst = Math.Max(worst, d / tol);
                Assert.True(d <= tol, $"{what}: píxel {i / 4} canal {c}: esperado {expected[i + c]}, GPU {actual[i + c]} (alfa {a})");
            }
        }
        Assert.True(worst <= 1);
    }

    // ---------- AD-02.4 ----------

    [Fact]
    public void TableAdjustmentsMatchCpuReference()
    {
        var src = Source();
        // la referencia parte de lo que la GPU puede representar: la entrada ya pasada a premultiplicado y vuelta
        var bgra = new byte[src.Length];
        Psd.ToPremultipliedBgra(src, bgra);
        var baseline = new byte[src.Length];
        Psd.ToStraightRgba(bgra, baseline);

        // curvas: compuesta en S y rojo invertido
        var curvesAdj = Get("curves");
        var ci = Inputs(curvesAdj);
        ci.Curves = new ToneCurves()
            .With(0, DynamicsCurve.From(new(0, 0), new(0.3f, 0.2f), new(0.7f, 0.85f), new(1, 1)))
            .With(1, DynamicsCurve.From(new(0, 1), new(1, 0)));
        var expected = (byte[])baseline.Clone();
        ToneTable.Apply(expected, ci.Curves.Table(0), ci.Curves.Table(1), ci.Curves.Table(2));
        AssertClose(expected, Run(curvesAdj, ci, src), "curvas");
        // sin puntos no cambia nada
        AssertClose(baseline, Run(curvesAdj, Inputs(curvesAdj), src), "curvas identidad");

        // mapa de degradado al 100 %: la luminancia elige el color
        var gm = Get("gradientmap");
        var gi = Inputs(gm);
        gi.Gradient = GradientMap.Library.First(g => g.Stops.Count == 4);
        expected = (byte[])baseline.Clone();
        for (var i = 0; i < expected.Length; i += 4)
        {
            var y = ToneTable.Luma(expected[i] / 255f, expected[i + 1] / 255f, expected[i + 2] / 255f);
            for (var c = 0; c < 3; c++) expected[i + c] = ToneTable.ToByte(ToneTable.Lookup(gi.Gradient.Table(c), y));
        }
        AssertClose(expected, Run(gm, gi, src), "mapa de degradado");
        // al 0 % no cambia nada
        gi.Values["a"] = 0;
        AssertClose(baseline, Run(gm, gi, src), "mapa de degradado al 0 %");

        // equilibrio: sombras hacia el azul, luces hacia el rojo
        var bal = Get("balance");
        var bi = Inputs(bal);
        bi.Values["s2"] = 80;
        bi.Values["h0"] = 60;
        bi.Values["m1"] = -40;
        var cb = new ColorBalance(new BalanceShift(0, 0, 0.8f), new BalanceShift(0, -0.4f, 0), new BalanceShift(0.6f, 0, 0));
        expected = (byte[])baseline.Clone();
        ToneTable.Apply(expected, cb.Table(0), cb.Table(1), cb.Table(2));
        AssertClose(expected, Run(bal, bi, src), "equilibrio de color");
    }

    /// <summary>Regresión: las intensidades intermedias de Invertir y Escala de grises salían al revés.</summary>
    [Fact]
    public void IntermediateIntensityMixesTowardsOriginal()
    {
        var src = Source();
        var bgra = new byte[src.Length];
        Psd.ToPremultipliedBgra(src, bgra);
        var baseline = new byte[src.Length];
        Psd.ToStraightRgba(bgra, baseline);
        var inv = Get("invert");
        var inputs = Inputs(inv);
        inputs.Values["a"] = 30;
        var expected = (byte[])baseline.Clone();
        for (var i = 0; i < expected.Length; i += 4)
            for (var c = 0; c < 3; c++) expected[i + c] = (byte)Math.Round(baseline[i + c] * 0.7 + (255 - baseline[i + c]) * 0.3);
        AssertClose(expected, Run(inv, inputs, src), "invertir al 30 %");
        inputs.Values["a"] = 0;
        AssertClose(baseline, Run(inv, inputs, src), "invertir al 0 %");
    }

    // ---------- AD-02.5 ----------

    [Fact]
    public void MotionBlurFollowsAngle()
    {
        const int w = 96, h = 96;
        // línea vertical opaca de 2 px en el centro
        var src = new byte[w * h * 4];
        for (var y = 20; y < 76; y++)
            for (var x = 47; x < 49; x++)
            {
                var i = (y * w + x) * 4;
                src[i] = src[i + 1] = src[i + 2] = 0;
                src[i + 3] = 255;
            }
        var blur = Get("motionblur");
        var inputs = Inputs(blur);
        Assert.Equal(src, Run(blur, inputs, src, w, h)); // distancia 0: identidad
        inputs.Values["d"] = 24;
        inputs.Values["ang"] = 0;
        var outH = Run(blur, inputs, src, w, h);
        long Ink(byte[] p) => Enumerable.Range(0, w * h).Sum(k => (long)p[k * 4 + 3]);
        int Alpha(byte[] p, int x, int y) => p[(y * w + x) * 4 + 3];
        // se extiende a los lados de la línea pero no por encima ni por debajo
        Assert.True(Alpha(outH, 40, 48) > 10, $"alfa a la izquierda {Alpha(outH, 40, 48)}");
        Assert.True(Alpha(outH, 56, 48) > 10);
        Assert.True(Alpha(outH, 48, 12) == 0, $"alfa por encima {Alpha(outH, 48, 12)}");
        Assert.True(Alpha(outH, 48, 48) < 200);
        // las colas más débiles del desenfoque redondean a 0 en 8 bits: se pierde algo de tinta, nunca se gana
        Assert.InRange(Ink(outH), Ink(src) * 0.9, Ink(src) * 1.01);
        // a 90° se extiende en vertical y no a los lados
        inputs.Values["ang"] = 90;
        var outV = Run(blur, inputs, src, w, h);
        Assert.True(Alpha(outV, 48, 12) > 10);
        Assert.True(Alpha(outV, 40, 48) == 0, $"alfa a la izquierda {Alpha(outV, 40, 48)}");
    }

    // ---------- AD-02.6 ----------

    [Fact]
    public void BloomBrightensOnlyAroundHighlights()
    {
        const int w = 80, h = 80;
        // círculo opaco gris medio con un punto blanco en el centro; fuera, transparente
        var src = new byte[w * h * 4];
        for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
            {
                var i = (y * w + x) * 4;
                var d = Math.Sqrt((x - 40) * (x - 40) + (y - 40) * (y - 40));
                if (d > 30) continue;
                var v = (byte)(d < 8 ? 255 : 90);
                src[i] = src[i + 1] = src[i + 2] = v;
                src[i + 3] = 255;
            }
        var bloom = Get("bloom");
        var inputs = Inputs(bloom);
        Assert.Equal(src, Run(bloom, inputs, src, w, h)); // intensidad 0: identidad
        inputs.Values["a"] = 100;
        inputs.Values["t"] = 70;
        inputs.Values["r"] = 10;
        var lit = Run(bloom, inputs, src, w, h);
        int R(byte[] p, int x, int y) => p[(y * w + x) * 4];
        int A(byte[] p, int x, int y) => p[(y * w + x) * 4 + 3];
        Assert.True(R(lit, 50, 40) > R(src, 50, 40) + 15, $"cerca de la luz: {R(src, 50, 40)} → {R(lit, 50, 40)}");
        Assert.True(R(lit, 66, 40) <= R(src, 66, 40) + 3, "lejos de la luz no cambia");
        // nada fuera de la forma
        for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
                if (A(src, x, y) == 0) Assert.Equal(0, A(lit, x, y));

        // una imagen sin luces sobre el umbral no cambia
        var dark = (byte[])src.Clone();
        for (var i = 0; i < dark.Length; i += 4) if (dark[i] == 255) dark[i] = dark[i + 1] = dark[i + 2] = 90;
        var same = Run(bloom, inputs, dark, w, h);
        Assert.True(Enumerable.Range(0, dark.Length).All(i => Math.Abs(dark[i] - same[i]) <= 1));
    }
}
