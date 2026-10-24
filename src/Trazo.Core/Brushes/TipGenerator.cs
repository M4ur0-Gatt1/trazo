namespace Trazo.Core.Brushes;

public enum TipShape
{
    Round,
    Soft,
    Airbrush,
    Pencil,
    Chalk,
    Flat,
    Spray,
    Bristle,
}

/// <summary>
/// Genera puntas de pincel como máscaras alfa (un byte por píxel), de forma
/// determinista, para que el mismo pincel se vea igual en cada ejecución.
/// </summary>
public static class TipGenerator
{
    public const int DefaultSize = 256;

    public static byte[] Generate(TipShape shape, int size = DefaultSize)
    {
        if (size < 8) throw new ArgumentOutOfRangeException(nameof(size));
        var a = new float[size * size];
        var c = size / 2f;
        var r = c - 2;
        switch (shape)
        {
            case TipShape.Round:
                Disc(a, size, c, c, r, 1);
                break;
            case TipShape.Soft:
                Radial(a, size, c, [(0, 1), (0.45f, 0.75f), (1, 0)]);
                break;
            case TipShape.Airbrush:
                Radial(a, size, c, [(0, 1), (0.2f, 0.82f), (0.4f, 0.5f), (0.6f, 0.22f), (0.8f, 0.06f), (1, 0)]);
                break;
            case TipShape.Pencil:
            {
                var rnd = new Random(7);
                for (var y = 0; y < size; y++)
                for (var x = 0; x < size; x++)
                {
                    var d = Dist(x, y, c) / r;
                    var n = (float)rnd.NextDouble();
                    if (d <= 1) a[y * size + x] = MathF.Pow(n, 1.6f) * (1 - MathF.Pow(d, 6)) * 1.15f;
                }
                break;
            }
            case TipShape.Chalk:
            {
                var coarse = ValueNoise(10, 11, size);
                var fine = new Random(23);
                for (var y = 0; y < size; y++)
                for (var x = 0; x < size; x++)
                {
                    var d = Dist(x, y, c) / r;
                    var f = (float)fine.NextDouble();
                    if (d > 1) continue;
                    a[y * size + x] = (coarse(x, y) - 0.32f) * 2.4f * (1 - d * d * d) + (f - 0.5f) * 0.35f;
                }
                break;
            }
            case TipShape.Flat:
                for (var y = 0; y < size; y++)
                for (var x = 0; x < size; x++)
                {
                    float dx = (x + 0.5f - c) / r, dy = (y + 0.5f - c) / (r * 0.22f);
                    var d = MathF.Sqrt(dx * dx + dy * dy);
                    a[y * size + x] = Math.Clamp((1 - d) * r * 0.22f + 0.5f, 0, 1);
                }
                break;
            case TipShape.Spray:
            {
                var rnd = new Random(41);
                for (var i = 0; i < 420; i++)
                {
                    var rad = MathF.Abs((float)(rnd.NextDouble() + rnd.NextDouble() + rnd.NextDouble()) / 3 - 0.5f) * 2 * (c - 6);
                    var th = rnd.NextDouble() * Math.PI * 2;
                    var alpha = 0.5f + (float)rnd.NextDouble() * 0.5f;
                    var dot = size / 256f * (1.2f + (float)rnd.NextDouble() * 2.6f);
                    Disc(a, size, c + MathF.Cos((float)th) * rad, c + MathF.Sin((float)th) * rad, dot, alpha);
                }
                break;
            }
            case TipShape.Bristle:
            {
                var rnd = new Random(97);
                var k = size / 256f;
                for (var i = 0; i < 60; i++)
                {
                    var u = (float)rnd.NextDouble() * 2 - 1;
                    var px = c + u * (c - 14 * k);
                    var py = c + ((float)rnd.NextDouble() * 2 - 1) * (c - 14 * k) * 0.35f * MathF.Sqrt(1 - u * u);
                    var alpha = 0.35f + (float)rnd.NextDouble() * 0.55f;
                    Disc(a, size, px, py, (6 + (float)rnd.NextDouble() * 10) * k, alpha);
                }
                break;
            }
        }

        var bytes = new byte[a.Length];
        for (var i = 0; i < a.Length; i++) bytes[i] = (byte)Math.Clamp(MathF.Round(a[i] * 255), 0, 255);
        return bytes;
    }

    /// <summary>Convierte una máscara alfa en BGRA premultiplicado con el color indicado.</summary>
    public static byte[] Tint(ReadOnlySpan<byte> mask, byte r, byte g, byte b)
    {
        var o = new byte[mask.Length * 4];
        for (var i = 0; i < mask.Length; i++)
        {
            int m = mask[i];
            o[i * 4] = (byte)(b * m / 255);
            o[i * 4 + 1] = (byte)(g * m / 255);
            o[i * 4 + 2] = (byte)(r * m / 255);
            o[i * 4 + 3] = (byte)m;
        }
        return o;
    }

    private static float Dist(int x, int y, float c) => MathF.Sqrt((x + 0.5f - c) * (x + 0.5f - c) + (y + 0.5f - c) * (y + 0.5f - c));

    // Disco con antialias acumulado ("over") sobre la máscara
    private static void Disc(float[] a, int size, float cx, float cy, float r, float alpha)
    {
        int x0 = Math.Max(0, (int)(cx - r - 1)), x1 = Math.Min(size - 1, (int)(cx + r + 1));
        int y0 = Math.Max(0, (int)(cy - r - 1)), y1 = Math.Min(size - 1, (int)(cy + r + 1));
        for (var y = y0; y <= y1; y++)
        for (var x = x0; x <= x1; x++)
        {
            var d = MathF.Sqrt((x + 0.5f - cx) * (x + 0.5f - cx) + (y + 0.5f - cy) * (y + 0.5f - cy));
            var cov = Math.Clamp(r - d + 0.5f, 0, 1) * alpha;
            ref var v = ref a[y * size + x];
            v += cov * (1 - v);
        }
    }

    private static void Radial(float[] a, int size, float c, (float At, float Alpha)[] stops)
    {
        for (var y = 0; y < size; y++)
        for (var x = 0; x < size; x++)
        {
            var t = Math.Min(1, Dist(x, y, c) / c);
            var i = 1;
            while (i < stops.Length - 1 && t > stops[i].At) i++;
            var (t0, a0) = stops[i - 1];
            var (t1, a1) = stops[i];
            var u = t1 > t0 ? Math.Clamp((t - t0) / (t1 - t0), 0, 1) : 0;
            a[y * size + x] = a0 + (a1 - a0) * u;
        }
    }

    private static Func<int, int, float> ValueNoise(int cells, int seed, int size)
    {
        var rnd = new Random(seed);
        var g = new float[(cells + 1) * (cells + 1)];
        for (var i = 0; i < g.Length; i++) g[i] = (float)rnd.NextDouble();
        float At(int x, int y) => g[Math.Min(y, cells) * (cells + 1) + Math.Min(x, cells)];
        return (x, y) =>
        {
            float fx = (float)x / size * cells, fy = (float)y / size * cells;
            int ix = (int)fx, iy = (int)fy;
            float tx = fx - ix, ty = fy - iy;
            float sx = tx * tx * (3 - 2 * tx), sy = ty * ty * (3 - 2 * ty);
            var top = At(ix, iy) + (At(ix + 1, iy) - At(ix, iy)) * sx;
            var bottom = At(ix, iy + 1) + (At(ix + 1, iy + 1) - At(ix, iy + 1)) * sx;
            return top + (bottom - top) * sy;
        };
    }
}
