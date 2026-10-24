using Trazo.Core.Brushes;

namespace Trazo.Core.Strokes;

/// <summary>Muestra de entrada en coordenadas del documento. <see cref="Time"/> en milisegundos (0 = sin marca de tiempo).</summary>
public readonly record struct InputPoint(float X, float Y, float Pressure = 1, float Tilt = 0, double Time = 0);

/// <summary>Un sello del pincel: posición, diámetro, opacidad, giro (radianes), distancia recorrida desde el inicio (px) y dirección de avance (radianes).</summary>
public readonly record struct Dab(float X, float Y, float Size, float Alpha, float Rotation, float Distance = 0, float TravelAngle = 0);

public static class PressureCurve
{
    /// <summary>Curva gamma: &lt;1 más sensible, &gt;1 más dura.</summary>
    public static float Apply(float pressure, float gamma) =>
        MathF.Pow(Math.Clamp(pressure, 0.02f, 1), Math.Clamp(gamma, 0.2f, 4));
}

/// <summary>
/// StreamLine estilo "cuerda": el punto suavizado solo avanza cuando la entrada
/// se aleja más que <see cref="Radius"/>, lo que elimina el temblor de la mano.
/// </summary>
public sealed class StreamLineSmoother(float radius)
{
    private InputPoint? _current;

    public float Radius { get; } = Math.Max(0, radius);
    public InputPoint? Current => _current;

    /// <summary>Devuelve el nuevo punto suavizado, o null si no hubo avance.</summary>
    public InputPoint? Push(InputPoint p)
    {
        if (_current is not { } c)
        {
            _current = p;
            return p;
        }
        float dx = p.X - c.X, dy = p.Y - c.Y, d = MathF.Sqrt(dx * dx + dy * dy);
        if (d <= Radius) return null;
        var f = Radius > 0 ? (d - Radius) / d : 1;
        var next = new InputPoint(c.X + dx * f, c.Y + dy * f, p.Pressure, p.Tilt);
        _current = next;
        return next;
    }
}

/// <summary>
/// Convierte una polilínea de entrada en sellos equiespaciados, interpolando
/// presión e inclinación, con variación, dispersión y giro según el pincel.
/// </summary>
public sealed class StrokeStamper
{
    private readonly BrushPreset _brush;
    private readonly float _baseSize;
    private readonly float _spacing;
    private readonly Random _rng;
    private InputPoint? _last;
    private float _carry;
    private float _distance;

    public StrokeStamper(BrushPreset brush, float baseSize, float? spacingOverride = null, Random? rng = null)
    {
        _brush = brush;
        _baseSize = baseSize;
        _spacing = spacingOverride ?? brush.Spacing;
        _rng = rng ?? Random.Shared;
    }

    public InputPoint? Last => _last;

    public void Reset()
    {
        _last = null;
        _carry = 0;
        _distance = 0;
    }

    /// <summary>Longitud recorrida hasta la última muestra, en px.</summary>
    public float Distance => _distance;

    private float Step(float pressure) => Math.Max(0.35f, _brush.SizeAt(_baseSize, pressure) * _spacing);

    public void LineTo(InputPoint p, List<Dab> output)
    {
        if (_last is not { } a)
        {
            output.Add(MakeDab(p.X, p.Y, p.Pressure, p.Tilt, 0, 0));
            _carry = Step(p.Pressure);
            _last = p;
            return;
        }
        float dx = p.X - a.X, dy = p.Y - a.Y, len = MathF.Sqrt(dx * dx + dy * dy);
        if (len < 1e-3f) return;
        var angle = MathF.Atan2(dy, dx);
        var t = _carry;
        while (t <= len)
        {
            var u = t / len;
            var pr = a.Pressure + (p.Pressure - a.Pressure) * u;
            output.Add(MakeDab(a.X + dx * u, a.Y + dy * u, pr, a.Tilt + (p.Tilt - a.Tilt) * u, angle, _distance + t));
            t += Step(pr);
        }
        _carry = t - len;
        _distance += len;
        _last = p;
    }

    private Dab MakeDab(float x, float y, float pressure, float tilt, float travelAngle, float distance)
    {
        var b = _brush;
        var size = b.SizeAt(_baseSize, pressure);
        if (b.Tilt > 0 && tilt > 0) size *= 1 + b.Tilt * tilt * tilt;
        if (b.Jitter > 0) size *= 1 + ((float)_rng.NextDouble() * 2 - 1) * b.Jitter;
        size = Math.Max(0.4f, size);
        if (b.Scatter > 0)
        {
            var r = (float)_rng.NextDouble() * b.Scatter * size * 2;
            var th = (float)_rng.NextDouble() * MathF.Tau;
            x += MathF.Cos(th) * r;
            y += MathF.Sin(th) * r;
        }
        var alpha = b.FlowAt(pressure);
        if (size < 1.5f) alpha *= size / 1.5f;
        var rot = b.Angle + (b.RotationFollowsStroke ? travelAngle : 0)
                  + (b.RotationRandom > 0 ? ((float)_rng.NextDouble() * 2 - 1) * b.RotationRandom : 0);
        return new Dab(x, y, size, alpha, rot, distance, travelAngle);
    }
}

public enum SymmetryMode
{
    None,
    Vertical,
    Horizontal,
    Quadrant,
    Radial,
}

public static class Symmetry
{
    public const int RadialSegments = 8;

    /// <summary>Añade el sello y sus reflejos alrededor del centro (cx, cy).</summary>
    public static void Expand(Dab d, SymmetryMode mode, float cx, float cy, List<Dab> output)
    {
        output.Add(d);
        switch (mode)
        {
            case SymmetryMode.Vertical:
                output.Add(d with { X = 2 * cx - d.X, Rotation = MathF.PI - d.Rotation });
                break;
            case SymmetryMode.Horizontal:
                output.Add(d with { Y = 2 * cy - d.Y, Rotation = -d.Rotation });
                break;
            case SymmetryMode.Quadrant:
                output.Add(d with { X = 2 * cx - d.X, Rotation = MathF.PI - d.Rotation });
                output.Add(d with { Y = 2 * cy - d.Y, Rotation = -d.Rotation });
                output.Add(d with { X = 2 * cx - d.X, Y = 2 * cy - d.Y, Rotation = d.Rotation + MathF.PI });
                break;
            case SymmetryMode.Radial:
                float dx = d.X - cx, dy = d.Y - cy;
                for (var i = 1; i < RadialSegments; i++)
                {
                    var t = i * MathF.Tau / RadialSegments;
                    float c = MathF.Cos(t), s = MathF.Sin(t);
                    output.Add(d with { X = cx + dx * c - dy * s, Y = cy + dx * s + dy * c, Rotation = d.Rotation + t });
                }
                break;
        }
    }
}
