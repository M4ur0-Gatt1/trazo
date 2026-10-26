using System.Numerics;
using static Trazo.Core.Localization.Tr;

namespace Trazo.Core.Strokes;

public enum ShapeKind
{
    Line,
    Circle,
    Ellipse,
    Polyline,
}

/// <summary>
/// Forma reconocida por QuickShape. Puede seguir ajustándose mientras el lápiz
/// permanece apoyado (<see cref="Adjust"/>).
/// </summary>
public sealed class QuickShape
{
    private float _rx0, _ry0, _d0;

    public ShapeKind Kind { get; private init; }
    public Vector2 A { get; private set; }
    public Vector2 B { get; private set; }
    public Vector2 Center { get; private init; }
    public float RadiusX { get; private set; }
    public float RadiusY { get; private set; }
    public IReadOnlyList<Vector2> Points { get; private init; } = [];
    /// <summary>Presión media del trazo original, usada para redibujar la forma.</summary>
    public float Pressure { get; private init; } = 1;

    public string DisplayName => Kind switch
    {
        ShapeKind.Line => T("Línea"),
        ShapeKind.Circle => T("Círculo"),
        ShapeKind.Ellipse => T("Elipse"),
        _ => T("Polilínea"),
    };

    /// <summary>Reconoce una forma. <paramref name="viewScale"/> es el zoom actual (px de pantalla por px de documento).</summary>
    public static QuickShape? Detect(IReadOnlyList<InputPoint> pts, float viewScale = 1)
    {
        if (pts.Count < 4) return null;
        float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue, psum = 0;
        foreach (var p in pts)
        {
            minX = Math.Min(minX, p.X); minY = Math.Min(minY, p.Y);
            maxX = Math.Max(maxX, p.X); maxY = Math.Max(maxY, p.Y);
            psum += p.Pressure;
        }
        var diag = MathF.Sqrt((maxX - minX) * (maxX - minX) + (maxY - minY) * (maxY - minY));
        if (diag * viewScale < 24) return null;

        var a = new Vector2(pts[0].X, pts[0].Y);
        var b = new Vector2(pts[^1].X, pts[^1].Y);
        var chord = Vector2.Distance(a, b);
        float maxDev = 0;
        if (chord > 0)
            foreach (var p in pts)
                maxDev = Math.Max(maxDev, DistanceToLine(new Vector2(p.X, p.Y), a, b));
        var pressure = Math.Max(0.35f, psum / pts.Count);

        if (chord > 0 && maxDev < Math.Max(6 / viewScale, chord * 0.08f))
            return new QuickShape { Kind = ShapeKind.Line, A = a, B = b, Pressure = pressure };

        if (chord < diag * 0.3f)
        {
            var c = new Vector2((minX + maxX) / 2, (minY + maxY) / 2);
            float rx = (maxX - minX) / 2, ry = (maxY - minY) / 2;
            var circle = MathF.Abs(rx - ry) / Math.Max(rx, ry) < 0.15f;
            if (circle) rx = ry = (rx + ry) / 2;
            return new QuickShape
            {
                Kind = circle ? ShapeKind.Circle : ShapeKind.Ellipse,
                Center = c, RadiusX = rx, RadiusY = ry, _rx0 = rx, _ry0 = ry,
                _d0 = Math.Max(1, Vector2.Distance(c, b)), Pressure = pressure,
            };
        }

        var poly = Simplify(pts.Select(p => new Vector2(p.X, p.Y)).ToList(), diag * 0.035f);
        return new QuickShape { Kind = ShapeKind.Polyline, Points = poly, Pressure = pressure };
    }

    /// <summary>Ajusta la forma al seguir moviendo el lápiz: mueve el extremo de la línea o escala la elipse.</summary>
    public void Adjust(Vector2 p)
    {
        switch (Kind)
        {
            case ShapeKind.Line:
                B = p;
                break;
            case ShapeKind.Circle or ShapeKind.Ellipse:
                var k = Vector2.Distance(Center, p) / _d0;
                RadiusX = _rx0 * k;
                RadiusY = _ry0 * k;
                break;
        }
    }

    public IReadOnlyList<Vector2> Path()
    {
        switch (Kind)
        {
            case ShapeKind.Line:
                return [A, B];
            case ShapeKind.Polyline:
                return Points;
            default:
                var n = Math.Max(48, (int)MathF.Ceiling(Math.Max(RadiusX, RadiusY) * 0.6f));
                var list = new List<Vector2>(n + 1);
                for (var i = 0; i <= n; i++)
                {
                    var t = i * MathF.Tau / n;
                    list.Add(new Vector2(Center.X + MathF.Cos(t) * RadiusX, Center.Y + MathF.Sin(t) * RadiusY));
                }
                return list;
        }
    }

    internal static float DistanceToLine(Vector2 p, Vector2 a, Vector2 b)
    {
        var len = Vector2.Distance(a, b);
        if (len < 1e-5f) return Vector2.Distance(a, p);
        return MathF.Abs((b.X - a.X) * (a.Y - p.Y) - (a.X - p.X) * (b.Y - a.Y)) / len;
    }

    /// <summary>Ramer–Douglas–Peucker.</summary>
    public static List<Vector2> Simplify(IReadOnlyList<Vector2> pts, float epsilon)
    {
        if (pts.Count < 3) return [.. pts];
        var keep = new bool[pts.Count];
        keep[0] = keep[^1] = true;
        var stack = new Stack<(int, int)>();
        stack.Push((0, pts.Count - 1));
        while (stack.Count > 0)
        {
            var (s, e) = stack.Pop();
            float max = 0;
            var idx = -1;
            for (var i = s + 1; i < e; i++)
            {
                var d = DistanceToLine(pts[i], pts[s], pts[e]);
                if (d > max) { max = d; idx = i; }
            }
            if (idx >= 0 && max > epsilon)
            {
                keep[idx] = true;
                stack.Push((s, idx));
                stack.Push((idx, e));
            }
        }
        var outList = new List<Vector2>();
        for (var i = 0; i < pts.Count; i++)
            if (keep[i]) outList.Add(pts[i]);
        return outList;
    }
}
