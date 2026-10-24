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
        BlendMode.Normal => "Normal",
        BlendMode.Multiply => "Multiplicar",
        BlendMode.Darken => "Oscurecer",
        BlendMode.ColorBurn => "Subexponer color",
        BlendMode.Screen => "Trama",
        BlendMode.Lighten => "Aclarar",
        BlendMode.ColorDodge => "Sobreexponer color",
        BlendMode.Overlay => "Superponer",
        BlendMode.SoftLight => "Luz suave",
        BlendMode.HardLight => "Luz fuerte",
        BlendMode.Difference => "Diferencia",
        BlendMode.Exclusion => "Exclusión",
        BlendMode.Hue => "Tono",
        BlendMode.Saturation => "Saturación",
        BlendMode.Color => "Color",
        _ => "Luminosidad",
    };

    public static string ShortName(this BlendMode m) => m switch
    {
        BlendMode.Normal => "N",
        BlendMode.Multiply => "M",
        BlendMode.Darken => "Os",
        BlendMode.ColorBurn => "Sc",
        BlendMode.Screen => "T",
        BlendMode.Lighten => "A",
        BlendMode.ColorDodge => "Sb",
        BlendMode.Overlay => "Su",
        BlendMode.SoftLight => "Ls",
        BlendMode.HardLight => "Lf",
        BlendMode.Difference => "D",
        BlendMode.Exclusion => "E",
        BlendMode.Hue => "To",
        BlendMode.Saturation => "Sa",
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
    public string Name { get; init; } = "Capa";
    public float Opacity { get; init; } = 1;
    public BlendMode Blend { get; init; } = BlendMode.Normal;
    public bool Visible { get; init; } = true;
    public bool AlphaLock { get; init; }
    public bool Clip { get; init; }
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

    public static readonly IReadOnlyList<CanvasPreset> Presets =
    [
        new("Cuadrado", 2048, 2048),
        new("4K", 3840, 2160),
        new("Full HD", 1920, 1080),
        new("A4 · 300 ppp", 2480, 3508),
        new("A3 · 300 ppp", 3508, 4961),
        new("Carta · 300 ppp", 2550, 3300),
        new("Historia (9:16)", 1080, 1920),
        new("Publicación (4:5)", 1080, 1350),
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
        while (names.Contains($"Capa {n}")) n++;
        return $"Capa {n}";
    }
}
