using System.Numerics;
using Microsoft.Graphics.Canvas;
using Trazo.Core.Brushes;
using Trazo.Core.Strokes;

namespace Trazo.Rendering;

/// <summary>
/// Lee el color de una textura de la GPU por mosaicos de 256 px bajo demanda (cada mosaico se lee una vez)
/// para la mezcla húmeda (BR-04).
/// </summary>
public sealed class LayerSampler(CanvasBitmap source, int width, int height)
{
    public const int Tile = 256;
    private readonly Dictionary<(int, int), byte[]> _tiles = [];

    /// <summary>Mosaicos leídos de la GPU hasta ahora.</summary>
    public int TilesRead => _tiles.Count;

    private (int Index, byte[] Bytes, int Stride)? Pixel(int x, int y)
    {
        if (x < 0 || y < 0 || x >= width || y >= height) return null;
        var key = (x / Tile, y / Tile);
        var tx = key.Item1 * Tile;
        var ty = key.Item2 * Tile;
        var tw = Math.Min(Tile, width - tx);
        if (!_tiles.TryGetValue(key, out var bytes))
        {
            var th = Math.Min(Tile, height - ty);
            bytes = source.GetPixelBytes(tx, ty, tw, th);
            _tiles[key] = bytes;
        }
        return (((y - ty) * tw + (x - tx)) * 4, bytes, tw);
    }

    /// <summary>Media premultiplicada en 9 puntos (centro y anillo de radio 0,35 × tamaño) bajo el sello.</summary>
    public CanvasSample Sample(Dab d)
    {
        var r = 0.35f * d.Size;
        float b = 0, g = 0, rr = 0, a = 0;
        for (var k = 0; k < 9; k++)
        {
            var off = k == 0 ? Vector2.Zero : new Vector2(MathF.Cos(k * MathF.PI / 4), MathF.Sin(k * MathF.PI / 4)) * r;
            if (Pixel((int)MathF.Floor(d.X + off.X), (int)MathF.Floor(d.Y + off.Y)) is not { } p) continue;
            b += p.Bytes[p.Index];
            g += p.Bytes[p.Index + 1];
            rr += p.Bytes[p.Index + 2];
            a += p.Bytes[p.Index + 3];
        }
        if (a <= 0) return default;
        return new CanvasSample(new Vector3(rr, g, b) / a, a / (9 * 255f));
    }
}
