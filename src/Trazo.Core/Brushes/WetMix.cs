using System.Numerics;
using Trazo.Core.Strokes;

namespace Trazo.Core.Brushes;

/// <summary>Ajustes de mezcla húmeda (BR-04). Los valores por defecto la desactivan.</summary>
public sealed record WetMixSettings
{
    public static readonly WetMixSettings Off = new();

    /// <summary>Agua: aclara y vuelve más transparente (0–1).</summary>
    public float Dilution { get; init; }
    /// <summary>Pintura cargada: 1 = no se acaba (0–1).</summary>
    public float Charge { get; init; } = 1;
    /// <summary>Cuánta pintura se deposita (0–1).</summary>
    public float Attack { get; init; } = 1;
    /// <summary>Cuánto color del lienzo arrastra el pincel (0–1).</summary>
    public float Pull { get; init; }

    public bool IsActive => Dilution > 0 || Charge < 1 || Attack < 1 || Pull > 0;

    /// <summary>Longitud de secado L en px para un tamaño de pincel (∞ con carga 1).</summary>
    public float DryLength(float size) =>
        Charge >= 1 ? float.PositiveInfinity : 12 * size * Math.Max(0, Charge) / (1 - Charge);

    public float AlphaFactor => (1 - 0.8f * Math.Clamp(Dilution, 0, 1)) * (0.2f + 0.8f * Math.Clamp(Attack, 0, 1));
}

/// <summary>Color medio y alfa (sin premultiplicar) del lienzo bajo un sello.</summary>
public readonly record struct CanvasSample(Vector3 Color, float Alpha);

/// <summary>
/// Depósito del pincel húmedo (BR-04): mezcla el color con lo que hay en la capa y gasta la pintura con la distancia.
/// </summary>
public sealed class WetMixer(WetMixSettings settings, Vector3 brushColor, float size, Func<Dab, CanvasSample> sample)
{
    private Vector3 _reservoir = brushColor;

    public Vector3 Reservoir => _reservoir;

    /// <summary>Procesa un sello definitivo: avanza el depósito y devuelve el sello con su color y alfa.</summary>
    public Dab Process(Dab d)
    {
        var s = sample(d);
        var u = Math.Clamp(settings.Pull, 0, 1) * Math.Clamp(s.Alpha, 0, 1);
        if (u > 0) _reservoir += u * (s.Color - _reservoir);
        return Apply(d);
    }

    /// <summary>El sello con el depósito actual, sin avanzarlo (para la cola provisional).</summary>
    public Dab Peek(Dab d) => Apply(d);

    private Dab Apply(Dab d)
    {
        var paint = settings.Charge >= 1 ? 1 : MathF.Exp(-d.Distance / Math.Max(1e-3f, settings.DryLength(size)));
        return d with { Alpha = d.Alpha * settings.AlphaFactor * paint, Color = _reservoir };
    }
}
