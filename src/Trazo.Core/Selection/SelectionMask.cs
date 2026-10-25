using System.Numerics;
using Trazo.Core.Imaging;

namespace Trazo.Core.Selection;

public enum SelectionCombine
{
    /// <summary>Sustituye la selección actual.</summary>
    Replace,
    /// <summary>Unión: máximo de las dos coberturas.</summary>
    Add,
    /// <summary>Resta: a · (1 − b).</summary>
    Subtract,
    /// <summary>Intersección: mínimo de las dos coberturas.</summary>
    Intersect,
}

/// <summary>
/// Selección como máscara de cobertura de 8 bits por píxel (0 = fuera, 255 = dentro), del tamaño del
/// documento. Admite bordes suaves (antialias y difuminado). Ver docs/specs/SE-01-selection.md.
/// </summary>
public sealed class SelectionMask
{
    private const int SubRows = 4;

    public SelectionMask(int width, int height, byte[]? data = null)
    {
        if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        Width = width;
        Height = height;
        Data = data ?? new byte[width * height];
        if (Data.Length != width * height) throw new ArgumentException("Tamaño de máscara incorrecto", nameof(data));
    }

    public int Width { get; }
    public int Height { get; }
    public byte[] Data { get; }

    public byte this[int x, int y] => Data[y * Width + x];

    /// <summary>Área seleccionada en píxeles (cobertura parcial cuenta proporcionalmente).</summary>
    public double Area
    {
        get
        {
            long sum = 0;
            foreach (var b in Data) sum += b;
            return sum / 255.0;
        }
    }

    public bool IsEmpty => Array.TrueForAll(Data, b => b == 0);

    public PixelRect Bounds
    {
        get
        {
            int x0 = Width, y0 = Height, x1 = -1, y1 = -1;
            for (var y = 0; y < Height; y++)
            {
                var row = y * Width;
                for (var x = 0; x < Width; x++)
                {
                    if (Data[row + x] == 0) continue;
                    if (x < x0) x0 = x;
                    if (x > x1) x1 = x;
                    if (y < y0) y0 = y;
                    y1 = y;
                }
            }
            return x1 < 0 ? default : new PixelRect(x0, y0, x1 - x0 + 1, y1 - y0 + 1);
        }
    }

    public SelectionMask Clone() => new(Width, Height, (byte[])Data.Clone());

    public static SelectionMask All(int width, int height)
    {
        var m = new SelectionMask(width, height);
        Array.Fill(m.Data, (byte)255);
        return m;
    }

    /// <summary>Rectángulo con cobertura exacta en los bordes fraccionarios.</summary>
    public static SelectionMask Rectangle(int width, int height, float x0, float y0, float x1, float y1)
    {
        if (x1 < x0) (x0, x1) = (x1, x0);
        if (y1 < y0) (y0, y1) = (y1, y0);
        var m = new SelectionMask(width, height);
        int px0 = Math.Max(0, (int)MathF.Floor(x0)), px1 = Math.Min(width - 1, (int)MathF.Ceiling(x1) - 1);
        int py0 = Math.Max(0, (int)MathF.Floor(y0)), py1 = Math.Min(height - 1, (int)MathF.Ceiling(y1) - 1);
        for (var y = py0; y <= py1; y++)
        {
            var cy = Math.Min(y + 1, y1) - Math.Max(y, y0);
            for (var x = px0; x <= px1; x++)
            {
                var cx = Math.Min(x + 1, x1) - Math.Max(x, x0);
                m.Data[y * width + x] = ToByte(cx * cy);
            }
        }
        return m;
    }

    /// <summary>Elipse inscrita en el rectángulo (polígono de 256 vértices).</summary>
    public static SelectionMask Ellipse(int width, int height, float x0, float y0, float x1, float y1)
    {
        var c = new Vector2((x0 + x1) / 2, (y0 + y1) / 2);
        float rx = MathF.Abs(x1 - x0) / 2, ry = MathF.Abs(y1 - y0) / 2;
        const int n = 256;
        var pts = new Vector2[n];
        for (var i = 0; i < n; i++)
        {
            var t = i * MathF.Tau / n;
            pts[i] = c + new Vector2(MathF.Cos(t) * rx, MathF.Sin(t) * ry);
        }
        return Polygon(width, height, pts);
    }

    /// <summary>
    /// Polígono cerrado con regla par-impar. Cobertura horizontal exacta por subfila
    /// y 4 subfilas por píxel: bordes con antialias deterministas.
    /// </summary>
    public static SelectionMask Polygon(int width, int height, IReadOnlyList<Vector2> points)
    {
        var m = new SelectionMask(width, height);
        if (points.Count < 3) return m;
        float minY = float.MaxValue, maxY = float.MinValue;
        foreach (var p in points)
        {
            minY = Math.Min(minY, p.Y);
            maxY = Math.Max(maxY, p.Y);
        }
        int row0 = Math.Max(0, (int)MathF.Floor(minY)), row1 = Math.Min(height - 1, (int)MathF.Ceiling(maxY));
        var acc = new float[width];
        var xs = new List<float>();
        for (var y = row0; y <= row1; y++)
        {
            Array.Clear(acc);
            var touched = false;
            for (var s = 0; s < SubRows; s++)
            {
                var sy = y + (s + 0.5f) / SubRows;
                xs.Clear();
                for (var i = 0; i < points.Count; i++)
                {
                    var a = points[i];
                    var b = points[(i + 1) % points.Count];
                    if (a.Y == b.Y) continue;
                    // semiabierto [min, max) para no contar dos veces los vértices
                    if (sy < Math.Min(a.Y, b.Y) || sy >= Math.Max(a.Y, b.Y)) continue;
                    xs.Add(a.X + (sy - a.Y) / (b.Y - a.Y) * (b.X - a.X));
                }
                if (xs.Count < 2) continue;
                xs.Sort();
                for (var k = 0; k + 1 < xs.Count; k += 2)
                {
                    float xa = Math.Max(0, xs[k]), xb = Math.Min(width, xs[k + 1]);
                    if (xb <= xa) continue;
                    touched = true;
                    for (var px = (int)MathF.Floor(xa); px < width && px < xb; px++)
                    {
                        var overlap = Math.Min(px + 1, xb) - Math.Max(px, xa);
                        if (overlap > 0) acc[px] += overlap / SubRows;
                    }
                }
            }
            if (!touched) continue;
            var o = y * width;
            for (var x = 0; x < width; x++) m.Data[o + x] = ToByte(acc[x]);
        }
        return m;
    }

    /// <summary>Selección automática a partir de un relleno por inundación (ver <see cref="FloodFill"/>).</summary>
    public static SelectionMask FromFill(FillMask fill, int width, int height) => new(width, height, (byte[])fill.Mask.Clone());

    public SelectionMask Combine(SelectionMask other, SelectionCombine mode)
    {
        if (other.Width != Width || other.Height != Height) throw new ArgumentException("Tamaños distintos", nameof(other));
        if (mode == SelectionCombine.Replace) return other.Clone();
        var r = new SelectionMask(Width, Height);
        for (var i = 0; i < Data.Length; i++)
        {
            int a = Data[i], b = other.Data[i];
            r.Data[i] = mode switch
            {
                SelectionCombine.Add => (byte)Math.Max(a, b),
                SelectionCombine.Intersect => (byte)Math.Min(a, b),
                _ => (byte)((a * (255 - b) + 127) / 255),
            };
        }
        return r;
    }

    public SelectionMask Invert()
    {
        var r = new SelectionMask(Width, Height);
        for (var i = 0; i < Data.Length; i++) r.Data[i] = (byte)(255 - Data[i]);
        return r;
    }

    /// <summary>Difumina el borde: 3 pasadas de desenfoque de caja separable (≈ gaussiano). Fuera del lienzo cuenta como 0.</summary>
    public SelectionMask Feather(float radius)
    {
        var h = (int)MathF.Round(radius / 3);
        if (h <= 0) return Clone();
        var a = new float[Data.Length];
        for (var i = 0; i < a.Length; i++) a[i] = Data[i];
        var b = new float[a.Length];
        for (var pass = 0; pass < 3; pass++)
        {
            BoxBlur(a, b, Width, Height, h, horizontal: true);
            BoxBlur(b, a, Width, Height, h, horizontal: false);
        }
        var r = new SelectionMask(Width, Height);
        for (var i = 0; i < a.Length; i++) r.Data[i] = (byte)Math.Clamp(MathF.Round(a[i]), 0, 255);
        return r;
    }

    private static void BoxBlur(float[] src, float[] dst, int w, int h, int radius, bool horizontal)
    {
        int lines = horizontal ? h : w, length = horizontal ? w : h;
        var norm = 1f / (2 * radius + 1);
        for (var line = 0; line < lines; line++)
        {
            float At(int i) => i < 0 || i >= length ? 0 : horizontal ? src[line * w + i] : src[i * w + line];
            float sum = 0;
            for (var i = -radius; i <= radius; i++) sum += At(i);
            for (var i = 0; i < length; i++)
            {
                if (horizontal) dst[line * w + i] = sum * norm;
                else dst[i * w + line] = sum * norm;
                sum += At(i + radius + 1) - At(i - radius);
            }
        }
    }

    private static byte ToByte(float coverage) => (byte)Math.Clamp(MathF.Round(coverage * 255), 0, 255);
}
