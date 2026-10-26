using static Trazo.Core.Localization.Tr;
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
            Cat(T("Bocetos"),
                new() { Id = "pencil6b", Name = T("Lápiz 6B"), Tip = TipShape.Pencil, Size = 10, MaxSize = 80, Spacing = 0.07f, MinSizePressure = 0.45f, Flow = 0.75f, FlowPressure = 0.8f, RotationRandom = Pi, Tilt = 1.6f, StreamLine = 0.12f, Grain = new() { Mode = GrainMode.Texturized, Kind = GrainKind.Paper, Depth = 0.75f, Scale = 0.12f }, TouchTaper = new() { Start = 1f, End = 2f, TipSize = 0.3f } },
                new() { Id = "hb", Name = T("Lápiz HB"), Tip = TipShape.Pencil, Size = 4, MaxSize = 30, Spacing = 0.06f, MinSizePressure = 0.6f, Flow = 0.9f, FlowPressure = 0.7f, RotationRandom = Pi, Tilt = 1, StreamLine = 0.15f, Grain = new() { Mode = GrainMode.Texturized, Kind = GrainKind.Paper, Depth = 0.6f, Scale = 0.12f }, TouchTaper = new() { Start = 1f, End = 2f, TipSize = 0.3f } },
                new() { Id = "charcoal", Name = T("Carboncillo"), Tip = TipShape.Chalk, Size = 22, MaxSize = 160, Spacing = 0.1f, MinSizePressure = 0.5f, Flow = 0.85f, FlowPressure = 0.7f, RotationRandom = Pi, Jitter = 0.1f, Tilt = 1, Grain = new() { Mode = GrainMode.Texturized, Kind = GrainKind.Charcoal, Depth = 0.8f, Scale = 0.4f } }),
            Cat(T("Entintado"),
                new() { Id = "studiopen", Name = T("Pluma de estudio"), Tip = TipShape.Round, Size = 7, MaxSize = 90, Spacing = 0.04f, MinSizePressure = 0.05f, StreamLine = 0.55f, PressureTaper = new() { Start = 1f, End = 2.5f, TipSize = 0.05f }, TouchTaper = new() { Start = 1.5f, End = 4f, TipSize = 0.05f } },
                new() { Id = "techpen", Name = T("Bolígrafo técnico"), Tip = TipShape.Round, Size = 4, MaxSize = 40, Spacing = 0.04f, MinSizePressure = 0.85f, StreamLine = 0.45f, TouchTaper = new() { Start = 0.5f, End = 0.5f, TipSize = 0.4f } },
                new() { Id = "dryink", Name = T("Tinta seca"), Tip = TipShape.Chalk, Size = 12, MaxSize = 90, Spacing = 0.05f, MinSizePressure = 0.2f, RotationRandom = Pi, StreamLine = 0.35f, Grain = new() { Mode = GrainMode.Texturized, Kind = GrainKind.Noise, Depth = 0.5f, Scale = 0.3f }, TouchTaper = new() { Start = 1f, End = 2f, TipSize = 0.1f } },
                new() { Id = "marker", Name = T("Rotulador"), Tip = TipShape.Round, Size = 16, MaxSize = 120, Spacing = 0.04f, MinSizePressure = 0.9f, Flow = 0.55f, StreamLine = 0.3f }),
            Cat(T("Pintura"),
                new() { Id = "roundbrush", Name = T("Pincel redondo"), Tip = TipShape.Round, Size = 30, MaxSize = 300, Spacing = 0.06f, MinSizePressure = 0.3f, Flow = 0.65f, FlowPressure = 0.5f, StreamLine = 0.25f },
                new() { Id = "gouache", Name = T("Gouache"), Tip = TipShape.Bristle, Size = 44, MaxSize = 320, Spacing = 0.05f, MinSizePressure = 0.45f, Flow = 0.55f, FlowPressure = 0.4f, RotationFollowsStroke = true, Angle = Pi / 2, StreamLine = 0.2f, Grain = new() { Mode = GrainMode.Texturized, Kind = GrainKind.Canvas, Depth = 0.35f, Scale = 0.4f }, Wet = new() { Pull = 0.35f, Charge = 0.85f, Dilution = 0.1f, Attack = 0.8f } },
                new() { Id = "oil", Name = T("Óleo"), Tip = TipShape.Bristle, Size = 56, MaxSize = 400, Spacing = 0.035f, MinSizePressure = 0.6f, Flow = 0.9f, RotationFollowsStroke = true, Angle = Pi / 2, StreamLine = 0.2f, Grain = new() { Mode = GrainMode.Texturized, Kind = GrainKind.Canvas, Depth = 0.2f, Scale = 0.5f }, Wet = new() { Pull = 0.5f, Charge = 0.92f, Attack = 0.9f } },
                new() { Id = "watercolor", Name = T("Acuarela"), Tip = TipShape.Soft, Size = 70, MaxSize = 400, Spacing = 0.05f, MinSizePressure = 0.5f, Flow = 0.18f, FlowPressure = 0.6f, Jitter = 0.08f, StreamLine = 0.25f, Grain = new() { Mode = GrainMode.Texturized, Kind = GrainKind.Paper, Depth = 0.35f, Scale = 0.8f }, Wet = new() { Pull = 0.25f, Charge = 0.7f, Dilution = 0.4f, Attack = 0.7f } }),
            Cat(T("Aerógrafos"),
                new() { Id = "softair", Name = T("Aerógrafo suave"), Tip = TipShape.Airbrush, Size = 140, MaxSize = 900, Spacing = 0.05f, MinSizePressure = 1, Flow = 0.1f, FlowPressure = 0.9f, StreamLine = 0.1f },
                new() { Id = "medair", Name = T("Aerógrafo medio"), Tip = TipShape.Soft, Size = 70, MaxSize = 600, Spacing = 0.05f, MinSizePressure = 0.8f, Flow = 0.22f, FlowPressure = 0.8f, StreamLine = 0.1f },
                new() { Id = "hardair", Name = T("Aerógrafo duro"), Tip = TipShape.Round, Size = 40, MaxSize = 500, Spacing = 0.05f, MinSizePressure = 1, Flow = 0.35f, FlowPressure = 0.8f, StreamLine = 0.1f },
                new() { Id = "spray", Name = T("Spray"), Tip = TipShape.Spray, Size = 90, MaxSize = 600, Spacing = 0.18f, MinSizePressure = 0.8f, Flow = 0.55f, FlowPressure = 0.6f, RotationRandom = Pi, Scatter = 0.12f, StreamLine = 0 }),
            Cat(T("Caligrafía"),
                new() { Id = "monoline", Name = T("Monolínea"), Tip = TipShape.Round, Size = 8, MaxSize = 90, Spacing = 0.03f, MinSizePressure = 1, StreamLine = 0.65f, TouchTaper = new() { Start = 0.5f, End = 0.5f, TipSize = 0.4f } },
                new() { Id = "calig", Name = T("Pluma caligráfica"), Tip = TipShape.Flat, Size = 22, MaxSize = 140, Spacing = 0.025f, MinSizePressure = 0.55f, Angle = -Pi / 4, StreamLine = 0.55f, TouchTaper = new() { Start = 1f, End = 2f, TipSize = 0.2f } },
                new() { Id = "brushpen", Name = T("Rotulador de pincel"), Tip = TipShape.Round, Size = 16, MaxSize = 110, Spacing = 0.03f, MinSizePressure = 0.08f, StreamLine = 0.55f, PressureTaper = new() { Start = 1f, End = 2f, TipSize = 0.05f }, TouchTaper = new() { Start = 2f, End = 4f, TipSize = 0.05f } }),
            Cat(T("Texturas"),
                new() { Id = "chalk", Name = T("Tiza"), Tip = TipShape.Chalk, Size = 34, MaxSize = 220, Spacing = 0.14f, MinSizePressure = 0.6f, FlowPressure = 0.5f, RotationRandom = Pi, Jitter = 0.15f, Scatter = 0.05f, Grain = new() { Mode = GrainMode.Texturized, Kind = GrainKind.Paper, Depth = 0.7f, Scale = 0.5f } },
                new() { Id = "grain", Name = T("Grano"), Tip = TipShape.Pencil, Size = 70, MaxSize = 340, Spacing = 0.1f, MinSizePressure = 0.7f, Flow = 0.5f, FlowPressure = 0.6f, RotationRandom = Pi, Grain = new() { Mode = GrainMode.Texturized, Kind = GrainKind.Noise, Depth = 0.9f, Scale = 0.3f } },
                new() { Id = "stipple", Name = T("Punteado"), Tip = TipShape.Round, Size = 6, MaxSize = 60, Spacing = 1.6f, MinSizePressure = 0.4f, Jitter = 0.5f, Scatter = 1.2f, StreamLine = 0 }),
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
