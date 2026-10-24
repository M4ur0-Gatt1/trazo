namespace Trazo.Core.Brushes;

/// <summary>
/// Afinado artificial de los extremos del trazo (BR-05). Las longitudes se expresan en
/// diámetros del pincel, de modo que el afinado mantiene su forma al cambiar el tamaño.
/// Ver docs/specs/BR-05-taper.md para la correspondencia con los parámetros de Procreate.
/// </summary>
public sealed record TaperSettings
{
    public static readonly TaperSettings None = new();

    /// <summary>Longitud del afinado inicial, en diámetros (0–30).</summary>
    public float Start { get; init; }
    /// <summary>Longitud del afinado final, en diámetros (0–30).</summary>
    public float End { get; init; }
    /// <summary>Tamaño relativo en el extremo (0 = punta, 1 = sin afinar el tamaño).</summary>
    public float TipSize { get; init; } = 0.1f;
    /// <summary>Opacidad relativa en el extremo (0 = transparente, 1 = sin afinar la opacidad).</summary>
    public float TipOpacity { get; init; } = 1;
    /// <summary>Forma del perfil, 0–1: 0 = cóncavo (punta larga y fina), 0,5 = lineal, 1 = convexo (punta roma).</summary>
    public float Shape { get; init; } = 0.5f;

    public bool IsActive => Start > 0 || End > 0;

    /// <summary>Exponente del perfil: 0 → 3 (cóncavo), 0,5 → 1 (lineal), 1 → 1/3 (convexo).</summary>
    public float Exponent => MathF.Pow(3, 1 - 2 * Math.Clamp(Shape, 0, 1));

    /// <summary>Factor 0–1 (1 = sin afinar) para una distancia desde el inicio y otra hasta el final, en px.</summary>
    public float Factor(float fromStart, float toEnd, float diameter)
    {
        var d = Math.Max(1e-3f, diameter);
        var fs = Start > 0 ? Math.Clamp(fromStart / (Start * d), 0, 1) : 1;
        var fe = End > 0 ? Math.Clamp(toEnd / (End * d), 0, 1) : 1;
        // en un trazo más corto que los dos afinados manda el extremo más cercano (mínimo)
        return MathF.Pow(Math.Min(fs, fe), Exponent);
    }

    public float SizeMultiplier(float factor) => TipSize + (1 - TipSize) * factor;
    public float AlphaMultiplier(float factor) => TipOpacity + (1 - TipOpacity) * factor;
}
