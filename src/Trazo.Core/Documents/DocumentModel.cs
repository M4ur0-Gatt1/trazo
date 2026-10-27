using static Trazo.Core.Localization.Tr;
namespace Trazo.Core.Documents;

public enum BlendMode
{
    Normal,
    Multiply,
    Darken,
    ColorBurn,
    Screen,
    Lighten,
    ColorDodge,
    Overlay,
    SoftLight,
    HardLight,
    Difference,
    Exclusion,
    Hue,
    Saturation,
    Color,
    Luminosity,
}

public static class BlendModes
{
    public static readonly IReadOnlyList<BlendMode> All = Enum.GetValues<BlendMode>();

    public static string DisplayName(this BlendMode m) => m switch
    {
        BlendMode.Normal => T("Normal"),
        BlendMode.Multiply => T("Multiplicar"),
        BlendMode.Darken => T("Oscurecer"),
        BlendMode.ColorBurn => T("Subexponer color"),
        BlendMode.Screen => T("Trama"),
        BlendMode.Lighten => T("Aclarar"),
        BlendMode.ColorDodge => T("Sobreexponer color"),
        BlendMode.Overlay => T("Superponer"),
        BlendMode.SoftLight => T("Luz suave"),
        BlendMode.HardLight => T("Luz fuerte"),
        BlendMode.Difference => T("Diferencia"),
        BlendMode.Exclusion => T("Exclusión"),
        BlendMode.Hue => T("Tono"),
        BlendMode.Saturation => T("Saturación"),
        BlendMode.Color => T("Color"),
        _ => T("Luminosidad"),
    };

    public static string ShortName(this BlendMode m) => m switch
    {
        BlendMode.Normal => "N",
        BlendMode.Multiply => "M",
        BlendMode.Darken => T("Os"),
        BlendMode.ColorBurn => T("Sc"),
        BlendMode.Screen => T("T"),
        BlendMode.Lighten => T("A"),
        BlendMode.ColorDodge => T("Sb"),
        BlendMode.Overlay => T("Su"),
        BlendMode.SoftLight => T("Ls"),
        BlendMode.HardLight => T("Lf"),
        BlendMode.Difference => "D",
        BlendMode.Exclusion => "E",
        BlendMode.Hue => T("To"),
        BlendMode.Saturation => T("Sa"),
        BlendMode.Color => "C",
        _ => "L",
    };

    /// <summary>Nombres de composición de Canvas 2D usados por la versión web (formato .trazo v1).</summary>
    public static BlendMode FromCssName(string? css) => css switch
    {
        "multiply" => BlendMode.Multiply,
        "darken" => BlendMode.Darken,
        "color-burn" => BlendMode.ColorBurn,
        "screen" => BlendMode.Screen,
        "lighten" => BlendMode.Lighten,
        "color-dodge" => BlendMode.ColorDodge,
        "overlay" => BlendMode.Overlay,
        "soft-light" => BlendMode.SoftLight,
        "hard-light" => BlendMode.HardLight,
        "difference" => BlendMode.Difference,
        "exclusion" => BlendMode.Exclusion,
        "hue" => BlendMode.Hue,
        "saturation" => BlendMode.Saturation,
        "color" => BlendMode.Color,
        "luminosity" => BlendMode.Luminosity,
        _ => BlendMode.Normal,
    };
}

/// <summary>Propiedades de una capa (sin píxeles).</summary>
public sealed record LayerProperties
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public string Name { get; init; } = T("Capa");
    public float Opacity { get; init; } = 1;
    public BlendMode Blend { get; init; } = BlendMode.Normal;
    public bool Visible { get; init; } = true;
    public bool AlphaLock { get; init; }
    public bool Clip { get; init; }
    /// <summary>Grupo al que pertenece (LY-03), o null.</summary>
    public string? GroupId { get; init; }
    /// <summary>LY-04: el relleno y la selección automática miran solo esta capa. Como mucho una por documento.</summary>
    public bool Reference { get; init; }
}

public sealed record CanvasPreset(string Name, int Width, int Height);

public static class CanvasLimits
{
    public const int MinSide = 16;
    /// <summary>Límite de textura de Direct3D 11 de nivel 11.</summary>
    public const int MaxSide = 16384;
    public const long MaxArea = 64L * 1024 * 1024;
    /// <summary>Presupuesto de memoria de vídeo para capas.</summary>
    public const long LayerBudgetBytes = 2L * 1024 * 1024 * 1024;

    // propiedad, no campo: los nombres se traducen con el idioma en uso (I18N-01)
    public static IReadOnlyList<CanvasPreset> Presets =>
    [
        new(T("Cuadrado"), 2048, 2048),
        new("4K", 3840, 2160),
        new(T("Full HD"), 1920, 1080),
        new(T("A4 · 300 ppp"), 2480, 3508),
        new(T("A3 · 300 ppp"), 3508, 4961),
        new(T("Carta · 300 ppp"), 2550, 3300),
        new(T("Historia (9:16)"), 1080, 1920),
        new(T("Publicación (4:5)"), 1080, 1350),
    ];

    /// <summary>Ajusta un tamaño pedido a los límites, manteniendo la proporción.</summary>
    public static (int Width, int Height) Fit(double w, double h)
    {
        w = Math.Clamp(Math.Round(w), MinSide, MaxSide);
        h = Math.Clamp(Math.Round(h), MinSide, MaxSide);
        if (w * h > MaxArea)
        {
            var k = Math.Sqrt(MaxArea / (w * h));
            w = Math.Floor(w * k);
            h = Math.Floor(h * k);
        }
        return ((int)w, (int)h);
    }

    public static int MaxLayers(int width, int height) =>
        (int)Math.Clamp(LayerBudgetBytes / ((long)width * height * 4), 4, 250);

    public static string NextLayerName(IEnumerable<string> existing)
    {
        var names = existing.ToHashSet();
        var n = 1;
        while (names.Contains(F("Capa {0}", n))) n++;
        return F("Capa {0}", n);
    }
}
