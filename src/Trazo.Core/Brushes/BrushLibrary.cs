namespace Trazo.Core.Brushes;

public sealed record BrushCategory(string Name, IReadOnlyList<BrushPreset> Brushes);

/// <summary>Biblioteca de pinceles incluidos.</summary>
public sealed class BrushLibrary
{
    private const float Pi = MathF.PI;
    private readonly Dictionary<string, BrushPreset> _byId = [];

    public IReadOnlyList<BrushCategory> Categories { get; }

    public BrushLibrary()
    {
        Categories =
        [
            Cat("Bocetos",
                new() { Id = "pencil6b", Name = "Lápiz 6B", Tip = TipShape.Pencil, Size = 10, MaxSize = 80, Spacing = 0.07f, MinSizePressure = 0.45f, Flow = 0.75f, FlowPressure = 0.8f, RotationRandom = Pi, Tilt = 1.6f, StreamLine = 0.12f },
                new() { Id = "hb", Name = "Lápiz HB", Tip = TipShape.Pencil, Size = 4, MaxSize = 30, Spacing = 0.06f, MinSizePressure = 0.6f, Flow = 0.9f, FlowPressure = 0.7f, RotationRandom = Pi, Tilt = 1, StreamLine = 0.15f },
                new() { Id = "charcoal", Name = "Carboncillo", Tip = TipShape.Chalk, Size = 22, MaxSize = 160, Spacing = 0.1f, MinSizePressure = 0.5f, Flow = 0.85f, FlowPressure = 0.7f, RotationRandom = Pi, Jitter = 0.1f, Tilt = 1 }),
            Cat("Entintado",
                new() { Id = "studiopen", Name = "Pluma de estudio", Tip = TipShape.Round, Size = 7, MaxSize = 90, Spacing = 0.04f, MinSizePressure = 0.05f, StreamLine = 0.55f },
                new() { Id = "techpen", Name = "Bolígrafo técnico", Tip = TipShape.Round, Size = 4, MaxSize = 40, Spacing = 0.04f, MinSizePressure = 0.85f, StreamLine = 0.45f },
                new() { Id = "dryink", Name = "Tinta seca", Tip = TipShape.Chalk, Size = 12, MaxSize = 90, Spacing = 0.05f, MinSizePressure = 0.2f, RotationRandom = Pi, StreamLine = 0.35f },
                new() { Id = "marker", Name = "Rotulador", Tip = TipShape.Round, Size = 16, MaxSize = 120, Spacing = 0.04f, MinSizePressure = 0.9f, Flow = 0.55f, StreamLine = 0.3f }),
            Cat("Pintura",
                new() { Id = "roundbrush", Name = "Pincel redondo", Tip = TipShape.Round, Size = 30, MaxSize = 300, Spacing = 0.06f, MinSizePressure = 0.3f, Flow = 0.65f, FlowPressure = 0.5f, StreamLine = 0.25f },
                new() { Id = "gouache", Name = "Gouache", Tip = TipShape.Bristle, Size = 44, MaxSize = 320, Spacing = 0.05f, MinSizePressure = 0.45f, Flow = 0.55f, FlowPressure = 0.4f, RotationFollowsStroke = true, Angle = Pi / 2, StreamLine = 0.2f },
                new() { Id = "oil", Name = "Óleo", Tip = TipShape.Bristle, Size = 56, MaxSize = 400, Spacing = 0.035f, MinSizePressure = 0.6f, Flow = 0.9f, RotationFollowsStroke = true, Angle = Pi / 2, StreamLine = 0.2f },
                new() { Id = "watercolor", Name = "Acuarela", Tip = TipShape.Soft, Size = 70, MaxSize = 400, Spacing = 0.05f, MinSizePressure = 0.5f, Flow = 0.18f, FlowPressure = 0.6f, Jitter = 0.08f, StreamLine = 0.25f }),
            Cat("Aerógrafos",
                new() { Id = "softair", Name = "Aerógrafo suave", Tip = TipShape.Airbrush, Size = 140, MaxSize = 900, Spacing = 0.05f, MinSizePressure = 1, Flow = 0.1f, FlowPressure = 0.9f, StreamLine = 0.1f },
                new() { Id = "medair", Name = "Aerógrafo medio", Tip = TipShape.Soft, Size = 70, MaxSize = 600, Spacing = 0.05f, MinSizePressure = 0.8f, Flow = 0.22f, FlowPressure = 0.8f, StreamLine = 0.1f },
                new() { Id = "hardair", Name = "Aerógrafo duro", Tip = TipShape.Round, Size = 40, MaxSize = 500, Spacing = 0.05f, MinSizePressure = 1, Flow = 0.35f, FlowPressure = 0.8f, StreamLine = 0.1f },
                new() { Id = "spray", Name = "Spray", Tip = TipShape.Spray, Size = 90, MaxSize = 600, Spacing = 0.18f, MinSizePressure = 0.8f, Flow = 0.55f, FlowPressure = 0.6f, RotationRandom = Pi, Scatter = 0.12f, StreamLine = 0 }),
            Cat("Caligrafía",
                new() { Id = "monoline", Name = "Monolínea", Tip = TipShape.Round, Size = 8, MaxSize = 90, Spacing = 0.03f, MinSizePressure = 1, StreamLine = 0.65f },
                new() { Id = "calig", Name = "Pluma caligráfica", Tip = TipShape.Flat, Size = 22, MaxSize = 140, Spacing = 0.025f, MinSizePressure = 0.55f, Angle = -Pi / 4, StreamLine = 0.55f },
                new() { Id = "brushpen", Name = "Rotulador de pincel", Tip = TipShape.Round, Size = 16, MaxSize = 110, Spacing = 0.03f, MinSizePressure = 0.08f, StreamLine = 0.55f }),
            Cat("Texturas",
                new() { Id = "chalk", Name = "Tiza", Tip = TipShape.Chalk, Size = 34, MaxSize = 220, Spacing = 0.14f, MinSizePressure = 0.6f, FlowPressure = 0.5f, RotationRandom = Pi, Jitter = 0.15f, Scatter = 0.05f },
                new() { Id = "grain", Name = "Grano", Tip = TipShape.Pencil, Size = 70, MaxSize = 340, Spacing = 0.1f, MinSizePressure = 0.7f, Flow = 0.5f, FlowPressure = 0.6f, RotationRandom = Pi },
                new() { Id = "stipple", Name = "Punteado", Tip = TipShape.Round, Size = 6, MaxSize = 60, Spacing = 1.6f, MinSizePressure = 0.4f, Jitter = 0.5f, Scatter = 1.2f, StreamLine = 0 }),
        ];
    }

    private BrushCategory Cat(string name, params BrushPreset[] brushes)
    {
        foreach (var b in brushes)
        {
            b.Category = name;
            _byId.Add(b.Id, b);
        }
        return new BrushCategory(name, brushes);
    }

    public IEnumerable<BrushPreset> All => Categories.SelectMany(c => c.Brushes);

    public BrushPreset this[string id] => _byId[id];

    public bool TryGet(string id, out BrushPreset brush) => _byId.TryGetValue(id, out brush!);

    /// <summary>Valores originales de un pincel (para "Restablecer").</summary>
    public static BrushPreset Default(string id) => new BrushLibrary()[id];
}
