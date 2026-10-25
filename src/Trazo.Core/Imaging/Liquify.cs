using System.Numerics;

namespace Trazo.Core.Imaging;

public enum LiquifyMode
{
    Push,
    TwirlRight,
    TwirlLeft,
    Pinch,
    Expand,
    Reconstruct,
}

/// <summary>
/// Campo de desplazamiento de Licuar (LQ-01): salida(p) = original(p − u(p)). Los nodos están cada
/// <see cref="Spacing"/> px (en coordenadas continuas, con el centro del píxel x en x + 0,5) y se interpolan
/// de forma bilineal.
/// </summary>
public sealed class LiquifyField
{
    public const float TwirlRadiansPerDab = 0.3f;
    public const float PinchPerDab = 0.25f;
    public const float ReconstructPerDab = 0.5f;

    public LiquifyField(int width, int height, int spacing = 4)
    {
        Width = width;
        Height = height;
        Spacing = spacing;
        Nx = (int)Math.Ceiling((double)width / spacing) + 1;
        Ny = (int)Math.Ceiling((double)height / spacing) + 1;
        Dx = new float[Nx * Ny];
        Dy = new float[Nx * Ny];
    }

    public int Width { get; }
    public int Height { get; }
    public int Spacing { get; }
    public int Nx { get; }
    public int Ny { get; }
    public float[] Dx { get; }
    public float[] Dy { get; }

    public bool IsZero => Dx.All(v => v == 0) && Dy.All(v => v == 0);

    public void Clear()
    {
        Array.Clear(Dx);
        Array.Clear(Dy);
    }

    public Vector2 Node(int i, int j) => new(Dx[j * Nx + i], Dy[j * Nx + i]);

    public Vector2 Sample(Vector2 p)
    {
        var gx = Math.Clamp(p.X / Spacing, 0, Nx - 1);
        var gy = Math.Clamp(p.Y / Spacing, 0, Ny - 1);
        var i0 = Math.Min((int)gx, Nx - 2);
        var j0 = Math.Min((int)gy, Ny - 2);
        if (Nx < 2 || Ny < 2) return Node(0, 0);
        var fx = gx - i0;
        var fy = gy - j0;
        var k = j0 * Nx + i0;
        var x = (Dx[k] * (1 - fx) + Dx[k + 1] * fx) * (1 - fy) + (Dx[k + Nx] * (1 - fx) + Dx[k + Nx + 1] * fx) * fy;
        var y = (Dy[k] * (1 - fx) + Dy[k + 1] * fx) * (1 - fy) + (Dy[k + Nx] * (1 - fx) + Dy[k + Nx + 1] * fx) * fy;
        return new Vector2(x, y);
    }

    /// <summary>Caída del pincel: (1 − (r/R)²)² dentro del radio, 0 fuera.</summary>
    public static float Falloff(float r, float radius)
    {
        if (r >= radius) return 0;
        var t = r / radius;
        var a = 1 - t * t;
        return a * a;
    }

    /// <summary>
    /// Aplica un toque y devuelve el rectángulo de píxeles que puede haber cambiado.
    /// La composición es exacta: u′(p) = δ(p) + u(p − δ(p)).
    /// </summary>
    public PixelRect Dab(LiquifyMode mode, Vector2 center, Vector2 previous, float radius, float strength)
    {
        var i0 = Math.Max(0, (int)MathF.Floor((center.X - radius) / Spacing));
        var i1 = Math.Min(Nx - 1, (int)MathF.Ceiling((center.X + radius) / Spacing));
        var j0 = Math.Max(0, (int)MathF.Floor((center.Y - radius) / Spacing));
        var j1 = Math.Min(Ny - 1, (int)MathF.Ceiling((center.Y + radius) / Spacing));
        if (i1 < i0 || j1 < j0) return default;
        var cols = i1 - i0 + 1;
        var nx = new float[cols * (j1 - j0 + 1)];
        var ny = new float[nx.Length];
        var changed = new bool[nx.Length];
        // se leen los valores anteriores antes de escribir ninguno
        for (var j = j0; j <= j1; j++)
            for (var i = i0; i <= i1; i++)
            {
                var p = new Vector2(i * Spacing, j * Spacing);
                var w = Falloff(Vector2.Distance(p, center), radius);
                if (w <= 0) continue;
                var t = (j - j0) * cols + (i - i0);
                var old = Node(i, j);
                Vector2 result;
                if (mode == LiquifyMode.Reconstruct)
                {
                    result = old * (1 - Math.Clamp(ReconstructPerDab * strength * w, 0, 1));
                }
                else
                {
                    var rel = p - center;
                    var delta = mode switch
                    {
                        LiquifyMode.Push => (center - previous) * w,
                        LiquifyMode.TwirlRight or LiquifyMode.TwirlLeft =>
                            Vector2.Transform(rel, Matrix3x2.CreateRotation((mode == LiquifyMode.TwirlRight ? 1 : -1) * TwirlRadiansPerDab * w * strength)) - rel,
                        LiquifyMode.Pinch => -PinchPerDab * strength * w * rel,
                        _ => PinchPerDab * strength * w * rel,
                    };
                    result = delta + Sample(p - delta);
                }
                nx[t] = result.X;
                ny[t] = result.Y;
                changed[t] = true;
            }
        for (var j = j0; j <= j1; j++)
            for (var i = i0; i <= i1; i++)
            {
                var t = (j - j0) * cols + (i - i0);
                if (!changed[t]) continue;
                Dx[j * Nx + i] = nx[t];
                Dy[j * Nx + i] = ny[t];
            }
        // el campo cambia hasta un nodo más allá del radio por la interpolación
        var m = radius + Spacing + 1;
        return Clip(new PixelRect((int)MathF.Floor(center.X - m), (int)MathF.Floor(center.Y - m), (int)MathF.Ceiling(2 * m) + 1, (int)MathF.Ceiling(2 * m) + 1));
    }

    public PixelRect Clip(PixelRect r)
    {
        var x0 = Math.Clamp(r.X, 0, Width);
        var y0 = Math.Clamp(r.Y, 0, Height);
        var x1 = Math.Clamp(r.Right, 0, Width);
        var y1 = Math.Clamp(r.Bottom, 0, Height);
        return new PixelRect(x0, y0, x1 - x0, y1 - y0);
    }

    public PixelRect Full => new(0, 0, Width, Height);
}

public static class Liquify
{
    /// <summary>
    /// Remuestrea la zona <paramref name="rect"/>: dst(p) = src(p − amount·u(p)), bilineal en BGRA premultiplicado,
    /// transparente fuera de la imagen. Con u = 0 copia los bytes exactos.
    /// </summary>
    public static void Render(byte[] src, byte[] dst, LiquifyField field, PixelRect rect, float amount = 1)
    {
        var w = field.Width;
        var h = field.Height;
        Parallel.For(rect.Y, rect.Bottom, y =>
        {
            Span<float> acc = stackalloc float[4];
            for (var x = rect.X; x < rect.Right; x++)
            {
                var u = field.Sample(new Vector2(x + 0.5f, y + 0.5f)) * amount;
                var sx = x - u.X;
                var sy = y - u.Y;
                var x0 = (int)MathF.Floor(sx);
                var y0 = (int)MathF.Floor(sy);
                var fx = sx - x0;
                var fy = sy - y0;
                acc.Clear();
                Add(src, w, h, x0, y0, (1 - fx) * (1 - fy), acc);
                Add(src, w, h, x0 + 1, y0, fx * (1 - fy), acc);
                Add(src, w, h, x0, y0 + 1, (1 - fx) * fy, acc);
                Add(src, w, h, x0 + 1, y0 + 1, fx * fy, acc);
                var o = (y * w + x) * 4;
                for (var c = 0; c < 4; c++) dst[o + c] = (byte)Math.Clamp((int)(acc[c] + 0.5f), 0, 255);
                // en premultiplicado ningún canal puede superar al alfa
                for (var c = 0; c < 3; c++) if (dst[o + c] > dst[o + 3]) dst[o + c] = dst[o + 3];
            }
        });
    }

    private static void Add(byte[] src, int w, int h, int x, int y, float weight, Span<float> acc)
    {
        if (weight == 0 || x < 0 || y < 0 || x >= w || y >= h) return;
        var o = (y * w + x) * 4;
        for (var c = 0; c < 4; c++) acc[c] += src[o + c] * weight;
    }

    /// <summary>Centros de los toques a lo largo de un tramo, uno cada 25 % del radio desde el último.</summary>
    public static List<Vector2> DabsAlong(ref Vector2 last, Vector2 to, float radius)
    {
        var step = Math.Max(1, radius * 0.25f);
        var list = new List<Vector2>();
        var d = Vector2.Distance(last, to);
        if (d < step) return list;
        var dir = (to - last) / d;
        var n = (int)(d / step);
        for (var k = 1; k <= n; k++) list.Add(last + dir * (step * k));
        last = list[^1];
        return list;
    }
}
