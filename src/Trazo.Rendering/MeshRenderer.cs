using System.Numerics;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Brushes;
using Microsoft.Graphics.Canvas.Geometry;
using Trazo.Core.Imaging;

namespace Trazo.Rendering;

/// <summary>
/// Dibuja una imagen deformada por una malla (TR-01): cada triángulo se rellena con la imagen mediante
/// la transformación afín que lleva su triángulo de origen al de destino. Sin antialias, los triángulos
/// vecinos cubren cada píxel exactamente una vez (sin costuras ni solapes).
/// </summary>
public static class MeshRenderer
{
    /// <param name="mesh">(n+1)² posiciones de destino, fila a fila, para la cuadrícula regular de <paramref name="bounds"/>.</param>
    public static CanvasCommandList Render(CanvasDevice device, CanvasBitmap source, PixelRect bounds, IReadOnlyList<Vector2> mesh, int n, bool smooth)
    {
        var list = new CanvasCommandList(device);
        using var ds = list.CreateDrawingSession();
        ds.Antialiasing = CanvasAntialiasing.Aliased;
        using var brush = new CanvasImageBrush(device, source)
        {
            Interpolation = smooth ? CanvasImageInterpolation.Linear : CanvasImageInterpolation.NearestNeighbor,
            ExtendX = CanvasEdgeBehavior.Clamp,
            ExtendY = CanvasEdgeBehavior.Clamp,
        };
        Vector2 Src(int i, int j) => new(bounds.X + (float)bounds.Width * i / n, bounds.Y + (float)bounds.Height * j / n);
        Vector2 Dst(int i, int j) => mesh[j * (n + 1) + i];
        // PF-01: un solo triángulo unidad para toda la malla; cada triángulo es ese con su transformación
        // (crear una geometría por triángulo y fotograma era lo más lento de la vista previa)
        using var unit = CanvasGeometry.CreatePolygon(device, [Vector2.Zero, Vector2.UnitX, Vector2.UnitY]);
        void Triangle(Vector2 s0, Vector2 s1, Vector2 s2, Vector2 d0, Vector2 d1, Vector2 d2)
        {
            var area = (d1.X - d0.X) * (d2.Y - d0.Y) - (d1.Y - d0.Y) * (d2.X - d0.X);
            if (MathF.Abs(area) < 1e-4f) return;
            var toSource = new Matrix3x2(s1.X - s0.X, s1.Y - s0.Y, s2.X - s0.X, s2.Y - s0.Y, s0.X, s0.Y);
            if (!Matrix3x2.Invert(toSource, out var fromSource)) return;
            // el pincel vive en el espacio del triángulo unidad: origen → unidad; la sesión: unidad → destino
            brush.Transform = fromSource;
            ds.Transform = new Matrix3x2(d1.X - d0.X, d1.Y - d0.Y, d2.X - d0.X, d2.Y - d0.Y, d0.X, d0.Y);
            ds.FillGeometry(unit, brush);
        }
        for (var j = 0; j < n; j++)
            for (var i = 0; i < n; i++)
            {
                Triangle(Src(i, j), Src(i + 1, j), Src(i + 1, j + 1), Dst(i, j), Dst(i + 1, j), Dst(i + 1, j + 1));
                Triangle(Src(i, j), Src(i + 1, j + 1), Src(i, j + 1), Dst(i, j), Dst(i + 1, j + 1), Dst(i, j + 1));
            }
        ds.Transform = Matrix3x2.Identity;
        return list;
    }

    /// <summary>Transformación afín que lleva s0, s1, s2 a d0, d1, d2.</summary>
    public static Matrix3x2 Affine(Vector2 s0, Vector2 s1, Vector2 s2, Vector2 d0, Vector2 d1, Vector2 d2)
    {
        var src = new Matrix3x2(s1.X - s0.X, s1.Y - s0.Y, s2.X - s0.X, s2.Y - s0.Y, s0.X, s0.Y);
        var dst = new Matrix3x2(d1.X - d0.X, d1.Y - d0.Y, d2.X - d0.X, d2.Y - d0.Y, d0.X, d0.Y);
        return Matrix3x2.Invert(src, out var inv) ? inv * dst : Matrix3x2.Identity;
    }
}
