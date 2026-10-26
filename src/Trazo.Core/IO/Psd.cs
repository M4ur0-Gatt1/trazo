using System.Buffers.Binary;
using System.Text;
using Trazo.Core.Documents;
using static Trazo.Core.Localization.Tr;

namespace Trazo.Core.IO;

public enum PsdLayerKind
{
    /// <summary>Capa con píxeles.</summary>
    Pixels,
    /// <summary>Inicio (arriba) de una carpeta abierta.</summary>
    FolderOpen,
    /// <summary>Inicio (arriba) de una carpeta contraída.</summary>
    FolderClosed,
    /// <summary>Separador que cierra (abajo) una carpeta.</summary>
    FolderEnd,
}

/// <summary>Máscara de capa de PSD: un rectángulo en gris (0 = oculto) y el valor fuera de él.</summary>
public sealed record PsdMask(int Left, int Top, int Right, int Bottom, byte DefaultColor, byte[] Gray)
{
    public int Width => Right - Left;
    public int Height => Bottom - Top;
}

/// <summary>Un registro de capa de PSD. <see cref="Rgba"/> es RGBA sin premultiplicar del rectángulo de la capa.</summary>
public sealed class PsdLayer
{
    public string Name { get; set; } = "";
    public int Left { get; set; }
    public int Top { get; set; }
    public int Right { get; set; }
    public int Bottom { get; set; }
    public byte Opacity { get; set; } = 255;
    public bool Visible { get; set; } = true;
    public string BlendKey { get; set; } = "norm";
    public bool Clipping { get; set; }
    /// <summary>«Bloquear píxeles transparentes» de Photoshop: el bloqueo alfa de Trazo.</summary>
    public bool TransparencyLocked { get; set; }
    public PsdLayerKind Kind { get; set; }
    public byte[] Rgba { get; set; } = [];
    public PsdMask? Mask { get; set; }
    /// <summary>Capa sin datos de color propios (ajuste o relleno): se omite al importar.</summary>
    public bool NoPixels { get; set; }

    public int Width => Right - Left;
    public int Height => Bottom - Top;
}

/// <summary>Documento PSD: tamaño, capas de abajo arriba (como en el archivo) e imagen compuesta RGBA.</summary>
public sealed class PsdFile
{
    public int Width { get; set; }
    public int Height { get; set; }
    public List<PsdLayer> Layers { get; } = [];
    /// <summary>Imagen compuesta RGBA sin premultiplicar (ancho × alto × 4).</summary>
    public byte[] Composite { get; set; } = [];
    /// <summary>Profundidad del archivo leído (8 o 16).</summary>
    public int SourceDepth { get; set; } = 8;
    public bool SourceGrayscale { get; set; }
}

/// <summary>
/// Lector y escritor de PSD (EX-01) según la «Adobe Photoshop File Formats Specification». Sin dependencias:
/// trabaja con canales RGBA sin premultiplicar.
/// </summary>
public static class Psd
{
    public const int MaxSide = 30000;
    private const int MaxLayers = 8000;

    // ---------- Modos de fusión ----------

    private static readonly (BlendMode Mode, string Key)[] Exact =
    [
        (BlendMode.Normal, "norm"), (BlendMode.Multiply, "mul "), (BlendMode.Darken, "dark"), (BlendMode.ColorBurn, "idiv"),
        (BlendMode.Screen, "scrn"), (BlendMode.Lighten, "lite"), (BlendMode.ColorDodge, "div "), (BlendMode.Overlay, "over"),
        (BlendMode.SoftLight, "sLit"), (BlendMode.HardLight, "hLit"), (BlendMode.Difference, "diff"), (BlendMode.Exclusion, "smud"),
        (BlendMode.Hue, "hue "), (BlendMode.Saturation, "sat "), (BlendMode.Color, "colr"), (BlendMode.Luminosity, "lum "),
    ];

    private static readonly Dictionary<string, BlendMode> Approximate = new()
    {
        ["lbrn"] = BlendMode.ColorBurn, ["lddg"] = BlendMode.ColorDodge, ["dkCl"] = BlendMode.Darken, ["lgCl"] = BlendMode.Lighten,
        ["vLit"] = BlendMode.HardLight, ["lLit"] = BlendMode.HardLight, ["pLit"] = BlendMode.HardLight, ["hMix"] = BlendMode.HardLight,
        ["fsub"] = BlendMode.Difference, ["fdiv"] = BlendMode.ColorDodge, ["diss"] = BlendMode.Normal, ["pass"] = BlendMode.Normal,
    };

    public static string BlendKey(BlendMode m) => Exact.First(e => e.Mode == m).Key;

    /// <summary>Modo de Trazo para una clave de PSD y si es aproximado.</summary>
    public static (BlendMode Mode, bool Approximated) FromBlendKey(string key)
    {
        foreach (var (mode, k) in Exact) if (k == key) return (mode, false);
        return Approximate.TryGetValue(key, out var a) ? (a, key != "pass") : (BlendMode.Normal, true);
    }

    // ---------- PackBits ----------

    /// <summary>Comprime una fila con PackBits.</summary>
    public static void PackBits(ReadOnlySpan<byte> row, List<byte> output)
    {
        var i = 0;
        while (i < row.Length)
        {
            // repetición de al menos 3 bytes iguales
            var run = 1;
            while (i + run < row.Length && run < 128 && row[i + run] == row[i]) run++;
            if (run >= 3)
            {
                output.Add((byte)(257 - run));
                output.Add(row[i]);
                i += run;
                continue;
            }
            // literales hasta la próxima repetición de 3
            var start = i;
            var lit = 0;
            while (i < row.Length && lit < 128)
            {
                if (i + 2 < row.Length && row[i] == row[i + 1] && row[i] == row[i + 2]) break;
                i++;
                lit++;
            }
            output.Add((byte)(lit - 1));
            for (var k = start; k < start + lit; k++) output.Add(row[k]);
        }
    }

    /// <summary>Descomprime PackBits en <paramref name="dest"/>; lanza InvalidDataException si los datos no cuadran.</summary>
    public static void UnpackBits(ReadOnlySpan<byte> src, Span<byte> dest)
    {
        int s = 0, d = 0;
        while (d < dest.Length)
        {
            if (s >= src.Length) throw new InvalidDataException(T("PSD: datos RLE truncados"));
            var n = (sbyte)src[s++];
            if (n >= 0)
            {
                var count = n + 1;
                if (s + count > src.Length || d + count > dest.Length) throw new InvalidDataException(T("PSD: literal RLE fuera de rango"));
                src.Slice(s, count).CopyTo(dest[d..]);
                s += count;
                d += count;
            }
            else if (n != -128)
            {
                var count = 1 - n;
                if (s >= src.Length || d + count > dest.Length) throw new InvalidDataException(T("PSD: repetición RLE fuera de rango"));
                dest.Slice(d, count).Fill(src[s++]);
                d += count;
            }
        }
    }

    // ---------- Escritura ----------

    private sealed class Writer(Stream s)
    {
        private readonly byte[] _b = new byte[8];
        public long Position => s.Position;
        public void U8(byte v) => s.WriteByte(v);
        public void U16(int v) { BinaryPrimitives.WriteUInt16BigEndian(_b, (ushort)v); s.Write(_b, 0, 2); }
        public void I16(int v) { BinaryPrimitives.WriteInt16BigEndian(_b, (short)v); s.Write(_b, 0, 2); }
        public void U32(long v) { BinaryPrimitives.WriteUInt32BigEndian(_b, checked((uint)v)); s.Write(_b, 0, 4); }
        public void I32(int v) { BinaryPrimitives.WriteInt32BigEndian(_b, v); s.Write(_b, 0, 4); }
        public void Ascii(string v) => s.Write(Encoding.ASCII.GetBytes(v));
        public void Bytes(ReadOnlySpan<byte> v) => s.Write(v);
        public void Zeros(int n) { for (var i = 0; i < n; i++) s.WriteByte(0); }

        /// <summary>Escribe un hueco de 4 bytes para una longitud y devuelve una acción que la rellena.</summary>
        public Action Length32(int align = 1)
        {
            var at = s.Position;
            U32(0);
            return () =>
            {
                var end = s.Position;
                var len = end - at - 4;
                while (len % align != 0)
                {
                    s.WriteByte(0);
                    len++;
                }
                end = s.Position;
                if (len > uint.MaxValue) throw new InvalidDataException(T("El documento es demasiado grande para PSD (más de 4 GB en una sección)."));
                s.Position = at;
                U32(len);
                s.Position = end;
            };
        }
    }

    /// <summary>Canal comprimido: compresión 1 (RLE), cuentas de fila y filas.</summary>
    private static byte[] EncodeChannel(Func<int, int, byte> value, int width, int height)
    {
        var counts = new List<byte>(height * 2);
        var data = new List<byte>();
        var row = new byte[width];
        var packed = new List<byte>();
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++) row[x] = value(x, y);
            packed.Clear();
            PackBits(row, packed);
            counts.Add((byte)(packed.Count >> 8));
            counts.Add((byte)packed.Count);
            data.AddRange(packed);
        }
        var o = new byte[2 + counts.Count + data.Count];
        o[1] = 1;
        counts.CopyTo(o, 2);
        data.CopyTo(o, 2 + counts.Count);
        return o;
    }

    private static byte[] PascalName(string name)
    {
        var ascii = Encoding.ASCII.GetBytes(new string(name.Select(c => c < 128 ? c : '_').Take(255).ToArray()));
        var len = 1 + ascii.Length;
        var padded = (len + 3) / 4 * 4;
        var o = new byte[padded];
        o[0] = (byte)ascii.Length;
        ascii.CopyTo(o, 1);
        return o;
    }

    /// <summary>Escribe el PSD. Las capas van de abajo arriba; la imagen compuesta se guarda en RGB.</summary>
    public static void Write(Stream stream, PsdFile psd)
    {
        if (psd.Width <= 0 || psd.Height <= 0 || psd.Width > MaxSide || psd.Height > MaxSide)
            throw new ArgumentException(T("Tamaño de lienzo no válido para PSD."));
        if (psd.Composite.Length != psd.Width * psd.Height * 4) throw new ArgumentException(T("La imagen compuesta no tiene el tamaño del lienzo."));
        var w = new Writer(stream);
        w.Ascii("8BPS");
        w.U16(1);
        w.Zeros(6);
        w.U16(3);
        w.U32(psd.Height);
        w.U32(psd.Width);
        w.U16(8);
        w.U16(3); // RGB
        w.U32(0); // datos de modo de color
        w.U32(0); // recursos de imagen

        var layerAndMask = w.Length32();
        var layerInfo = w.Length32(align: 2);
        w.I16(psd.Layers.Count);
        // los datos de los canales se preparan antes: el registro necesita sus longitudes
        var channelData = new List<List<(short Id, byte[] Data)>>();
        foreach (var l in psd.Layers)
        {
            var channels = new List<(short, byte[])>();
            if (l.Kind == PsdLayerKind.Pixels && l.Width > 0 && l.Height > 0)
            {
                if (l.Rgba.Length != l.Width * l.Height * 4) throw new ArgumentException(F("La capa «{0}» no tiene el tamaño de su rectángulo.", l.Name));
                var lw = l.Width;
                var px = l.Rgba;
                channels.Add((-1, EncodeChannel((x, y) => px[(y * lw + x) * 4 + 3], l.Width, l.Height)));
                for (var c = 0; c < 3; c++)
                {
                    var cc = c;
                    channels.Add(((short)c, EncodeChannel((x, y) => px[(y * lw + x) * 4 + cc], l.Width, l.Height)));
                }
            }
            else
            {
                // carpetas y capas vacías: cuatro canales sin datos
                foreach (var id in new short[] { -1, 0, 1, 2 }) channels.Add((id, [0, 0]));
            }
            if (l.Mask is { } m && m.Width > 0 && m.Height > 0)
            {
                var mw = m.Width;
                channels.Add((-2, EncodeChannel((x, y) => m.Gray[y * mw + x], m.Width, m.Height)));
            }
            channelData.Add(channels);
        }
        for (var i = 0; i < psd.Layers.Count; i++)
        {
            var l = psd.Layers[i];
            var pixels = l.Kind == PsdLayerKind.Pixels;
            w.I32(pixels ? l.Top : 0);
            w.I32(pixels ? l.Left : 0);
            w.I32(pixels ? l.Bottom : 0);
            w.I32(pixels ? l.Right : 0);
            w.U16(channelData[i].Count);
            foreach (var (id, data) in channelData[i])
            {
                w.I16(id);
                w.U32(data.Length);
            }
            w.Ascii("8BIM");
            w.Ascii(l.BlendKey);
            w.U8(l.Opacity);
            w.U8((byte)(l.Clipping ? 1 : 0));
            w.U8((byte)((l.TransparencyLocked ? 0x01 : 0) | (l.Visible ? 0 : 0x02) | 0x08 | (pixels ? 0 : 0x10)));
            w.U8(0);
            var extra = w.Length32();
            if (l.Mask is { } m)
            {
                w.U32(20);
                w.I32(m.Top);
                w.I32(m.Left);
                w.I32(m.Bottom);
                w.I32(m.Right);
                w.U8(m.DefaultColor);
                w.U8(0);
                w.Zeros(2);
            }
            else w.U32(0);
            w.U32(0); // rangos de fusión
            var name = l.Kind == PsdLayerKind.FolderEnd ? "</Layer group>" : l.Name;
            w.Bytes(PascalName(name));
            // nombre Unicode
            w.Ascii("8BIMluni");
            var luni = w.Length32(align: 4);
            w.U32(name.Length);
            foreach (var ch in name) w.U16(ch);
            luni();
            if (!pixels)
            {
                w.Ascii("8BIMlsct");
                var lsct = w.Length32();
                w.U32(l.Kind switch { PsdLayerKind.FolderOpen => 1, PsdLayerKind.FolderClosed => 2, _ => 3 });
                if (l.Kind != PsdLayerKind.FolderEnd)
                {
                    w.Ascii("8BIM");
                    w.Ascii(l.BlendKey);
                }
                lsct();
            }
            extra();
        }
        foreach (var channels in channelData)
            foreach (var (_, data) in channels) w.Bytes(data);
        layerInfo();
        w.U32(0); // información global de máscara
        layerAndMask();

        // imagen compuesta: RLE, todas las cuentas de fila primero y después los canales R, G y B
        var width = psd.Width;
        var height = psd.Height;
        var comp = psd.Composite;
        var rows = new List<byte[]>();
        var counts = new List<byte>();
        var packed = new List<byte>();
        var row = new byte[width];
        for (var c = 0; c < 3; c++)
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++) row[x] = comp[(y * width + x) * 4 + c];
                packed.Clear();
                PackBits(row, packed);
                counts.Add((byte)(packed.Count >> 8));
                counts.Add((byte)packed.Count);
                rows.Add(packed.ToArray());
            }
        w.U16(1);
        w.Bytes(counts.ToArray());
        foreach (var r in rows) w.Bytes(r);
    }

    // ---------- Lectura ----------

    private ref struct Reader(ReadOnlySpan<byte> data)
    {
        private readonly ReadOnlySpan<byte> _d = data;
        public int Pos;

        public readonly int Remaining => _d.Length - Pos;

        private ReadOnlySpan<byte> Take(int n)
        {
            if (n < 0 || n > Remaining) throw new InvalidDataException(T("PSD: el archivo está truncado o dañado"));
            var s = _d.Slice(Pos, n);
            Pos += n;
            return s;
        }

        public byte U8() => Take(1)[0];
        public int U16() => BinaryPrimitives.ReadUInt16BigEndian(Take(2));
        public int I16() => BinaryPrimitives.ReadInt16BigEndian(Take(2));
        public int I32() => BinaryPrimitives.ReadInt32BigEndian(Take(4));
        public long U32() => BinaryPrimitives.ReadUInt32BigEndian(Take(4));
        public string Ascii(int n) => Encoding.ASCII.GetString(Take(n));
        public ReadOnlySpan<byte> Bytes(int n) => Take(n);

        public int Len32()
        {
            var v = U32();
            if (v > Remaining) throw new InvalidDataException(T("PSD: una sección dice medir más que el resto del archivo"));
            return (int)v;
        }

        public void Skip(int n) => Take(n);
    }

    /// <summary>Lee un PSD. Lanza InvalidDataException con un mensaje claro si no es válido o no está soportado.</summary>
    public static PsdFile Read(Stream stream)
    {
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return Read(ms.GetBuffer().AsSpan(0, (int)ms.Length));
    }

    public static PsdFile Read(ReadOnlySpan<byte> data)
    {
        var r = new Reader(data);
        if (r.Remaining < 26 || r.Ascii(4) != "8BPS") throw new InvalidDataException(T("No es un archivo PSD."));
        var version = r.U16();
        if (version == 2) throw new InvalidDataException(T("Los archivos PSB (documento grande) no están soportados; guárdalo como PSD."));
        if (version != 1) throw new InvalidDataException(T("Versión de PSD desconocida."));
        r.Skip(6);
        var channels = r.U16();
        var height = (int)r.U32();
        var width = (int)r.U32();
        var depth = r.U16();
        var mode = r.U16();
        if (width <= 0 || height <= 0 || width > MaxSide || height > MaxSide) throw new InvalidDataException(T("Tamaño de lienzo no válido en el PSD."));
        if (mode is not (3 or 1)) throw new InvalidDataException(mode switch
        {
            4 => T("Los PSD en CMYK no están soportados; conviértelo a RGB."),
            9 => T("Los PSD en Lab no están soportados; conviértelo a RGB."),
            2 => T("Los PSD en color indexado no están soportados; conviértelo a RGB."),
            _ => T("Modo de color de PSD no soportado; conviértelo a RGB."),
        });
        if (depth is not (8 or 16)) throw new InvalidDataException(depth == 32 ? T("Los PSD de 32 bits no están soportados; conviértelo a 8 o 16 bits.") : T("Profundidad de PSD no soportada."));
        if (channels < (mode == 3 ? 3 : 1) || channels > 56) throw new InvalidDataException(T("Número de canales no válido en el PSD."));
        var gray = mode == 1;
        var psd = new PsdFile { Width = width, Height = height, SourceDepth = depth, SourceGrayscale = gray };
        var bpc = depth / 8;

        r.Skip(r.Len32()); // datos de modo de color
        r.Skip(r.Len32()); // recursos de imagen

        var lmLength = r.Len32();
        var lmEnd = r.Pos + lmLength;
        if (lmLength > 0)
        {
            var liLength = r.Len32();
            var liEnd = r.Pos + liLength;
            if (liLength > 0) ReadLayers(ref r, psd, bpc, gray);
            r.Pos = liEnd;
            // Photoshop guarda las capas de 16 y 32 bits en un bloque «Lr16»/«Lr32» después de la máscara global
            if (psd.Layers.Count == 0 && r.Pos + 4 <= lmEnd)
            {
                r.Skip(r.Len32());
                while (r.Pos + 12 <= lmEnd)
                {
                    var sig = r.Ascii(4);
                    if (sig != "8BIM" && sig != "8B64") break;
                    var key = r.Ascii(4);
                    var len = r.Len32();
                    var start = r.Pos;
                    if (key is "Lr16" or "Layr" && len > 2) ReadLayers(ref r, psd, bpc, gray);
                    r.Pos = start + len;
                    while ((r.Pos & 1) != 0 && r.Pos < lmEnd) r.Pos++;
                    if (psd.Layers.Count > 0) break;
                }
            }
        }
        r.Pos = lmEnd;

        // imagen compuesta
        var compression = r.U16();
        if (compression is 2 or 3) throw new InvalidDataException(T("Los PSD con compresión ZIP no están soportados; guárdalo con compresión RLE o sin comprimir."));
        if (compression > 3) throw new InvalidDataException(T("Compresión de PSD desconocida."));
        if ((long)channels * MinEncoded(width * bpc, height, compression) > r.Remaining) throw new InvalidDataException(T("PSD: la imagen compuesta está truncada."));
        // solo color: un canal extra en la imagen compuesta suele ser una selección guardada, no transparencia
        var planes = gray ? 1 : Math.Min(channels, 3);
        var plane = new byte[width * height * bpc];
        var composite = new byte[width * height * 4];
        for (var i = 0; i < width * height; i++) composite[i * 4 + 3] = 255;
        int[]? rowCounts = null;
        if (compression == 1)
        {
            rowCounts = new int[channels * height];
            for (var i = 0; i < rowCounts.Length; i++) rowCounts[i] = r.U16();
        }
        for (var c = 0; c < channels; c++)
        {
            ReadPlane(ref r, compression, rowCounts, c * height, width * bpc, height, plane);
            if (c >= planes) continue;
            var target = gray ? -1 : c;
            for (var i = 0; i < width * height; i++)
            {
                var v = plane[i * bpc]; // 16 bits → byte alto
                if (target == -1) composite[i * 4] = composite[i * 4 + 1] = composite[i * 4 + 2] = v;
                else composite[i * 4 + target] = v;
            }
        }
        psd.Composite = composite;
        return psd;
    }

    /// <summary>
    /// Bytes mínimos que ocupa un plano de <paramref name="height"/> filas: sin comprimir, todo; con RLE, dos bytes por
    /// cada 128 de la fila más la cuenta de fila. Así nunca se reserva más de unas 64 veces lo que mide el archivo.
    /// </summary>
    private static long MinEncoded(int rowBytes, int height, int compression) =>
        compression == 0 ? (long)rowBytes * height : (long)height * (2 + 2 * ((rowBytes + 127) / 128));

    private static void ReadPlane(ref Reader r, int compression, int[]? rowCounts, int firstRow, int rowBytes, int height, Span<byte> dest)
    {
        if (compression == 0)
        {
            r.Bytes(rowBytes * height).CopyTo(dest);
            return;
        }
        for (var y = 0; y < height; y++)
        {
            var n = rowCounts![firstRow + y];
            UnpackBits(r.Bytes(n), dest.Slice(y * rowBytes, rowBytes));
        }
    }

    private static void ReadLayers(ref Reader r, PsdFile psd, int bpc, bool gray)
    {
        var count = Math.Abs(r.I16());
        if (count > MaxLayers) throw new InvalidDataException(T("El PSD tiene demasiadas capas."));
        var records = new List<(PsdLayer Layer, List<(int Id, long Length)> Channels, (int L, int T, int R, int B, byte Def)? MaskRect)>();
        for (var i = 0; i < count; i++)
        {
            var l = new PsdLayer { Top = r.I32(), Left = r.I32(), Bottom = r.I32(), Right = r.I32() };
            if (l.Width < 0 || l.Height < 0 || l.Width > MaxSide || l.Height > MaxSide) throw new InvalidDataException(T("Rectángulo de capa no válido en el PSD."));
            var nch = r.U16();
            if (nch > 56) throw new InvalidDataException(T("Demasiados canales en una capa del PSD."));
            var chans = new List<(int, long)>();
            for (var c = 0; c < nch; c++) chans.Add((r.I16(), r.U32()));
            if (r.Ascii(4) != "8BIM") throw new InvalidDataException(T("PSD: registro de capa dañado."));
            l.BlendKey = r.Ascii(4);
            l.Opacity = r.U8();
            l.Clipping = r.U8() != 0;
            var flags = r.U8();
            l.Visible = (flags & 0x02) == 0;
            l.TransparencyLocked = (flags & 0x01) != 0;
            r.U8();
            var extraLen = r.Len32();
            var extraEnd = r.Pos + extraLen;
            (int, int, int, int, byte)? mask = null;
            var maskLen = r.Len32();
            if (maskLen >= 18)
            {
                var mt = r.I32();
                var ml = r.I32();
                var mb = r.I32();
                var mr = r.I32();
                var def = r.U8();
                r.Skip(maskLen - 17);
                if (mr - ml >= 0 && mb - mt >= 0 && mr - ml <= MaxSide && mb - mt <= MaxSide) mask = (ml, mt, mr, mb, def);
            }
            else r.Skip(maskLen);
            r.Skip(r.Len32()); // rangos de fusión
            var nameLen = r.U8();
            l.Name = Encoding.Latin1.GetString(r.Bytes(nameLen));
            r.Skip((4 - (1 + nameLen) % 4) % 4);
            // información adicional: nombre Unicode, carpetas y capas sin píxeles
            while (r.Pos + 12 <= extraEnd)
            {
                var sig = r.Ascii(4);
                if (sig != "8BIM" && sig != "8B64") break;
                var key = r.Ascii(4);
                var len = r.Len32();
                var start = r.Pos;
                switch (key)
                {
                    case "luni" when len >= 4:
                        var chars = r.U32();
                        if (chars * 2 <= len - 4)
                        {
                            var sb = new StringBuilder((int)chars);
                            for (var k = 0; k < chars; k++) sb.Append((char)r.U16());
                            l.Name = sb.ToString().TrimEnd('\0');
                        }
                        break;
                    case "lsct" or "lsdk" when len >= 4:
                        var type = (int)r.U32();
                        l.Kind = type switch { 1 => PsdLayerKind.FolderOpen, 2 => PsdLayerKind.FolderClosed, 3 => PsdLayerKind.FolderEnd, _ => PsdLayerKind.Pixels };
                        if (len >= 12 && l.Kind is PsdLayerKind.FolderOpen or PsdLayerKind.FolderClosed && r.Ascii(4) == "8BIM") l.BlendKey = r.Ascii(4);
                        break;
                    case "SoCo" or "GdFl" or "PtFl" or "levl" or "curv" or "brit" or "hue2" or "blnc" or "vibA" or "mixr" or "selc" or "thrs" or "nvrt" or "post" or "grdm" or "phfl" or "expA" or "clrL":
                        l.NoPixels = true;
                        break;
                }
                r.Pos = start + len;
                if (len % 2 != 0 && r.Pos < extraEnd) r.Pos++;
            }
            r.Pos = extraEnd;
            records.Add((l, chans, mask));
        }

        foreach (var (l, chans, maskRect) in records)
        {
            var w = l.Width;
            var h = l.Height;
            if ((long)w * h * 4 > int.MaxValue) throw new InvalidDataException(T("Capa demasiado grande en el PSD."));
            foreach (var (id, length) in chans)
            {
                var (cw0, ch0) = id == -2 ? (maskRect is { } mr0 ? (mr0.R - mr0.L, mr0.B - mr0.T) : (0, 0)) : (w, h);
                if (id < -2 || cw0 == 0 || ch0 == 0) continue;
                if (2 + MinEncoded(cw0 * bpc, ch0, 1) > length && 2 + MinEncoded(cw0 * bpc, ch0, 0) > length)
                    throw new InvalidDataException(T("PSD: datos de canal truncados."));
            }
            var rgba = new byte[w * h * 4];
            var hasAlpha = false;
            byte[]? maskGray = null;
            foreach (var (id, length) in chans)
            {
                if (length < 2 || length > r.Remaining) throw new InvalidDataException(T("PSD: datos de canal truncados."));
                var end = r.Pos + (int)length;
                var compression = r.U16();
                var isMask = id == -2;
                var cw = isMask ? (maskRect is { } mr1 ? mr1.R - mr1.L : 0) : w;
                var chh = isMask ? (maskRect is { } mr2 ? mr2.B - mr2.T : 0) : h;
                if (id < -2 || cw == 0 || chh == 0)
                {
                    r.Pos = end;
                    continue;
                }
                if (compression is 2 or 3) throw new InvalidDataException(T("Los PSD con compresión ZIP no están soportados; guárdalo con compresión RLE o sin comprimir."));
                if (compression > 3) throw new InvalidDataException(T("Compresión de PSD desconocida."));
                var plane = new byte[cw * chh * bpc];
                int[]? counts = null;
                if (compression == 1)
                {
                    counts = new int[chh];
                    for (var y = 0; y < chh; y++) counts[y] = r.U16();
                }
                ReadPlane(ref r, compression, counts, 0, cw * bpc, chh, plane);
                r.Pos = end;
                if (isMask)
                {
                    maskGray = new byte[cw * chh];
                    for (var i = 0; i < maskGray.Length; i++) maskGray[i] = plane[i * bpc];
                    continue;
                }
                var target = id == -1 ? 3 : gray ? (id == 0 ? -1 : 99) : id;
                if (target > 3) continue;
                if (id == -1) hasAlpha = true;
                for (var i = 0; i < w * h; i++)
                {
                    var v = plane[i * bpc];
                    if (target == -1) rgba[i * 4] = rgba[i * 4 + 1] = rgba[i * 4 + 2] = v;
                    else rgba[i * 4 + target] = v;
                }
            }
            if (!hasAlpha) for (var i = 0; i < w * h; i++) rgba[i * 4 + 3] = 255;
            l.Rgba = rgba;
            if (maskRect is { } m && maskGray is not null) l.Mask = new PsdMask(m.L, m.T, m.R, m.B, m.Def, maskGray);
            psd.Layers.Add(l);
        }
    }

    // ---------- Alfa ----------

    /// <summary>RGBA sin premultiplicar (PSD) → BGRA premultiplicado (GPU), redondeando. Único sitio de esta conversión.</summary>
    public static void ToPremultipliedBgra(ReadOnlySpan<byte> rgba, Span<byte> bgra)
    {
        for (var i = 0; i < rgba.Length; i += 4)
        {
            var a = rgba[i + 3];
            bgra[i] = (byte)((rgba[i + 2] * a + 127) / 255);
            bgra[i + 1] = (byte)((rgba[i + 1] * a + 127) / 255);
            bgra[i + 2] = (byte)((rgba[i] * a + 127) / 255);
            bgra[i + 3] = a;
        }
    }

    /// <summary>BGRA premultiplicado (GPU) → RGBA sin premultiplicar (PSD), redondeando. Alfa 0 da negro transparente.</summary>
    public static void ToStraightRgba(ReadOnlySpan<byte> bgra, Span<byte> rgba)
    {
        for (var i = 0; i < bgra.Length; i += 4)
        {
            var a = bgra[i + 3];
            if (a == 0)
            {
                rgba.Slice(i, 4).Clear();
                continue;
            }
            rgba[i] = (byte)Math.Min(255, (bgra[i + 2] * 255 + a / 2) / a);
            rgba[i + 1] = (byte)Math.Min(255, (bgra[i + 1] * 255 + a / 2) / a);
            rgba[i + 2] = (byte)Math.Min(255, (bgra[i] * 255 + a / 2) / a);
            rgba[i + 3] = a;
        }
    }

    // ---------- Colocación ----------

    /// <summary>
    /// Copia un rectángulo de píxeles (con <paramref name="bpp"/> bytes por píxel) sobre un lienzo nuevo, recortando lo
    /// que queda fuera. El resto del lienzo se llena con <paramref name="fill"/>.
    /// </summary>
    public static byte[] Place(byte[] src, int left, int top, int width, int height, int canvasW, int canvasH, int bpp, byte fill)
    {
        var o = new byte[canvasW * canvasH * bpp];
        if (fill != 0) Array.Fill(o, fill);
        var x0 = Math.Max(0, left);
        var y0 = Math.Max(0, top);
        var x1 = Math.Min(canvasW, left + width);
        var y1 = Math.Min(canvasH, top + height);
        if (x1 <= x0 || y1 <= y0) return o;
        var bytes = (x1 - x0) * bpp;
        for (var y = y0; y < y1; y++)
            Buffer.BlockCopy(src, ((y - top) * width + (x0 - left)) * bpp, o, (y * canvasW + x0) * bpp, bytes);
        return o;
    }

    /// <summary>Rectángulo mínimo con alfa distinto de cero de una imagen RGBA del lienzo; (0,0,0,0) si está vacía.</summary>
    public static (int Left, int Top, int Right, int Bottom) OpaqueBounds(ReadOnlySpan<byte> rgba, int width, int height)
    {
        int l = width, t = height, r = 0, b = 0;
        for (var y = 0; y < height; y++)
        {
            var row = rgba.Slice(y * width * 4, width * 4);
            for (var x = 0; x < width; x++)
            {
                if (row[x * 4 + 3] == 0) continue;
                if (x < l) l = x;
                if (x >= r) r = x + 1;
                if (y < t) t = y;
                b = y + 1;
            }
        }
        return r == 0 ? (0, 0, 0, 0) : (l, t, r, b);
    }

    /// <summary>Recorta una imagen RGBA del lienzo a su contenido y la devuelve como capa con ese rectángulo.</summary>
    public static void SetPixelsCropped(PsdLayer layer, byte[] canvasRgba, int width, int height)
    {
        var (l, t, r, b) = OpaqueBounds(canvasRgba, width, height);
        layer.Left = l;
        layer.Top = t;
        layer.Right = r;
        layer.Bottom = b;
        var w = r - l;
        var data = new byte[w * (b - t) * 4];
        for (var y = t; y < b; y++) Buffer.BlockCopy(canvasRgba, (y * width + l) * 4, data, (y - t) * w * 4, w * 4);
        layer.Rgba = data;
    }

    // ---------- Estructura de Trazo ----------

    /// <summary>Capa importada en el orden de Trazo (de abajo arriba), con su grupo de un solo nivel.</summary>
    public sealed record TrazoLayer(PsdLayer Layer, int? Group);

    /// <summary>Grupo de Trazo creado a partir de la carpeta más exterior.</summary>
    public sealed record TrazoGroup(string Name, byte Opacity, string BlendKey, bool Visible, bool Collapsed);

    public sealed record TrazoStructure(List<TrazoLayer> Layers, List<TrazoGroup> Groups, List<string> Warnings);

    /// <summary>
    /// Convierte las capas del PSD a la estructura de Trazo: carpetas de un nivel (las interiores se aplanan en la
    /// exterior), capas sin píxeles omitidas, modos aproximados y avisos para todo lo que cambia.
    /// </summary>
    public static TrazoStructure ToTrazo(PsdFile psd)
    {
        var layers = new List<TrazoLayer>();
        var groups = new List<TrazoGroup>();
        var warnings = new List<string>();
        int skipped = 0, approximated = 0, nested = 0, passThrough = 0;
        // el archivo va de abajo arriba: un separador abre una carpeta y el registro de carpeta la cierra
        var stack = new Stack<int>();
        var pendingMembers = new List<List<int>>();
        foreach (var l in psd.Layers)
        {
            switch (l.Kind)
            {
                case PsdLayerKind.FolderEnd:
                    stack.Push(layers.Count);
                    break;
                case PsdLayerKind.FolderOpen or PsdLayerKind.FolderClosed:
                    if (stack.Count == 0) break; // carpeta sin separador: se ignora
                    var from = stack.Pop();
                    if (stack.Count > 0)
                    {
                        nested++;
                        break; // carpeta interior: sus capas quedan en la exterior
                    }
                    var (_, approx) = FromBlendKey(l.BlendKey);
                    if (l.BlendKey == "pass") passThrough++;
                    else if (approx) approximated++;
                    var gi = groups.Count;
                    groups.Add(new TrazoGroup(l.Name, l.Opacity, l.BlendKey, l.Visible, l.Kind == PsdLayerKind.FolderClosed));
                    for (var k = from; k < layers.Count; k++) layers[k] = layers[k] with { Group = gi };
                    break;
                default:
                    if (l.NoPixels)
                    {
                        skipped++;
                        break;
                    }
                    if (FromBlendKey(l.BlendKey).Approximated) approximated++;
                    layers.Add(new TrazoLayer(l, null));
                    break;
            }
        }
        // grupos que quedaron vacíos (solo tenían capas omitidas): se quitan
        var used = layers.Where(t => t.Group is not null).Select(t => t.Group!.Value).ToHashSet();
        if (used.Count != groups.Count)
        {
            var remap = new Dictionary<int, int>();
            var kept = new List<TrazoGroup>();
            for (var g = 0; g < groups.Count; g++)
            {
                if (!used.Contains(g)) continue;
                remap[g] = kept.Count;
                kept.Add(groups[g]);
            }
            layers = layers.Select(t => t with { Group = t.Group is { } g ? remap[g] : null }).ToList();
            groups = kept;
        }
        if (skipped > 0) warnings.Add(F("{0} capas de ajuste o relleno no se importaron.", skipped));
        if (approximated > 0) warnings.Add(F("{0} modos de fusión sin equivalente se aproximaron.", approximated));
        if (nested > 0) warnings.Add(F("{0} grupos anidados se juntaron en su grupo exterior.", nested));
        if (passThrough > 0) warnings.Add(F("{0} grupos en «Pasar a través» se importaron como grupos normales.", passThrough));
        return new TrazoStructure(layers, groups, warnings);
    }
}
