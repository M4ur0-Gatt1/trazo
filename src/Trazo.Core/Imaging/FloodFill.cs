namespace Trazo.Core.Imaging;

public readonly record struct PixelRect(int X, int Y, int Width, int Height)
{
    public bool IsEmpty => Width <= 0 || Height <= 0;
    public int Right => X + Width;
    public int Bottom => Y + Height;
}

public sealed record FillMask(byte[] Mask, PixelRect Bounds);

public static class FloodFill
{
    /// <summary>
    /// Relleno por línea de barrido sobre una imagen de 4 bytes por píxel. Compara
    /// los cuatro canales con la tolerancia (0–255) y dilata 1 px el resultado para
    /// cubrir el antialias de los bordes. Devuelve una máscara del tamaño de la imagen.
    /// </summary>
    public static FillMask? Compute(ReadOnlySpan<byte> pixels, int width, int height, int x, int y, int tolerance)
    {
        if (x < 0 || y < 0 || x >= width || y >= height) return null;
        if (pixels.Length < width * height * 4) throw new ArgumentException("Búfer demasiado pequeño", nameof(pixels));
        var o = (y * width + x) * 4;
        byte t0 = pixels[o], t1 = pixels[o + 1], t2 = pixels[o + 2], t3 = pixels[o + 3];
        var mask = new byte[width * height];
        var px = pixels.ToArray();

        bool Match(int i)
        {
            var j = i * 4;
            return Math.Abs(px[j] - t0) <= tolerance && Math.Abs(px[j + 1] - t1) <= tolerance &&
                   Math.Abs(px[j + 2] - t2) <= tolerance && Math.Abs(px[j + 3] - t3) <= tolerance;
        }

        int x0 = x, x1 = x, y0 = y, y1 = y;
        var stack = new Stack<(int X, int Y)>();
        stack.Push((x, y));
        while (stack.Count > 0)
        {
            var (sx, sy) = stack.Pop();
            var i = sy * width + sx;
            while (sx >= 0 && mask[i] == 0 && Match(i)) { sx--; i--; }
            sx++; i++;
            bool up = false, down = false;
            while (sx < width && mask[i] == 0 && Match(i))
            {
                mask[i] = 1;
                if (sx < x0) x0 = sx;
                if (sx > x1) x1 = sx;
                if (sy < y0) y0 = sy;
                if (sy > y1) y1 = sy;
                if (sy > 0)
                {
                    var m = mask[i - width] == 0 && Match(i - width);
                    if (m && !up) { stack.Push((sx, sy - 1)); up = true; }
                    else if (!m) up = false;
                }
                if (sy < height - 1)
                {
                    var m = mask[i + width] == 0 && Match(i + width);
                    if (m && !down) { stack.Push((sx, sy + 1)); down = true; }
                    else if (!m) down = false;
                }
                sx++; i++;
            }
        }

        x0 = Math.Max(0, x0 - 1); y0 = Math.Max(0, y0 - 1);
        x1 = Math.Min(width - 1, x1 + 1); y1 = Math.Min(height - 1, y1 + 1);
        var dilated = new byte[mask.Length];
        for (var yy = y0; yy <= y1; yy++)
        for (var xx = x0; xx <= x1; xx++)
        {
            var i = yy * width + xx;
            if (mask[i] != 0 || (xx > 0 && mask[i - 1] != 0) || (xx < width - 1 && mask[i + 1] != 0) ||
                (yy > 0 && mask[i - width] != 0) || (yy < height - 1 && mask[i + width] != 0))
                dilated[i] = 255;
        }
        return new FillMask(dilated, new PixelRect(x0, y0, x1 - x0 + 1, y1 - y0 + 1));
    }

    /// <summary>Recorta la máscara a sus límites y la colorea en BGRA premultiplicado.</summary>
    public static byte[] ToBgra(FillMask fill, int imageWidth, byte r, byte g, byte b)
    {
        var bd = fill.Bounds;
        var o = new byte[bd.Width * bd.Height * 4];
        for (var y = 0; y < bd.Height; y++)
        for (var x = 0; x < bd.Width; x++)
        {
            if (fill.Mask[(bd.Y + y) * imageWidth + bd.X + x] == 0) continue;
            var j = (y * bd.Width + x) * 4;
            o[j] = b; o[j + 1] = g; o[j + 2] = r; o[j + 3] = 255;
        }
        return o;
    }

    /// <summary>Rectángulo que contiene los píxeles con alfa &gt; 0 (imagen de 4 bytes por píxel, alfa en el byte 3).</summary>
    public static PixelRect ContentBounds(ReadOnlySpan<byte> pixels, int width, int height)
    {
        int x0 = width, y0 = height, x1 = -1, y1 = -1;
        for (var y = 0; y < height; y++)
        {
            var row = y * width * 4 + 3;
            for (var x = 0; x < width; x++, row += 4)
            {
                if (pixels[row] == 0) continue;
                if (x < x0) x0 = x;
                if (x > x1) x1 = x;
                if (y < y0) y0 = y;
                y1 = y;
            }
        }
        return x1 < 0 ? default : new PixelRect(x0, y0, x1 - x0 + 1, y1 - y0 + 1);
    }
}
