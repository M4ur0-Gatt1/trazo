namespace Trazo.Core.Brushes;

/// <summary>
/// Definición de un pincel. Los valores de presión van de 0 a 1;
/// <see cref="Spacing"/> es la distancia entre sellos como fracción del tamaño.
/// </summary>
public sealed class BrushPreset
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public string Category { get; internal set; } = "";
    public required TipShape Tip { get; init; }

    public float Size { get; set; } = 12;
    public float MinSize { get; set; } = 1;
    public float MaxSize { get; set; } = 120;

    /// <summary>Tamaño relativo con presión mínima (1 = la presión no cambia el tamaño).</summary>
    public float MinSizePressure { get; set; } = 0.15f;
    public float Spacing { get; set; } = 0.08f;
    public float Flow { get; set; } = 1;
    /// <summary>Cuánto reduce la presión baja la opacidad de cada sello.</summary>
    public float FlowPressure { get; set; }
    public float Jitter { get; set; }
    public float Scatter { get; set; }
    public float RotationRandom { get; set; }
    public bool RotationFollowsStroke { get; set; }
    public float Angle { get; set; }
    /// <summary>Aumento de tamaño al inclinar el lápiz.</summary>
    public float Tilt { get; set; }
    /// <summary>Suavizado 0–1.</summary>
    public float StreamLine { get; set; } = 0.3f;

    public float SizeAt(float baseSize, float pressure) =>
        baseSize * (MinSizePressure + (1 - MinSizePressure) * Math.Clamp(pressure, 0, 1));

    public float FlowAt(float pressure) =>
        Flow * (1 - FlowPressure + FlowPressure * Math.Clamp(pressure, 0, 1));

    public BrushPreset Clone() => (BrushPreset)MemberwiseClone();

    /// <summary>Copia los ajustes editables de otro pincel con el mismo id.</summary>
    public void CopySettingsFrom(BrushPreset o)
    {
        StreamLine = o.StreamLine;
        Spacing = o.Spacing;
        Flow = o.Flow;
        Jitter = o.Jitter;
        Scatter = o.Scatter;
        MinSizePressure = o.MinSizePressure;
        FlowPressure = o.FlowPressure;
    }
}
