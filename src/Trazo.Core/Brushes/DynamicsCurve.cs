namespace Trazo.Core.Brushes;

/// <summary>Punto de control de una curva de dinámica, en [0,1]².</summary>
public readonly record struct CurvePoint(float X, float Y);

/// <summary>
/// Curva de respuesta a la presión (BR-06): interpolación cúbica monótona de Fritsch-Carlson entre
/// 2 y 8 puntos de control con x estrictamente creciente. Sin puntos es la identidad.
/// </summary>
public sealed record DynamicsCurve
{
    public const int MaxPoints = 8;
    public static readonly DynamicsCurve Identity = new();

    private float[]? _slopes;
    private readonly IReadOnlyList<CurvePoint> _points = [];

    public IReadOnlyList<CurvePoint> Points
    {
        get => _points;
        init
        {
            _points = value ?? [];
            _slopes = null;
        }
    }

    public bool IsIdentity => Points.Count == 0;

    /// <summary>Crea una curva validando los puntos; lanza ArgumentException si no son válidos.</summary>
    public static DynamicsCurve From(params CurvePoint[] points)
    {
        if (!IsValid(points)) throw new ArgumentException("Los puntos deben estar en [0,1] con x estrictamente creciente (2 a 8).", nameof(points));
        return new DynamicsCurve { Points = points };
    }

    public static bool IsValid(IReadOnlyList<CurvePoint> pts)
    {
        if (pts.Count == 0) return true;
        if (pts.Count < 2 || pts.Count > MaxPoints) return false;
        for (var i = 0; i < pts.Count; i++)
        {
            var p = pts[i];
            if (!float.IsFinite(p.X) || !float.IsFinite(p.Y) || p.X < 0 || p.X > 1 || p.Y < 0 || p.Y > 1) return false;
            if (i > 0 && p.X <= pts[i - 1].X) return false;
        }
        return true;
    }

    /// <summary>Valor de la curva en x ∈ [0,1] (fuera se recorta). Una curva no válida se trata como identidad.</summary>
    public float Evaluate(float x)
    {
        x = Math.Clamp(float.IsFinite(x) ? x : 0, 0, 1);
        var pts = Points;
        if (pts.Count == 0 || !IsValid(pts)) return x;
        if (x <= pts[0].X) return pts[0].Y;
        if (x >= pts[^1].X) return pts[^1].Y;
        var m = _slopes ??= Slopes(pts);
        var k = 0;
        while (x > pts[k + 1].X) k++;
        var h = pts[k + 1].X - pts[k].X;
        var t = (x - pts[k].X) / h;
        float t2 = t * t, t3 = t2 * t;
        var y = (2 * t3 - 3 * t2 + 1) * pts[k].Y + (t3 - 2 * t2 + t) * h * m[k]
              + (-2 * t3 + 3 * t2) * pts[k + 1].Y + (t3 - t2) * h * m[k + 1];
        return Math.Clamp(y, 0, 1);
    }

    // Pendientes de Fritsch-Carlson: monótonas allí donde los datos lo son
    private static float[] Slopes(IReadOnlyList<CurvePoint> p)
    {
        var n = p.Count;
        var d = new float[n - 1];
        for (var i = 0; i < n - 1; i++) d[i] = (p[i + 1].Y - p[i].Y) / (p[i + 1].X - p[i].X);
        var m = new float[n];
        m[0] = d[0];
        m[n - 1] = d[n - 2];
        for (var i = 1; i < n - 1; i++) m[i] = d[i - 1] * d[i] <= 0 ? 0 : (d[i - 1] + d[i]) / 2;
        for (var i = 0; i < n - 1; i++)
        {
            if (d[i] == 0)
            {
                m[i] = m[i + 1] = 0;
                continue;
            }
            var a = m[i] / d[i];
            var b = m[i + 1] / d[i];
            var s = a * a + b * b;
            if (s > 9)
            {
                var tau = 3 / MathF.Sqrt(s);
                m[i] = tau * a * d[i];
                m[i + 1] = tau * b * d[i];
            }
        }
        return m;
    }

    public bool Equals(DynamicsCurve? other) => other is not null && Points.SequenceEqual(other.Points);
    public override int GetHashCode() => Points.Aggregate(17, (h, p) => HashCode.Combine(h, p));
}
