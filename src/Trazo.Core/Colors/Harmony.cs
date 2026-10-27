using static Trazo.Core.Localization.Tr;

namespace Trazo.Core.Colors;

public enum HarmonyMode
{
    Complementary,
    SplitComplementary,
    Analogous,
    Triadic,
    Tetradic,
}

/// <summary>CO-02: colores en armonía sobre la rueda de tono HSB (docs/specs/CO-02-color-panel.md).</summary>
public static class ColorHarmony
{
    /// <summary>Desplazamientos de tono de los colores ligados, en grados, sin contar el principal.</summary>
    public static IReadOnlyList<double> Offsets(HarmonyMode mode) => mode switch
    {
        HarmonyMode.Complementary => [180],
        HarmonyMode.SplitComplementary => [150, 210],
        HarmonyMode.Analogous => [-30, 30],
        HarmonyMode.Triadic => [120, 240],
        _ => [60, 180, 240], // rectángulo
    };

    /// <summary>El principal y sus colores ligados, con la misma saturación y brillo.</summary>
    public static Hsv[] Colors(Hsv main, HarmonyMode mode) =>
        [Normalize(main), .. Offsets(mode).Select(o => Normalize(main with { H = main.H + o }))];

    private static Hsv Normalize(Hsv c) => c with { H = ((c.H % 360) + 360) % 360 };

    public static string Name(HarmonyMode mode) => mode switch
    {
        HarmonyMode.Complementary => T("Complementario"),
        HarmonyMode.SplitComplementary => T("Complementario dividido"),
        HarmonyMode.Analogous => T("Análogo"),
        HarmonyMode.Triadic => T("Triádico"),
        _ => T("Tetrádico"),
    };
}
