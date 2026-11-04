using System.Numerics;

namespace Trazo.Core.Geometry;

/// <summary>
/// Transformación documento → pantalla: escala, giro (radianes) y traslación.
/// pantalla = T(Offset) · R(Rotation) · S(Scale) · documento
/// </summary>
public sealed class ViewTransform
{
    public const float MinScale = 0.02f;
    public const float MaxScale = 64f;

    public Vector2 Offset { get; set; }
    public float Scale { get; set; } = 1;
    public float Rotation { get; set; }

    public Matrix3x2 Matrix =>
        Matrix3x2.CreateScale(Scale) * Matrix3x2.CreateRotation(Rotation) * Matrix3x2.CreateTranslation(Offset);

    public Vector2 ToScreen(Vector2 doc) => Vector2.Transform(doc, Matrix);

    public Vector2 ToDocument(Vector2 screen)
    {
        var p = screen - Offset;
        var c = MathF.Cos(-Rotation);
        var s = MathF.Sin(-Rotation);
        return new Vector2(p.X * c - p.Y * s, p.X * s + p.Y * c) / Scale;
    }

    /// <summary>Desplaza la vista para que el punto del documento quede en la posición de pantalla indicada.</summary>
    public void Pin(Vector2 doc, Vector2 screen)
    {
        Offset = Vector2.Zero;
        Offset = screen - ToScreen(doc);
    }

    public void ZoomAt(Vector2 screen, float factor)
    {
        var d = ToDocument(screen);
        Scale = Math.Clamp(Scale * factor, MinScale, MaxScale);
        Pin(d, screen);
    }

    public void RotateAt(Vector2 screen, float radians)
    {
        var d = ToDocument(screen);
        Rotation += radians;
        Pin(d, screen);
    }

    /// <summary>Centra el documento en el área visible, sin giro.</summary>
    public void Fit(Vector2 docSize, Vector2 viewSize, float margin = 28, float topInset = 0)
    {
        Rotation = 0;
        var availW = Math.Max(1, viewSize.X - margin * 2);
        var availH = Math.Max(1, viewSize.Y - topInset - margin * 2);
        Scale = Math.Clamp(Math.Min(availW / docSize.X, availH / docSize.Y), MinScale, MaxScale);
        Offset = new Vector2((viewSize.X - docSize.X * Scale) / 2, topInset + (viewSize.Y - topInset - docSize.Y * Scale) / 2);
    }

    /// <summary>Si el giro está a menos de <paramref name="toleranceDeg"/> de un múltiplo de 90°, lo ajusta (girando alrededor de <paramref name="pivot"/>).</summary>
    public bool SnapRotation(Vector2 pivot, float toleranceDeg = 7)
    {
        var deg = Rotation * 180 / MathF.PI;
        var near = MathF.Round(deg / 90) * 90;
        if (MathF.Abs(deg - near) >= toleranceDeg || deg == near) return false;
        RotateAt(pivot, (near - deg) * MathF.PI / 180);
        return true;
    }
}

/// <summary>Cómo se escala la imagen del lienzo en pantalla según el zoom.</summary>
public enum ZoomFilter
{
    /// <summary>Alejado: cúbico de alta calidad, sin dientes de sierra.</summary>
    Smooth,
    /// <summary>Lineal: suave y rápido.</summary>
    Linear,
    /// <summary>Píxel a píxel (vecino más cercano), para ver los píxeles reales de muy cerca.</summary>
    Pixels,
}

public static class ZoomFilters
{
    /// <summary>
    /// Píxeles de la pantalla por píxel del lienzo a partir de los que se ven los píxeles reales. Antes era un zoom de
    /// 2,5 sin contar la escala de la pantalla: con un lienzo pequeño, lo dibujado se veía en escalones al acercarse un
    /// poco (2026-11-04).
    /// </summary>
    public const double PixelsFrom = 6;

    /// <param name="scale">Zoom de la vista (píxeles independientes del dispositivo por píxel del lienzo).</param>
    /// <param name="rasterizationScale">Escala de la pantalla (1 = 96 ppp, 1,5 = 150 %…).</param>
    public static ZoomFilter For(double scale, double rasterizationScale)
    {
        var physical = scale * rasterizationScale;
        if (physical >= PixelsFrom) return ZoomFilter.Pixels;
        return physical < 0.5 ? ZoomFilter.Smooth : ZoomFilter.Linear;
    }
}
