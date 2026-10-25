namespace Trazo.Rendering;

/// <summary>Parámetros de los efectos de presentación del lienzo, acotados a los rangos que admite Direct2D.</summary>
public static class ViewEffects
{
    /// <summary>Desenfoque máximo que admite el efecto de sombra de Direct2D.</summary>
    public const float MaxShadowBlur = 250;

    /// <summary>
    /// Desenfoque de la sombra del lienzo en unidades del documento para que en pantalla mida ~12 px.
    /// Con zoom muy alejado el valor crudo (12 / zoom) supera el máximo de Direct2D y el efecto lanza
    /// una excepción que dejaba el lienzo sin redibujar (error encontrado el 2026-10-25).
    /// </summary>
    public static float ShadowBlur(float viewScale) =>
        float.IsFinite(viewScale) && viewScale > 0 ? Math.Clamp(12 / viewScale, 0, MaxShadowBlur) : MaxShadowBlur;
}
