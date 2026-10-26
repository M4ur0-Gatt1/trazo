using Trazo.Core.Brushes;
using Trazo.Core.Colors;
using static Trazo.Core.Localization.Tr;

namespace Trazo.Core.Imaging;

/// <summary>
/// AD-02: ajustes que se expresan como tablas de 256 valores por canal (docs/specs/AD-02-adjustments.md). La GPU solo
/// aplica la tabla; la matemática vive aquí y se prueba sin GPU.
/// </summary>
public static class ToneTable
{
    public const int Size = 256;

    /// <summary>Tabla de 256 valores en [0,1] a partir de una función de [0,1] en [0,1].</summary>
    public static float[] From(Func<float, float> f)
    {
        var t = new float[Size];
        for (var i = 0; i < Size; i++) t[i] = Math.Clamp(f(i / (float)(Size - 1)), 0, 1);
        return t;
    }

    /// <summary>Valor de una tabla para v ∈ [0,1] con interpolación lineal, como el efecto de tabla de Direct2D.</summary>
    public static float Lookup(float[] table, float v)
    {
        var x = Math.Clamp(v, 0, 1) * (table.Length - 1);
        var i = (int)MathF.Floor(x);
        if (i >= table.Length - 1) return table[^1];
        return table[i] + (table[i + 1] - table[i]) * (x - i);
    }

    /// <summary>Aplica tablas R, G, B a píxeles RGBA sin premultiplicar (referencia de CPU para las pruebas).</summary>
    public static void Apply(Span<byte> rgba, float[] r, float[] g, float[] b)
    {
        for (var i = 0; i < rgba.Length; i += 4)
        {
            rgba[i] = ToByte(Lookup(r, rgba[i] / 255f));
            rgba[i + 1] = ToByte(Lookup(g, rgba[i + 1] / 255f));
            rgba[i + 2] = ToByte(Lookup(b, rgba[i + 2] / 255f));
        }
    }

    public static byte ToByte(float v) => (byte)Math.Round(Math.Clamp(v, 0, 1) * 255);

    /// <summary>Luminancia Rec. 709 de un color en [0,1].</summary>
    public static float Luma(float r, float g, float b) => 0.2126f * r + 0.7152f * g + 0.0722f * b;
}

/// <summary>Curvas de tono: una compuesta y una por canal. Sin puntos son la identidad.</summary>
public sealed record ToneCurves
{
    public DynamicsCurve Composite { get; init; } = DynamicsCurve.Identity;
    public DynamicsCurve Red { get; init; } = DynamicsCurve.Identity;
    public DynamicsCurve Green { get; init; } = DynamicsCurve.Identity;
    public DynamicsCurve Blue { get; init; } = DynamicsCurve.Identity;

    public bool IsIdentity => Composite.IsIdentity && Red.IsIdentity && Green.IsIdentity && Blue.IsIdentity;

    /// <summary>Curva de un canal (0 = compuesta, 1 = rojo, 2 = verde, 3 = azul).</summary>
    public DynamicsCurve this[int channel] => channel switch { 1 => Red, 2 => Green, 3 => Blue, _ => Composite };

    public ToneCurves With(int channel, DynamicsCurve c) => channel switch
    {
        1 => this with { Red = c },
        2 => this with { Green = c },
        3 => this with { Blue = c },
        _ => this with { Composite = c },
    };

    /// <summary>Tabla del canal de color (0 = R, 1 = G, 2 = B): primero la compuesta y después la del canal.</summary>
    public float[] Table(int rgb) => ToneTable.From(x => this[rgb + 1].Evaluate(Composite.Evaluate(x)));
}

public readonly record struct GradientStop(float Position, Rgba Color);

/// <summary>Degradado para el mapa de degradado: de 2 a 8 paradas en orden, interpolación lineal en sRGB.</summary>
public sealed record GradientMap(string Name, IReadOnlyList<GradientStop> Stops)
{
    public static bool IsValid(IReadOnlyList<GradientStop> stops)
    {
        if (stops.Count is < 2 or > 8) return false;
        for (var i = 0; i < stops.Count; i++)
        {
            var p = stops[i].Position;
            if (!float.IsFinite(p) || p < 0 || p > 1 || (i > 0 && p < stops[i - 1].Position)) return false;
        }
        return true;
    }

    /// <summary>Color en t ∈ [0,1], cada canal en [0,1].</summary>
    public (float R, float G, float B) Sample(float t)
    {
        t = Math.Clamp(t, 0, 1);
        var s = Stops;
        if (t <= s[0].Position) return Of(s[0].Color);
        if (t >= s[^1].Position) return Of(s[^1].Color);
        var k = 0;
        while (t > s[k + 1].Position) k++;
        var span = s[k + 1].Position - s[k].Position;
        var u = span <= 0 ? 1 : (t - s[k].Position) / span;
        var (r0, g0, b0) = Of(s[k].Color);
        var (r1, g1, b1) = Of(s[k + 1].Color);
        return (r0 + (r1 - r0) * u, g0 + (g1 - g0) * u, b0 + (b1 - b0) * u);
    }

    private static (float, float, float) Of(Rgba c) => (c.R / 255f, c.G / 255f, c.B / 255f);

    /// <summary>El mismo degradado al revés.</summary>
    public GradientMap Reversed() => this with { Stops = Stops.Reverse().Select(s => s with { Position = 1 - s.Position }).ToList() };

    /// <summary>Tabla del canal (0 = R, 1 = G, 2 = B) indexada por la luminancia.</summary>
    public float[] Table(int rgb) => ToneTable.From(t =>
    {
        var c = Sample(t);
        return rgb switch { 0 => c.R, 1 => c.G, _ => c.B };
    });

    private static GradientMap Of(string name, params (float P, string Hex)[] stops) =>
        new(name, stops.Select(s => new GradientStop(s.P, Rgba.TryParseHex(s.Hex, out var c) ? c : default)).ToList());

    /// <summary>Biblioteca. La propiedad se reconstruye para que los nombres sigan el idioma en uso (I18N-01).</summary>
    public static IReadOnlyList<GradientMap> Library =>
    [
        Of(T("Blanco y negro"), (0, "#000000"), (1, "#FFFFFF")),
        Of(T("Noche"), (0, "#0B0F2E"), (0.5f, "#1F6F8B"), (1, "#E8F4E4")),
        Of(T("Atardecer"), (0, "#2A0A3D"), (0.45f, "#C23B4E"), (0.75f, "#F49B42"), (1, "#FFF1B5")),
        Of(T("Neón"), (0, "#12002B"), (0.5f, "#FF2E9A"), (1, "#3EF7FF")),
        Of(T("Bosque"), (0, "#0E1A12"), (0.5f, "#4F7A3A"), (1, "#E9E3B4")),
        Of(T("Sepia antiguo"), (0, "#1E140C"), (0.6f, "#9C7350"), (1, "#F4E7CF")),
    ];

    /// <summary>Negro → color → blanco (duotono con el color actual).</summary>
    public static GradientMap FromColor(Rgba color) =>
        new(T("Color actual"), [new(0, new Rgba(0, 0, 0)), new(0.5f, color with { A = 255 }), new(1, new Rgba(255, 255, 255))]);
}

/// <summary>Desplazamientos de equilibrio de color de una zona tonal, cada uno en [-1,1]: + rojo, + verde, + azul.</summary>
public readonly record struct BalanceShift(float CyanRed, float MagentaGreen, float YellowBlue)
{
    public float this[int rgb] => rgb switch { 0 => CyanRed, 1 => MagentaGreen, _ => YellowBlue };
}

/// <summary>Equilibrio de color por sombras, medios tonos y luces, con pesos de Bernstein que suman 1.</summary>
public sealed record ColorBalance(BalanceShift Shadows, BalanceShift Midtones, BalanceShift Highlights)
{
    /// <summary>Desplazamiento máximo del canal con un control al extremo.</summary>
    public const float MaxShift = 0.3f;

    public static (float S, float M, float H) Weights(float v) => ((1 - v) * (1 - v), 2 * v * (1 - v), v * v);

    public float[] Table(int rgb) => ToneTable.From(v =>
    {
        var (s, m, h) = Weights(v);
        return v + MaxShift * (Shadows[rgb] * s + Midtones[rgb] * m + Highlights[rgb] * h);
    });
}
