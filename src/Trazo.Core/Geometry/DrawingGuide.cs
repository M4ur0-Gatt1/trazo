using System.Numerics;

namespace Trazo.Core.Geometry;

public enum GuideKind
{
    None,
    Grid,
    Isometric,
    Perspective,
}

/// <summary>Guía de dibujo del lienzo (DG-01). Se guarda en el manifiesto del .trazo.</summary>
public sealed record DrawingGuide
{
    public GuideKind Kind { get; init; } = GuideKind.None;
    /// <summary>Separación de la cuadrícula en px del lienzo.</summary>
    public float Size { get; init; } = 100;
    /// <summary>Grosor de las líneas en px de pantalla.</summary>
    public float Thickness { get; init; } = 1;
    public float Opacity { get; init; } = 0.4f;
    /// <summary>Color en hexadecimal RRGGBB.</summary>
    public string Color { get; init; } = "00E5FF";
    public bool Assist { get; init; }
    /// <summary>Puntos de fuga (1 a 3) para la perspectiva.</summary>
    public IReadOnlyList<Vector2> VanishingPoints { get; init; } = [];

    public bool IsVisible => Kind != GuideKind.None && (Kind != GuideKind.Perspective || VanishingPoints.Count > 0);

    public static readonly float Tan30 = MathF.Tan(MathF.PI / 6);

    /// <summary>Segmentos de la guía recortados al lienzo, en coordenadas del lienzo.</summary>
    public List<(Vector2 A, Vector2 B)> Lines(float width, float height)
    {
        var lines = new List<(Vector2, Vector2)>();
        var size = Math.Max(4, Size);
        void Add(Vector2 point, Vector2 dir, bool ray = false)
        {
            if (Clip(point, dir, width, height, ray) is { } seg) lines.Add(seg);
        }
        switch (Kind)
        {
            case GuideKind.Grid:
                for (var x = size; x < width; x += size) lines.Add((new(x, 0), new(x, height)));
                for (var y = size; y < height; y += size) lines.Add((new(0, y), new(width, y)));
                break;
            case GuideKind.Isometric:
                for (var x = size; x < width; x += size) lines.Add((new(x, 0), new(x, height)));
                // familias a ±30°: cortan las verticales cada 2·tamaño·tan30, desfasadas media celda entre columnas
                var h = 2 * size * Tan30;
                var reach = width * Tan30;
                for (var b = -MathF.Ceiling(reach / h) * h; b < height + reach; b += h)
                {
                    Add(new(0, b), new(1, Tan30));
                    Add(new(0, b), new(1, -Tan30));
                }
                break;
            case GuideKind.Perspective:
                if (VanishingPoints.Count == 0) break;
                foreach (var vp in VanishingPoints)
                    for (var k = 0; k < 60; k++)
                    {
                        var a = k * MathF.PI / 30;
                        Add(vp, new(MathF.Cos(a), MathF.Sin(a)), ray: true);
                    }
                if (VanishingPoints.Count >= 2 && Vector2.Distance(VanishingPoints[0], VanishingPoints[1]) > 1e-3f)
                    Add(VanishingPoints[0], VanishingPoints[1] - VanishingPoints[0]);
                else Add(VanishingPoints[0], Vector2.UnitX);
                break;
        }
        return lines;
    }

    /// <summary>Recorta la recta (o la semirrecta si <paramref name="ray"/>) al rectángulo (Liang-Barsky).</summary>
    public static (Vector2, Vector2)? Clip(Vector2 p, Vector2 dir, float width, float height, bool ray = false)
    {
        double t0 = ray ? 0 : double.NegativeInfinity, t1 = double.PositiveInfinity;
        bool Edge(double denom, double num)
        {
            if (Math.Abs(denom) < 1e-12) return num >= 0;
            var t = num / denom;
            if (denom > 0) t1 = Math.Min(t1, t);
            else t0 = Math.Max(t0, t);
            return true;
        }
        if (!Edge(dir.X, width - p.X) || !Edge(-dir.X, p.X) || !Edge(dir.Y, height - p.Y) || !Edge(-dir.Y, p.Y)) return null;
        // un tramo de menos de medio píxel (una recta que roza una esquina) no se dibuja
        if (double.IsInfinity(t0) || double.IsInfinity(t1) || (t1 - t0) * dir.Length() < 0.5) return null;
        return (p + dir * (float)t0, p + dir * (float)t1);
    }

    /// <summary>Direcciones que admite el Asistente de dibujo desde el punto inicial del trazo.</summary>
    public List<Vector2> AssistDirections(Vector2 start)
    {
        var dirs = new List<Vector2>();
        switch (Kind)
        {
            case GuideKind.Grid:
                dirs.Add(Vector2.UnitX);
                dirs.Add(Vector2.UnitY);
                break;
            case GuideKind.Isometric:
                dirs.Add(Vector2.UnitY);
                dirs.Add(Vector2.Normalize(new Vector2(1, Tan30)));
                dirs.Add(Vector2.Normalize(new Vector2(1, -Tan30)));
                break;
            case GuideKind.Perspective:
                foreach (var vp in VanishingPoints)
                    if (Vector2.Distance(vp, start) > 1e-3f) dirs.Add(Vector2.Normalize(vp - start));
                if (VanishingPoints.Count == 1) dirs.Add(Vector2.UnitX);
                if (VanishingPoints.Count is 1 or 2) dirs.Add(Vector2.UnitY);
                break;
        }
        return dirs;
    }
}

/// <summary>
/// Asistente de dibujo (DG-01): el trazo espera en el punto inicial hasta superar el umbral, elige la dirección
/// permitida más parecida a ese primer movimiento y después proyecta cada punto sobre esa recta.
/// </summary>
public sealed class GuideAssist(DrawingGuide guide, float threshold)
{
    private Vector2 _start;
    private Vector2? _dir;

    public Vector2? Direction => _dir;

    public void Begin(Vector2 start)
    {
        _start = start;
        _dir = null;
    }

    public Vector2 Constrain(Vector2 p)
    {
        if (_dir is null)
        {
            var d = p - _start;
            if (d.Length() < threshold) return _start;
            var dirs = guide.AssistDirections(_start);
            if (dirs.Count == 0) return p;
            var n = Vector2.Normalize(d);
            _dir = dirs.MaxBy(v => MathF.Abs(Vector2.Dot(v, n)));
        }
        var dir = _dir.Value;
        return _start + dir * Vector2.Dot(p - _start, dir);
    }
}
