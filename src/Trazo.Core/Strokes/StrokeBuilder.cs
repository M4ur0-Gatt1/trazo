using System.Numerics;
using Trazo.Core.Brushes;

namespace Trazo.Core.Strokes;

/// <summary>
/// Pipeline de trazo sin dependencias gráficas:
/// entrada → orden temporal → StreamLine → espaciado y dinámicas → sellos.
/// El renderizador solo dibuja los sellos que produce, por lo que el mismo
/// código se usa en la app y en las pruebas (BR-01, BR-02).
/// </summary>
public sealed class StrokeBuilder
{
    /// <summary>Radio de StreamLine en píxeles de pantalla con StreamLine = 1.</summary>
    public const float StreamLineScreenRadius = 26;

    private readonly BrushPreset _brush;
    private readonly StreamLineSmoother _smoother;
    private readonly StrokeStamper _stamper;
    private readonly List<InputPoint> _raw = [];
    private double _lastTime = double.NegativeInfinity;

    /// <param name="viewScale">Zoom actual: el radio de StreamLine es constante en pantalla.</param>
    /// <param name="seed">Semilla de la variación aleatoria: el mismo trazo con la misma semilla da los mismos sellos.</param>
    /// <param name="spacingOverride">Espaciado forzado (p. ej. difuminar usa sellos más separados).</param>
    public StrokeBuilder(BrushPreset brush, float size, float viewScale, int seed, float? spacingOverride = null)
    {
        _brush = brush;
        Size = size;
        Seed = seed;
        _smoother = new StreamLineSmoother(brush.StreamLine * StreamLineScreenRadius / Math.Max(1e-4f, viewScale));
        _stamper = new StrokeStamper(brush, size, spacingOverride, new Random(seed));
    }

    public float Size { get; }
    public int Seed { get; }
    public float SmoothingRadius => _smoother.Radius;
    public IReadOnlyList<InputPoint> Raw => _raw;
    public QuickShape? Shape { get; private set; }
    /// <summary>Muestras descartadas por llegar con una marca de tiempo anterior a la última aceptada.</summary>
    public int RejectedOutOfOrder { get; private set; }

    /// <summary>Añade una muestra. Devuelve false si se descartó (fuera de orden o forma activa sin cambios).</summary>
    public bool Add(InputPoint p, List<Dab> output)
    {
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
        if (_smoother.Push(p) is { } s) _stamper.LineTo(s, output);
        return true;
    }

    /// <summary>Termina el trazo: el StreamLine alcanza el último punto real.</summary>
    public void End(List<Dab> output)
    {
        if (Shape is not null || _raw.Count < 2 || _smoother.Radius <= 0) return;
        var last = _raw[^1];
        _stamper.LineTo(last with { Pressure = _smoother.Current?.Pressure ?? last.Pressure }, output);
    }

    /// <summary>Intenta convertir lo dibujado en una forma (QuickShape).</summary>
    public bool TrySnapShape(float viewScale)
    {
        if (Shape is not null) return false;
        Shape = QuickShape.Detect(_raw, viewScale);
        return Shape is not null;
    }

    /// <summary>Sellos completos de la forma actual (el llamador debe borrar lo dibujado antes).</summary>
    public void ShapeDabs(List<Dab> output)
    {
        if (Shape is null) return;
        _stamper.Reset();
        foreach (var v in Shape.Path()) _stamper.LineTo(new InputPoint(v.X, v.Y, Shape.Pressure), output);
    }

    /// <summary>Procesa una lista completa de muestras (útil para pruebas y reproducción).</summary>
    public static List<Dab> Build(BrushPreset brush, float size, float viewScale, int seed, IEnumerable<InputPoint> samples, out StrokeBuilder builder)
    {
        builder = new StrokeBuilder(brush, size, viewScale, seed);
        var dabs = new List<Dab>();
        foreach (var s in samples) builder.Add(s, dabs);
        builder.End(dabs);
        return dabs;
    }
}
