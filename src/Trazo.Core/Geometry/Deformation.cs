using System.Numerics;
using Trazo.Core.Imaging;

namespace Trazo.Core.Geometry;

/// <summary>Homografía plana (matriz 3 × 3 en doble precisión). Lleva el cuadrado unidad a un cuadrilátero.</summary>
public readonly record struct Homography(double A, double B, double C, double D, double E, double F, double G, double H, double I = 1)
{
    /// <summary>
    /// Homografía del cuadrado unidad (0,0) (1,0) (1,1) (0,1) a las esquinas q0..q3 (Heckbert, 1989).
    /// Devuelve null si el cuadrilátero no es convexo o no tiene área.
    /// </summary>
    public static Homography? FromUnitSquare(IReadOnlyList<Vector2> q)
    {
        if (!IsValidQuad(q)) return null;
        double x0 = q[0].X, y0 = q[0].Y, x1 = q[1].X, y1 = q[1].Y, x2 = q[2].X, y2 = q[2].Y, x3 = q[3].X, y3 = q[3].Y;
        double sx = x0 - x1 + x2 - x3, sy = y0 - y1 + y2 - y3;
        if (Math.Abs(sx) < 1e-12 && Math.Abs(sy) < 1e-12)
            return new Homography(x1 - x0, x3 - x0, x0, y1 - y0, y3 - y0, y0, 0, 0);
        double dx1 = x1 - x2, dx2 = x3 - x2, dy1 = y1 - y2, dy2 = y3 - y2;
        var det = dx1 * dy2 - dx2 * dy1;
        var g = (sx * dy2 - dx2 * sy) / det;
        var h = (dx1 * sy - sx * dy1) / det;
        return new Homography(x1 - x0 + g * x1, x3 - x0 + h * x3, x0, y1 - y0 + g * y1, y3 - y0 + h * y3, y0, g, h);
    }

    /// <summary>Cuadrilátero convexo, sin esquinas cruzadas y con área de al menos 1 px².</summary>
    public static bool IsValidQuad(IReadOnlyList<Vector2> q)
    {
        if (q.Count != 4 || q.Any(p => !float.IsFinite(p.X) || !float.IsFinite(p.Y))) return false;
        var sign = 0;
        for (var i = 0; i < 4; i++)
        {
            var a = q[i];
            var b = q[(i + 1) % 4];
            var c = q[(i + 2) % 4];
            var cross = (double)(b.X - a.X) * (c.Y - b.Y) - (double)(b.Y - a.Y) * (c.X - b.X);
            if (Math.Abs(cross) < 1e-6) return false;
            var s = Math.Sign(cross);
            if (sign == 0) sign = s;
            else if (s != sign) return false;
        }
        double area = 0;
        for (var i = 0; i < 4; i++) area += (double)q[i].X * q[(i + 1) % 4].Y - (double)q[(i + 1) % 4].X * q[i].Y;
        return Math.Abs(area) / 2 >= 1;
    }

    public Vector2 Map(Vector2 p) => Map(p.X, p.Y);

    public Vector2 Map(double u, double v)
    {
        var w = G * u + H * v + I;
        return new Vector2((float)((A * u + B * v + C) / w), (float)((D * u + E * v + F) / w));
    }

    public Homography Inverse()
    {
        // adjunta de la matriz [[A B C] [D E F] [G H I]]
        var a = E * I - F * H;
        var b = C * H - B * I;
        var c = B * F - C * E;
        var d = F * G - D * I;
        var e = A * I - C * G;
        var f = C * D - A * F;
        var g = D * H - E * G;
        var h = B * G - A * H;
        var i = A * E - B * D;
        return new Homography(a / i, b / i, c / i, d / i, e / i, f / i, g / i, h / i, 1);
    }
}

/// <summary>Parche de Bézier bicúbico de 4 × 4 puntos de control (fila a fila, de arriba abajo).</summary>
public static class BezierPatch
{
    private static void Bernstein(float t, Span<float> b)
    {
        var s = 1 - t;
        b[0] = s * s * s;
        b[1] = 3 * s * s * t;
        b[2] = 3 * s * t * t;
        b[3] = t * t * t;
    }

    public static Vector2 Evaluate(IReadOnlyList<Vector2> ctrl, float u, float v)
    {
        Span<float> bu = stackalloc float[4];
        Span<float> bv = stackalloc float[4];
        Bernstein(u, bu);
        Bernstein(v, bv);
        var p = Vector2.Zero;
        for (var j = 0; j < 4; j++)
            for (var i = 0; i < 4; i++)
                p += ctrl[j * 4 + i] * (bu[i] * bv[j]);
        return p;
    }
}

public enum TransformMode
{
    Freeform,
    Uniform,
    Distort,
    Warp,
}

public enum DeformationKind
{
    None,
    Perspective,
    Warp,
}

/// <summary>Asas de Libre y Uniforme: 4 esquinas (0–3), 4 lados (4–7: arriba, derecha, abajo, izquierda) y giro (8).</summary>
public enum TransformHandle
{
    TopLeft, TopRight, BottomRight, BottomLeft,
    Top, Right, Bottom, Left,
    Rotate,
}

/// <summary>Umbrales del modo Magnético (TR-01).</summary>
public static class Magnetics
{
    public const float AngleStepDegrees = 15;
    public const float AngleThresholdDegrees = 3;
    public const float AxisThresholdDegrees = 6;
    public const float CenterThresholdScreenPx = 8;
    public const float ScaleThreshold = 0.03f;

    /// <summary>Ajusta un ángulo (radianes) al múltiplo de 15° más cercano si está a menos de 3°.</summary>
    public static float SnapAngle(float radians)
    {
        var step = AngleStepDegrees * MathF.PI / 180;
        var nearest = MathF.Round(radians / step) * step;
        return MathF.Abs(radians - nearest) < AngleThresholdDegrees * MathF.PI / 180 ? nearest : radians;
    }

    /// <summary>Bloquea un desplazamiento al eje horizontal o vertical si está a menos de 6° de él.</summary>
    public static Vector2 LockAxis(Vector2 delta)
    {
        if (delta.LengthSquared() < 1e-6f) return delta;
        var angle = MathF.Atan2(MathF.Abs(delta.Y), MathF.Abs(delta.X)) * 180 / MathF.PI;
        if (angle < AxisThresholdDegrees) return new Vector2(delta.X, 0);
        if (angle > 90 - AxisThresholdDegrees) return new Vector2(0, delta.Y);
        return delta;
    }

    /// <summary>Corrección que lleva el centro al objetivo si está más cerca que el umbral (px del documento).</summary>
    public static Vector2 SnapCenter(Vector2 center, Vector2 target, float threshold) =>
        Vector2.Distance(center, target) < threshold ? target - center : Vector2.Zero;

    public static float SnapScale(float s) => MathF.Abs(s - 1) < ScaleThreshold ? 1 : s;
}

/// <summary>
/// Estado puro de Transformar (TR-01): F(p) = M · D(p). D deforma el rectángulo del contenido
/// (identidad, perspectiva de 4 esquinas o parche de Bézier de 16 puntos) y M es afín.
/// </summary>
public sealed class TransformState
{
    private Matrix3x2 _anchorMatrix;
    private Vector2[] _anchorPoints = [];
    private uint[] _anchorIds = [];
    private TransformHandle? _handle;
    private Matrix3x2 _handleMatrix;
    private Vector2 _handleStart;

    public TransformState(PixelRect bounds, Vector2 canvasSize)
    {
        Bounds = bounds;
        CanvasSize = canvasSize;
    }

    public PixelRect Bounds { get; }
    public Vector2 CanvasSize { get; }
    public Matrix3x2 Matrix { get; set; } = Matrix3x2.Identity;
    public TransformMode Mode { get; private set; } = TransformMode.Freeform;
    public DeformationKind Kind { get; private set; } = DeformationKind.None;
    public bool Magnetic { get; set; } = true;

    /// <summary>Puntos de la deformación en el espacio anterior a M: 4 esquinas (perspectiva) o 16 de control (deformar).</summary>
    public Vector2[] Points { get; private set; } = [];

    public bool IsAffine => Kind == DeformationKind.None;

    private Vector2 Rect(float u, float v) => new(Bounds.X + u * Bounds.Width, Bounds.Y + v * Bounds.Height);

    /// <summary>D(u, v): punto del parámetro (u, v) ∈ [0,1]² antes de aplicar M.</summary>
    public Vector2 Deform(float u, float v) => Kind switch
    {
        DeformationKind.Perspective => Homography.FromUnitSquare(Points)!.Value.Map(u, v),
        DeformationKind.Warp => BezierPatch.Evaluate(Points, u, v),
        _ => Rect(u, v),
    };

    /// <summary>F(u, v) en coordenadas del documento.</summary>
    public Vector2 Map(float u, float v) => Vector2.Transform(Deform(u, v), Matrix);

    /// <summary>Esquinas transformadas en el orden sup-izq, sup-der, inf-der, inf-izq.</summary>
    public Vector2[] Corners() => [Map(0, 0), Map(1, 0), Map(1, 1), Map(0, 1)];

    /// <summary>Malla de (n+1)² posiciones en el documento, fila a fila.</summary>
    public Vector2[] Mesh(int n)
    {
        var pts = new Vector2[(n + 1) * (n + 1)];
        if (Kind == DeformationKind.Perspective)
        {
            var h = Homography.FromUnitSquare(Points)!.Value;
            for (var j = 0; j <= n; j++)
                for (var i = 0; i <= n; i++)
                    pts[j * (n + 1) + i] = Vector2.Transform(h.Map((double)i / n, (double)j / n), Matrix);
            return pts;
        }
        for (var j = 0; j <= n; j++)
            for (var i = 0; i <= n; i++)
                pts[j * (n + 1) + i] = Map((float)i / n, (float)j / n);
        return pts;
    }

    /// <summary>Asas que se muestran: posiciones en el documento.</summary>
    public Vector2[] HandlePoints() => Mode switch
    {
        TransformMode.Distort => Points.Select(p => Vector2.Transform(p, Matrix)).ToArray(),
        TransformMode.Warp => Points.Select(p => Vector2.Transform(p, Matrix)).ToArray(),
        _ => AffineHandles(),
    };

    /// <summary>Rectángulo que contiene la deformación (espacio anterior a M), para las asas afines.</summary>
    public (Vector2 Min, Vector2 Max) DeformedBox()
    {
        if (IsAffine) return (Rect(0, 0), Rect(1, 1));
        var min = new Vector2(float.MaxValue);
        var max = new Vector2(float.MinValue);
        for (var j = 0; j <= 8; j++)
            for (var i = 0; i <= 8; i++)
            {
                var p = Deform(i / 8f, j / 8f);
                min = Vector2.Min(min, p);
                max = Vector2.Max(max, p);
            }
        return (min, max);
    }

    private static Vector2 BoxPoint(Vector2 min, Vector2 max, TransformHandle h) => h switch
    {
        TransformHandle.TopLeft => min,
        TransformHandle.TopRight => new(max.X, min.Y),
        TransformHandle.BottomRight => max,
        TransformHandle.BottomLeft => new(min.X, max.Y),
        TransformHandle.Top => new((min.X + max.X) / 2, min.Y),
        TransformHandle.Right => new(max.X, (min.Y + max.Y) / 2),
        TransformHandle.Bottom => new((min.X + max.X) / 2, max.Y),
        TransformHandle.Left => new(min.X, (min.Y + max.Y) / 2),
        _ => (min + max) / 2,
    };

    private static TransformHandle Opposite(TransformHandle h) => h switch
    {
        TransformHandle.TopLeft => TransformHandle.BottomRight,
        TransformHandle.TopRight => TransformHandle.BottomLeft,
        TransformHandle.BottomRight => TransformHandle.TopLeft,
        TransformHandle.BottomLeft => TransformHandle.TopRight,
        TransformHandle.Top => TransformHandle.Bottom,
        TransformHandle.Bottom => TransformHandle.Top,
        TransformHandle.Left => TransformHandle.Right,
        TransformHandle.Right => TransformHandle.Left,
        _ => h,
    };

    /// <summary>Las 8 asas de la caja más el asa de giro, en el documento. El asa de giro va a <paramref name="rotateOffset"/> px por encima del lado superior.</summary>
    public Vector2[] AffineHandles(float rotateOffset = 0)
    {
        var (min, max) = DeformedBox();
        var pts = new Vector2[9];
        for (var i = 0; i < 8; i++) pts[i] = Vector2.Transform(BoxPoint(min, max, (TransformHandle)i), Matrix);
        var top = pts[(int)TransformHandle.Top];
        var center = Vector2.Transform((min + max) / 2, Matrix);
        var up = top - center;
        pts[8] = top + (up.LengthSquared() > 1e-6f ? Vector2.Normalize(up) : -Vector2.UnitY) * rotateOffset;
        return pts;
    }

    // ---------- Modos ----------

    /// <summary>Cambia de modo sin mover el contenido (salvo de Deformar a Distorsionar, que conserva solo las esquinas).</summary>
    public void SetMode(TransformMode mode)
    {
        if (mode == TransformMode.Distort && Kind != DeformationKind.Perspective)
        {
            Points = [Deform(0, 0), Deform(1, 0), Deform(1, 1), Deform(0, 1)];
            Kind = DeformationKind.Perspective;
        }
        else if (mode == TransformMode.Warp && Kind != DeformationKind.Warp)
        {
            var pts = new Vector2[16];
            for (var j = 0; j < 4; j++)
                for (var i = 0; i < 4; i++)
                    pts[j * 4 + i] = Deform(i / 3f, j / 3f);
            Points = pts;
            Kind = DeformationKind.Warp;
        }
        Mode = mode;
    }

    public void Reset()
    {
        Matrix = Matrix3x2.Identity;
        Kind = DeformationKind.None;
        Points = [];
        if (Mode is TransformMode.Distort or TransformMode.Warp)
        {
            var mode = Mode;
            Mode = TransformMode.Freeform;
            SetMode(mode);
        }
    }

    // ---------- Puntos de Distorsionar y Deformar ----------

    /// <summary>Lleva el punto i (esquina o control) a la posición del documento; devuelve false si deja una perspectiva inválida.</summary>
    public bool MovePoint(int index, Vector2 docPoint)
    {
        if (index < 0 || index >= Points.Length || !Matrix3x2.Invert(Matrix, out var inv)) return false;
        var p = Vector2.Transform(docPoint, inv);
        if (Kind == DeformationKind.Perspective)
        {
            var q = (Vector2[])Points.Clone();
            q[index] = p;
            if (!Homography.IsValidQuad(q)) return false;
            Points = q;
            return true;
        }
        Points[index] = p;
        return true;
    }

    /// <summary>Coloca las 4 esquinas de Distorsionar a la vez (en el documento); false si el cuadrilátero no es válido.</summary>
    public bool SetCorners(IReadOnlyList<Vector2> docCorners)
    {
        if (docCorners.Count != 4 || !Matrix3x2.Invert(Matrix, out var inv)) return false;
        var q = docCorners.Select(p => Vector2.Transform(p, inv)).ToArray();
        if (!Homography.IsValidQuad(q)) return false;
        if (Mode != TransformMode.Distort) SetMode(TransformMode.Distort);
        Points = q;
        return true;
    }

    // ---------- Asas de Libre y Uniforme ----------

    public void BeginHandle(TransformHandle handle, Vector2 docPoint)
    {
        _handle = handle;
        _handleMatrix = Matrix;
        _handleStart = docPoint;
    }

    public void DragHandle(Vector2 docPoint)
    {
        if (_handle is not { } h || !Matrix3x2.Invert(_handleMatrix, out var inv)) return;
        var (min, max) = DeformedBox();
        var center = (min + max) / 2;
        if (h == TransformHandle.Rotate)
        {
            var c = Vector2.Transform(center, _handleMatrix);
            var a0 = MathF.Atan2(_handleStart.Y - c.Y, _handleStart.X - c.X);
            var a1 = MathF.Atan2(docPoint.Y - c.Y, docPoint.X - c.X);
            var da = a1 - a0;
            if (Magnetic) da = SnapAbsoluteRotation(_handleMatrix, da);
            Matrix = _handleMatrix * Matrix3x2.CreateTranslation(-c) * Matrix3x2.CreateRotation(da) * Matrix3x2.CreateTranslation(c);
            return;
        }
        var fixedPt = BoxPoint(min, max, Opposite(h));
        var moving = BoxPoint(min, max, h);
        var q = Vector2.Transform(docPoint, inv);
        var span = moving - fixedPt;
        float sx = 1, sy = 1;
        var corner = (int)h < 4;
        if (Mode == TransformMode.Uniform && corner)
        {
            var s = Vector2.Dot(q - fixedPt, span) / Math.Max(1e-6f, span.LengthSquared());
            sx = sy = s;
        }
        else
        {
            if (h is not (TransformHandle.Top or TransformHandle.Bottom) && MathF.Abs(span.X) > 1e-6f) sx = (q.X - fixedPt.X) / span.X;
            if (h is not (TransformHandle.Left or TransformHandle.Right) && MathF.Abs(span.Y) > 1e-6f) sy = (q.Y - fixedPt.Y) / span.Y;
            if (Mode == TransformMode.Uniform)
            {
                // un lado en Uniforme escala los dos ejes por igual
                if (h is TransformHandle.Top or TransformHandle.Bottom) sx = sy;
                else sy = sx;
            }
        }
        if (Magnetic)
        {
            sx = Magnetics.SnapScale(sx);
            sy = Magnetics.SnapScale(sy);
        }
        sx = Guard(sx);
        sy = Guard(sy);
        Matrix = Matrix3x2.CreateTranslation(-fixedPt) * Matrix3x2.CreateScale(sx, sy) * Matrix3x2.CreateTranslation(fixedPt) * _handleMatrix;
    }

    public void EndHandle() => _handle = null;
    public bool HandleActive => _handle is not null;

    private static float Guard(float s) => MathF.Abs(s) < 0.01f ? (s < 0 ? -0.01f : 0.01f) : s;

    private static float RotationOf(Matrix3x2 m) => MathF.Atan2(m.M12, m.M11);

    // Devuelve el incremento de giro que deja el giro total ajustado a 15° (si está cerca)
    private static float SnapAbsoluteRotation(Matrix3x2 start, float delta)
    {
        var total = RotationOf(start) + delta;
        return delta + Magnetics.SnapAngle(total) - total;
    }

    // ---------- Gestos (uno o dos punteros, en coordenadas del documento) ----------

    public void Anchor(IReadOnlyList<(uint Id, Vector2 DocPoint)> pointers)
    {
        _anchorMatrix = Matrix;
        _anchorIds = pointers.Take(2).Select(p => p.Id).ToArray();
        _anchorPoints = pointers.Take(2).Select(p => p.DocPoint).ToArray();
    }

    /// <param name="centerThreshold">Distancia (px del documento) a la que el centro se pega al del lienzo.</param>
    public void Move(IReadOnlyDictionary<uint, Vector2> current, float centerThreshold = 0)
    {
        if (_anchorIds.Length == 0) return;
        if (_anchorIds.Length >= 2 && current.TryGetValue(_anchorIds[0], out var a1) && current.TryGetValue(_anchorIds[1], out var b1))
        {
            Vector2 a0 = _anchorPoints[0], b0 = _anchorPoints[1];
            var c0 = (a0 + b0) / 2;
            var c1 = (a1 + b1) / 2;
            var k = Vector2.Distance(a1, b1) / Math.Max(1e-3f, Vector2.Distance(a0, b0));
            var da = MathF.Atan2(b1.Y - a1.Y, b1.X - a1.X) - MathF.Atan2(b0.Y - a0.Y, b0.X - a0.X);
            if (Magnetic) da = SnapAbsoluteRotation(_anchorMatrix, da);
            Matrix = _anchorMatrix * Matrix3x2.CreateTranslation(-c0) * Matrix3x2.CreateScale(k) * Matrix3x2.CreateRotation(da) * Matrix3x2.CreateTranslation(c1);
        }
        else if (current.TryGetValue(_anchorIds[0], out var p))
        {
            var delta = p - _anchorPoints[0];
            if (Magnetic) delta = Magnetics.LockAxis(delta);
            Matrix = _anchorMatrix * Matrix3x2.CreateTranslation(delta);
            if (Magnetic && centerThreshold > 0)
            {
                var (min, max) = DeformedBox();
                var c = Vector2.Transform((min + max) / 2, Matrix);
                Matrix *= Matrix3x2.CreateTranslation(Magnetics.SnapCenter(c, CanvasSize / 2, centerThreshold));
            }
        }
    }

    // ---------- Operaciones de los botones ----------

    public Vector2 Center
    {
        get
        {
            var (min, max) = DeformedBox();
            return Vector2.Transform((min + max) / 2, Matrix);
        }
    }

    private void Around(Vector2 pivot, Matrix3x2 m) =>
        Matrix = Matrix * Matrix3x2.CreateTranslation(-pivot) * m * Matrix3x2.CreateTranslation(pivot);

    public void Flip(bool horizontal) => Around(Center, Matrix3x2.CreateScale(horizontal ? -1 : 1, horizontal ? 1 : -1));
    public void Rotate(float degrees) => Around(Center, Matrix3x2.CreateRotation(degrees * MathF.PI / 180));
    public void ScaleAt(Vector2 pivot, float k) => Around(pivot, Matrix3x2.CreateScale(k));
    public void RotateAt(Vector2 pivot, float degrees) => Around(pivot, Matrix3x2.CreateRotation(degrees * MathF.PI / 180));

    public void FitToCanvas()
    {
        var (min, max) = DeformedBox();
        var size = max - min;
        var k = Math.Min(CanvasSize.X / size.X, CanvasSize.Y / size.Y);
        Matrix = Matrix3x2.CreateTranslation(-min) * Matrix3x2.CreateScale(k) * Matrix3x2.CreateTranslation((CanvasSize - size * k) / 2);
    }
}
