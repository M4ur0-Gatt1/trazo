using Trazo.Core.Brushes;
using Trazo.Core.Colors;
using Trazo.Core.Imaging;

namespace Trazo.Core.Tests;

/// <summary>AD-02 — Curvas, mapa de degradado y equilibrio de color (docs/specs/AD-02-adjustments.md), parte sin GPU.</summary>
public sealed class AD02_ToneMapsTests
{
    private static readonly float[] Identity = ToneTable.From(x => x);

    // ---------- AD-02.1 ----------

    [Fact]
    public void CurvesComposeAndStayMonotonic()
    {
        var none = new ToneCurves();
        Assert.True(none.IsIdentity);
        for (var c = 0; c < 3; c++) Assert.Equal(Identity, none.Table(c));

        // compuesta que aclara y canal rojo que invierte: el rojo es inv(aclarar(x)), los otros solo aclarar(x)
        var lighten = DynamicsCurve.From(new(0, 0), new(0.5f, 0.75f), new(1, 1));
        var invert = DynamicsCurve.From(new(0, 1), new(1, 0));
        var curves = new ToneCurves().With(0, lighten).With(1, invert);
        Assert.False(curves.IsIdentity);
        Assert.Same(invert, curves[1]);
        var red = curves.Table(0);
        var green = curves.Table(1);
        for (var i = 0; i < ToneTable.Size; i++)
        {
            var x = i / 255f;
            Assert.Equal(lighten.Evaluate(x), green[i], 5);
            Assert.Equal(invert.Evaluate(lighten.Evaluate(x)), red[i], 5);
        }
        Assert.Equal(0.75f, ToneTable.Lookup(green, 0.5f), 2);
        // puntos monótonos → tabla monótona (Fritsch-Carlson), sin saltos fuera de [0,1]
        var s = DynamicsCurve.From(new(0, 0), new(0.2f, 0.05f), new(0.4f, 0.6f), new(0.7f, 0.65f), new(1, 1));
        var t = new ToneCurves { Blue = s }.Table(2);
        for (var i = 1; i < t.Length; i++) Assert.True(t[i] >= t[i - 1] - 1e-6f, $"no monótona en {i}");
        Assert.All(t, v => Assert.InRange(v, 0, 1));
    }

    [Fact]
    public void LookupInterpolatesLikeTheGpu()
    {
        float[] t = [0, 1];
        Assert.Equal(0.25f, ToneTable.Lookup(t, 0.25f), 5);
        Assert.Equal(1, ToneTable.Lookup(t, 2));
        Assert.Equal(0, ToneTable.Lookup(t, -1));
        // aplicar en la CPU no toca el alfa
        byte[] px = [10, 20, 30, 77];
        var inv = ToneTable.From(x => 1 - x);
        ToneTable.Apply(px, inv, Identity, inv);
        Assert.Equal([245, 20, 225, 77], px);
    }

    // ---------- AD-02.2 ----------

    [Fact]
    public void GradientSamplesStopsAndInverts()
    {
        var g = new GradientMap("x", [new(0, new Rgba(0, 0, 0)), new(0.5f, new Rgba(255, 0, 0)), new(1, new Rgba(255, 255, 255))]);
        Assert.True(GradientMap.IsValid(g.Stops));
        Assert.Equal((0f, 0f, 0f), g.Sample(0));
        Assert.Equal((1f, 0f, 0f), g.Sample(0.5f));
        Assert.Equal((1f, 1f, 1f), g.Sample(1));
        var q = g.Sample(0.25f);
        Assert.Equal(0.5f, q.R, 4);
        Assert.Equal(0, q.G, 4);
        Assert.Equal((1f, 1f, 1f), g.Sample(7)); // fuera de rango: recorta
        // invertir da la vuelta
        var r = g.Reversed();
        foreach (var t in new[] { 0f, 0.1f, 0.33f, 0.5f, 0.9f, 1f })
        {
            var (a, b) = (g.Sample(1 - t), r.Sample(t));
            Assert.Equal(a.R, b.R, 4);
            Assert.Equal(a.G, b.G, 4);
            Assert.Equal(a.B, b.B, 4);
        }
        // tabla por canal
        Assert.Equal(1, g.Table(0)[255], 5);
        Assert.Equal(0, g.Table(1)[0], 5);

        Assert.False(GradientMap.IsValid([new(0, default)]));
        Assert.False(GradientMap.IsValid([new(0.6f, default), new(0.2f, default)]));
        Assert.Equal(6, GradientMap.Library.Count);
        foreach (var lib in GradientMap.Library) Assert.True(GradientMap.IsValid(lib.Stops), lib.Name);
        var duo = GradientMap.FromColor(new Rgba(0, 128, 255, 10));
        Assert.True(GradientMap.IsValid(duo.Stops));
        Assert.Equal(255, duo.Stops[1].Color.A);
        Assert.Equal((0f, 128 / 255f, 1f), duo.Sample(0.5f));
    }

    // ---------- AD-02.3 ----------

    [Fact]
    public void ColorBalanceWeightsAreTonal()
    {
        var zero = new ColorBalance(default, default, default);
        for (var c = 0; c < 3; c++) Assert.Equal(Identity, zero.Table(c));

        for (var v = 0f; v <= 1; v += 0.05f)
        {
            var (s, m, h) = ColorBalance.Weights(v);
            Assert.Equal(1, s + m + h, 5);
        }
        // el mismo desplazamiento en las tres zonas mueve el canal entero por igual
        var all = new BalanceShift(0.5f, 0, 0);
        var uniform = new ColorBalance(all, all, all).Table(0);
        for (var i = 0; i < 200; i++) Assert.Equal(Math.Min(1, i / 255f + 0.5f * ColorBalance.MaxShift), uniform[i], 4);

        // las sombras mueven mucho los oscuros y casi nada las luces; las luces, al revés (las luces se oscurecen
        // y las sombras se aclaran para no chocar con los límites 0 y 1)
        var shadows = new ColorBalance(new BalanceShift(0, 0, 1), default, default).Table(2);
        var highs = new ColorBalance(default, default, new BalanceShift(0, 0, -1)).Table(2);
        float Delta(float[] t, int i) => Math.Abs(t[i] - i / 255f);
        Assert.True(Delta(shadows, 25) > 0.2f && Delta(shadows, 230) < 0.01f);
        Assert.True(Delta(highs, 230) > 0.2f && Delta(highs, 25) < 0.01f);
        // los ejes son independientes: tocar azul no mueve el rojo
        Assert.Equal(Identity, new ColorBalance(new BalanceShift(0, 0, -1), default, default).Table(0));
    }
}
