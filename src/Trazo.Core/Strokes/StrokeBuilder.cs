using System.Numerics;
using Trazo.Core.Brushes;

namespace Trazo.Core.Strokes;

/// <summary>
/// Pipeline de trazo sin dependencias gráficas:
/// entrada → orden temporal → StreamLine → espaciado y dinámicas → afinado (taper) → sellos.
/// El renderizador solo dibuja los sellos que produce, por lo que el mismo
/// código se usa en la app y en las pruebas (BR-01, BR-02, BR-05).
/// </summary>
/// <remarks>
/// Afinado final: el final del trazo no se conoce hasta que se levanta el lápiz. Los sellos de
/// los últimos <c>End × diámetro</c> px se retienen en una <see cref="PendingTail"/> provisional
/// (el renderizador la muestra sin afinar) y se emiten afinados en <see cref="End"/>.
/// </remarks>
public sealed class StrokeBuilder
{
    /// <summary>Radio de StreamLine en píxeles de pantalla con StreamLine = 1.</summary>
    public const float StreamLineScreenRadius = 26;

    private readonly StreamLineSmoother _smoother;
    private readonly StrokeStamper _stamper;
    private readonly TaperSettings _taper;
    private readonly List<InputPoint> _raw = [];
    private readonly List<Dab> _fresh = [];
    private readonly List<Dab> _tail = []; // sellos sin afinar todavía (afinado final pendiente)
    private double _lastTime = double.NegativeInfinity;

    /// <param name="viewScale">Zoom actual: el radio de StreamLine es constante en pantalla.</param>
    /// <param name="seed">Semilla de la variación aleatoria: el mismo trazo con la misma semilla da los mismos sellos.</param>
    /// <param name="spacingOverride">Espaciado forzado (p. ej. difuminar usa sellos más separados).</param>
    /// <param name="hasPressure">El dispositivo informa presión (lápiz): usa el afinado de presión; si no, el táctil.</param>
    public StrokeBuilder(BrushPreset brush, float size, float viewScale, int seed, float? spacingOverride = null, bool hasPressure = true)
    {
        Size = size;
        Seed = seed;
        _taper = brush.TaperFor(hasPressure);
        _smoother = new StreamLineSmoother(brush.StreamLine * StreamLineScreenRadius / Math.Max(1e-4f, viewScale));
        _stamper = new StrokeStamper(brush, size, spacingOverride, new Random(seed));
    }

    public float Size { get; }
    public int Seed { get; }
    public float SmoothingRadius => _smoother.Radius;
    public TaperSettings Taper => _taper;
    public IReadOnlyList<InputPoint> Raw => _raw;
    public QuickShape? Shape { get; private set; }
    /// <summary>Muestras descartadas por llegar con una marca de tiempo anterior a la última aceptada.</summary>
    public int RejectedOutOfOrder { get; private set; }
    /// <summary>Sellos provisionales del final del trazo, con el afinado inicial aplicado pero no el final.</summary>
    public IReadOnlyList<Dab> PendingTail => _tail;
    /// <summary>Indica que <see cref="PendingTail"/> cambió en la última llamada.</summary>
    public bool TailChanged { get; private set; }

    private float EndLength => _taper.End * Size;

    /// <summary>Añade una muestra. Devuelve false si se descartó (fuera de orden o no finita).</summary>
    public bool Add(InputPoint p, List<Dab> output)
    {
        TailChanged = false;
        if (!float.IsFinite(p.X) || !float.IsFinite(p.Y)) return false;
        if (p.Time > 0)
        {
            if (p.Time < _lastTime)
            {
                RejectedOutOfOrder++;
                return false;
            }
            _lastTime = p.Time;
        }
        p = p with { Pressure = Math.Clamp(float.IsFinite(p.Pressure) ? p.Pressure : 1, 0, 1), Tilt = Math.Clamp(p.Tilt, 0, 1) };
        _raw.Add(p);
        if (Shape is not null)
        {
            Shape.Adjust(new Vector2(p.X, p.Y));
            return true;
        }
        if (_smoother.Push(p) is { } s) Stamp(s, output);
        return true;
    }

    private void Stamp(InputPoint p, List<Dab> output)
    {
        _fresh.Clear();
        _stamper.LineTo(p, _fresh);
        if (_fresh.Count == 0) return;
        if (EndLength <= 0)
        {
            foreach (var d in _fresh) output.Add(Apply(d, float.PositiveInfinity));
            return;
        }
        _tail.AddRange(_fresh);
        TailChanged = true;
        // lo que ya queda a más de EndLength de la cabeza no puede verse afectado por el afinado final
        var head = _tail[^1].Distance;
        var n = 0;
        while (n < _tail.Count && _tail[n].Distance < head - EndLength) n++;
        for (var i = 0; i < n; i++) output.Add(Apply(_tail[i], float.PositiveInfinity));
        _tail.RemoveRange(0, n);
    }

    /// <summary>Aplica el afinado a un sello sin afinar, conocida la distancia hasta el final (∞ si aún no se conoce).</summary>
    private Dab Apply(Dab d, float toEnd)
    {
        if (!_taper.IsActive) return d;
        var f = _taper.Factor(d.Distance, toEnd, Size);
        return d with { Size = Math.Max(0.4f, d.Size * _taper.SizeMultiplier(f)), Alpha = d.Alpha * _taper.AlphaMultiplier(f) };
    }

    /// <summary>Sellos provisionales de la cola tal como se ven antes de levantar el lápiz.</summary>
    public void TailPreview(List<Dab> output)
    {
        foreach (var d in _tail) output.Add(Apply(d, float.PositiveInfinity));
    }

    /// <summary>Termina el trazo: el StreamLine alcanza el último punto real y se emite la cola con el afinado final.</summary>
    public void End(List<Dab> output)
    {
        TailChanged = _tail.Count > 0;
        if (Shape is not null) return;
        if (_raw.Count >= 2 && _smoother.Radius > 0)
        {
            var last = _raw[^1];
            Stamp(last with { Pressure = _smoother.Current?.Pressure ?? last.Pressure }, output);
        }
        var total = _stamper.Distance;
        foreach (var d in _tail) output.Add(Apply(d, total - d.Distance));
        _tail.Clear();
    }

    /// <summary>Intenta convertir lo dibujado en una forma (QuickShape).</summary>
    public bool TrySnapShape(float viewScale)
    {
        if (Shape is not null) return false;
        Shape = QuickShape.Detect(_raw, viewScale);
        if (Shape is null) return false;
        _tail.Clear();
        return true;
    }

    /// <summary>Sellos completos de la forma actual, con los dos afinados (el llamador debe borrar lo dibujado antes).</summary>
    public void ShapeDabs(List<Dab> output)
    {
        if (Shape is null) return;
        _stamper.Reset();
        _fresh.Clear();
        foreach (var v in Shape.Path()) _stamper.LineTo(new InputPoint(v.X, v.Y, Shape.Pressure), _fresh);
        var total = _stamper.Distance;
        foreach (var d in _fresh) output.Add(Apply(d, total - d.Distance));
    }

    /// <summary>Procesa una lista completa de muestras (útil para pruebas y reproducción).</summary>
    public static List<Dab> Build(BrushPreset brush, float size, float viewScale, int seed, IEnumerable<InputPoint> samples, out StrokeBuilder builder, bool hasPressure = true)
    {
        builder = new StrokeBuilder(brush, size, viewScale, seed, hasPressure: hasPressure);
        var dabs = new List<Dab>();
        foreach (var s in samples) builder.Add(s, dabs);
        builder.End(dabs);
        return dabs;
    }
}
