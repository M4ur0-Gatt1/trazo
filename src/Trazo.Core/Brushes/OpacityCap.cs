using Trazo.Core.Imaging;
using Trazo.Core.Strokes;

namespace Trazo.Core.Brushes;

/// <summary>
/// Tope de opacidad de un trazo (BR-06): para cada píxel, el máximo de cobertura de la punta × opacidad del sello
/// entre todos los sellos. Se calcula en la CPU (Direct2D no ofrece una mezcla «máximo» fiable en todos los
/// adaptadores) y el motor sube a la GPU solo la zona que cambia.
/// </summary>
public sealed class OpacityCap(int width, int height)
{
    public int Width { get; } = width;
    public int Height { get; } = height;
    /// <summary>Tope por píxel, 0–255.</summary>
    public byte[] Data { get; } = new byte[width * height];

    public void Clear() => Array.Clear(Data);

    /// <summary>
    /// Añade un sello con su opacidad. <paramref name="tip"/> es la máscara de la punta (tamaño × tamaño, 0–255),
    /// muestreada de forma bilineal con el giro y la escala del sello. Devuelve los píxeles que pudieron cambiar.
    /// </summary>
    public PixelRect Stamp(in Dab d, ReadOnlySpan<byte> tip, int tipSize)
    {
        var op = Math.Clamp(d.Opacity, 0, 1);
        if (op <= 0 || d.Size <= 0) return default;
        // la punta girada cabe en un cuadrado de lado √2 × tamaño
        var reach = d.Size * 0.7072f + 1;
        var x0 = Math.Max(0, (int)MathF.Floor(d.X - reach));
        var y0 = Math.Max(0, (int)MathF.Floor(d.Y - reach));
        var x1 = Math.Min(Width, (int)MathF.Ceiling(d.X + reach));
        var y1 = Math.Min(Height, (int)MathF.Ceiling(d.Y + reach));
        if (x1 <= x0 || y1 <= y0) return default;
        var cos = MathF.Cos(-d.Rotation);
        var sin = MathF.Sin(-d.Rotation);
        var k = tipSize / d.Size;
        for (var y = y0; y < y1; y++)
            for (var x = x0; x < x1; x++)
            {
                // del centro del píxel al espacio de la punta
                float dx = x + 0.5f - d.X, dy = y + 0.5f - d.Y;
                var u = (dx * cos - dy * sin) * k + tipSize / 2f - 0.5f;
                var v = (dx * sin + dy * cos) * k + tipSize / 2f - 0.5f;
                var cov = Bilinear(tip, tipSize, u, v);
                if (cov <= 0) continue;
                var value = (byte)Math.Min(255, (int)(cov * op + 0.5f));
                ref var cell = ref Data[y * Width + x];
                if (value > cell) cell = value;
            }
        return new PixelRect(x0, y0, x1 - x0, y1 - y0);
    }

    private static float Bilinear(ReadOnlySpan<byte> m, int n, float u, float v)
    {
        if (u <= -1 || v <= -1 || u >= n || v >= n) return 0;
        var i = (int)MathF.Floor(u);
        var j = (int)MathF.Floor(v);
        float fu = u - i, fv = v - j;
        return (At(m, n, i, j) * (1 - fu) + At(m, n, i + 1, j) * fu) * (1 - fv) + (At(m, n, i, j + 1) * (1 - fu) + At(m, n, i + 1, j + 1) * fu) * fv;
    }

    private static float At(ReadOnlySpan<byte> m, int n, int a, int b) => a < 0 || b < 0 || a >= n || b >= n ? 0 : m[b * n + a];

    /// <summary>BGRA premultiplicado (blanco con alfa = tope) de una zona, para subirla a la GPU.</summary>
    public byte[] ToBgra(PixelRect r)
    {
        var o = new byte[r.Width * r.Height * 4];
        for (var y = 0; y < r.Height; y++)
            for (var x = 0; x < r.Width; x++)
            {
                var v = Data[(r.Y + y) * Width + r.X + x];
                var i = (y * r.Width + x) * 4;
                o[i] = o[i + 1] = o[i + 2] = o[i + 3] = v;
            }
        return o;
    }
}
