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

    /// <summary>Respuesta del tamaño a la presión (BR-06).</summary>
    public DynamicsCurve SizeCurve { get; set; } = DynamicsCurve.Identity;
    /// <summary>Respuesta del flujo a la presión (BR-06).</summary>
    public DynamicsCurve FlowCurve { get; set; } = DynamicsCurve.Identity;
    /// <summary>Cuánto limita la presión baja la opacidad máxima del trazo (BR-06). 0 = sin tope.</summary>
    public float OpacityPressure { get; set; }
    public DynamicsCurve OpacityCurve { get; set; } = DynamicsCurve.Identity;
    /// <summary>Mezcla húmeda (BR-04).</summary>
    public WetMixSettings Wet { get; set; } = WetMixSettings.Off;

    public float SizeAt(float baseSize, float pressure) =>
        baseSize * (MinSizePressure + (1 - MinSizePressure) * SizeCurve.Evaluate(pressure));

    public float FlowAt(float pressure) =>
        Flow * (1 - FlowPressure + FlowPressure * FlowCurve.Evaluate(pressure));

    /// <summary>Tope de opacidad del sello: el trazo acumulado no pasa de este valor.</summary>
    public float OpacityAt(float pressure) =>
        1 - OpacityPressure + OpacityPressure * OpacityCurve.Evaluate(pressure);

    public bool HasOpacityCap => OpacityPressure > 0;

    /// <summary>Grano independiente de la punta (BR-03).</summary>
    public GrainSettings Grain { get; set; } = GrainSettings.Off;
    /// <summary>Afinado de extremos con lápiz (dispositivos con presión).</summary>
    public TaperSettings PressureTaper { get; set; } = TaperSettings.None;
    /// <summary>Afinado de extremos con dedo o ratón (sin presión).</summary>
    public TaperSettings TouchTaper { get; set; } = TaperSettings.None;

    public TaperSettings TaperFor(bool hasPressure) => hasPressure ? PressureTaper : TouchTaper;

    // GrainSettings y TaperSettings son records inmutables: la copia superficial es segura
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
        Grain = o.Grain;
        PressureTaper = o.PressureTaper;
        TouchTaper = o.TouchTaper;
        SizeCurve = o.SizeCurve;
        FlowCurve = o.FlowCurve;
        OpacityPressure = o.OpacityPressure;
        OpacityCurve = o.OpacityCurve;
        Wet = o.Wet;
    }
}
