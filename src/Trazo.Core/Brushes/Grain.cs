namespace Trazo.Core.Brushes;

/// <summary>Comportamiento del grano (BR-03).</summary>
public enum GrainMode
{
    /// <summary>Sin grano: el trazo es la acumulación de las puntas.</summary>
    Off,
    /// <summary>El grano se muestrea en cada sello y se desplaza con él (marcas estriadas).</summary>
    Moving,
    /// <summary>El grano queda fijo al lienzo y actúa como plantilla del trazo completo.</summary>
    Texturized,
}

/// <summary>Texturas de grano incluidas, generadas por código y repetibles sin costuras.</summary>
public enum GrainKind
{
    Paper,
    Canvas,
    Charcoal,
    Noise,
    Sponge,
}

/// <summary>
/// Ajustes de grano de un pincel. Las unidades son decisiones de Trazo (ver docs/specs/BR-03-grain.md):
/// Scale es el período del mosaico en múltiplos de 256 px del lienzo (o del diámetro del pincel con <see cref="FollowSize"/>).
/// </summary>
public sealed record GrainSettings
{
    public static readonly GrainSettings Off = new();

    public GrainMode Mode { get; init; }
    public GrainKind Kind { get; init; } = GrainKind.Paper;
    /// <summary>0,1–4. Período del mosaico: 256 px × Scale (o diámetro × Scale con FollowSize).</summary>
    public float Scale { get; init; } = 1;
    /// <summary>0–1. Intensidad de la textura: 0 = sin efecto, 1 = la textura recorta por completo.</summary>
    public float Depth { get; init; } = 0.7f;
    /// <summary>Solo Moving. 0 = el grano viaja con el sello (estrías); 1 = se ancla al lienzo como Texturized.</summary>
    public float Movement { get; init; } = 0.5f;
    /// <summary>Solo Moving. Giro del grano respecto de la dirección del trazo: −1 inverso … 0 fijo … 1 sigue el trazo.</summary>
    public float Rotation { get; init; }
    /// <summary>Solo Moving. El período del grano escala con el tamaño del pincel ("Follow Size").</summary>
    public bool FollowSize { get; init; }
    /// <summary>Solo Moving. Cada trazo empieza en un punto distinto de la textura.</summary>
    public bool OffsetJitter { get; init; }
    /// <summary>−1–1. Aclara u oscurece la textura antes de aplicarla.</summary>
    public float Brightness { get; init; }
    /// <summary>0–2. 1 = sin cambio.</summary>
    public float Contrast { get; init; } = 1;

    public bool IsActive => Mode != GrainMode.Off && Depth > 0;

    /// <summary>Período del mosaico en px del documento.</summary>
    public float Period(float brushDiameter) =>
        Math.Max(4, (FollowSize && Mode == GrainMode.Moving ? brushDiameter * 2 : GrainGenerator.Size) * Math.Clamp(Scale, 0.1f, 4));

    /// <summary>Multiplicador de alfa para un valor de grano g (0–1): (1 − Depth) + Depth · g.</summary>
    public float AlphaFactor(float g) => 1 - Math.Clamp(Depth, 0, 1) * (1 - Math.Clamp(g, 0, 1));
}

/// <summary>Genera texturas de grano de 256×256 que se repiten sin costuras (ruido periódico).</summary>
public static class GrainGenerator
{
    public const int Size = 256;

    /// <summary>Luminancia 0–255 por píxel (255 = deja pasar la pintura, 0 = papel en relieve que no recibe pintura).</summary>
    public static byte[] Generate(GrainKind kind, float brightness = 0, float contrast = 1)
    {
        var v = new float[Size * Size];
        switch (kind)
        {
            case GrainKind.Paper:
                Octaves(v, [(8, 0.45f), (16, 0.3f), (32, 0.15f), (64, 0.1f)], seed: 3);
                break;
            case GrainKind.Canvas:
                for (var y = 0; y < Size; y++)
                for (var x = 0; x < Size; x++)
                {
                    // tejido: dos ondas ortogonales con 16 hilos por mosaico; un ajedrezado suave (sin
                    // cortes) decide qué hilo va por encima. Todas las frecuencias son enteras: sin costuras.
                    var wx = MathF.Sin(x * MathF.Tau * 16 / Size);
                    var wy = MathF.Sin(y * MathF.Tau * 16 / Size);
                    var s = 0.5f + 0.5f * MathF.Sin(x * MathF.Tau * 8 / Size) * MathF.Sin(y * MathF.Tau * 8 / Size);
                    v[y * Size + x] = 0.55f + 0.35f * (wx * s + wy * (1 - s));
                }
                AddOctaves(v, [(32, 0.15f)], seed: 9);
                break;
            case GrainKind.Charcoal:
                // estrías horizontales: ruido muy estirado en x
                Octaves(v, [(4, 0.2f), (8, 0.2f)], seed: 21, stretchY: 8);
                AddOctaves(v, [(64, 0.25f), (128, 0.2f)], seed: 22);
                break;
            case GrainKind.Noise:
            {
                var rnd = new Random(41);
                for (var i = 0; i < v.Length; i++) v[i] = (float)rnd.NextDouble();
                break;
            }
            case GrainKind.Sponge:
                Octaves(v, [(12, 0.6f), (24, 0.4f)], seed: 55);
                for (var i = 0; i < v.Length; i++) v[i] = v[i] > 0.52f ? 1 : v[i] * 0.4f;
                break;
        }
        Normalize(v);
        var bytes = new byte[v.Length];
        for (var i = 0; i < v.Length; i++)
        {
            var x = (v[i] - 0.5f) * Math.Clamp(contrast, 0, 4) + 0.5f + Math.Clamp(brightness, -1, 1) * 0.5f;
            bytes[i] = (byte)Math.Clamp(MathF.Round(x * 255), 0, 255);
        }
        return bytes;
    }

    /// <summary>Convierte la luminancia en BGRA premultiplicado (blanco con alfa = luminancia).</summary>
    public static byte[] ToBgra(ReadOnlySpan<byte> luminance)
    {
        var o = new byte[luminance.Length * 4];
        for (var i = 0; i < luminance.Length; i++)
            o[i * 4] = o[i * 4 + 1] = o[i * 4 + 2] = o[i * 4 + 3] = luminance[i];
        return o;
    }

    private static void Octaves(float[] v, (int Cells, float Weight)[] octaves, int seed, int stretchY = 1)
    {
        Array.Clear(v);
        AddOctaves(v, octaves, seed, stretchY);
    }

    private static void AddOctaves(float[] v, (int Cells, float Weight)[] octaves, int seed, int stretchY = 1)
    {
        foreach (var (cells, w) in octaves)
        {
            var noise = PeriodicNoise(cells, Math.Max(1, cells * stretchY), seed++);
            for (var i = 0; i < v.Length; i++) v[i] += noise[i] * w;
        }
    }

    /// <summary>Ruido de valor suavizado sobre una retícula que se cierra sobre sí misma (mosaico sin costuras).</summary>
    private static float[] PeriodicNoise(int cellsX, int cellsY, int seed)
    {
        var rnd = new Random(seed);
        var lattice = new float[cellsX * cellsY];
        for (var i = 0; i < lattice.Length; i++) lattice[i] = (float)rnd.NextDouble();
        float L(int x, int y) => lattice[(y % cellsY) * cellsX + x % cellsX];
        var o = new float[Size * Size];
        for (var y = 0; y < Size; y++)
        for (var x = 0; x < Size; x++)
        {
            float fx = (float)x * cellsX / Size, fy = (float)y * cellsY / Size;
            int ix = (int)fx, iy = (int)fy;
            float tx = fx - ix, ty = fy - iy;
            float sx = tx * tx * (3 - 2 * tx), sy = ty * ty * (3 - 2 * ty);
            var top = L(ix, iy) + (L(ix + 1, iy) - L(ix, iy)) * sx;
            var bottom = L(ix, iy + 1) + (L(ix + 1, iy + 1) - L(ix, iy + 1)) * sx;
            o[y * Size + x] = top + (bottom - top) * sy;
        }
        return o;
    }

    private static void Normalize(float[] v)
    {
        float min = v.Min(), max = v.Max();
        var range = Math.Max(1e-6f, max - min);
        for (var i = 0; i < v.Length; i++) v[i] = (v[i] - min) / range;
    }
}
