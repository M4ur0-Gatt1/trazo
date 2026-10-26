using System.Text;
using Trazo.Core.Documents;
using Trazo.Core.IO;

namespace Trazo.Core.Tests;

/// <summary>EX-01 — Importar y exportar PSD (docs/specs/EX-01-psd.md), parte sin GPU.</summary>
public sealed class EX01_PsdTests
{
    // ---------- utilidades ----------

    /// <summary>Escritor big-endian para fabricar PSD que Trazo no escribe (gris, 16 bits, CMYK…).</summary>
    private sealed class Be
    {
        public readonly List<byte> B = [];
        public Be U8(int v) { B.Add((byte)v); return this; }
        public Be U16(int v) { B.Add((byte)(v >> 8)); B.Add((byte)v); return this; }
        public Be U32(long v) { for (var i = 3; i >= 0; i--) B.Add((byte)(v >> (8 * i))); return this; }
        public Be Ascii(string s) { B.AddRange(Encoding.ASCII.GetBytes(s)); return this; }
        public Be Raw(IEnumerable<byte> v) { B.AddRange(v); return this; }
        public byte[] ToArray() => B.ToArray();
    }

    private static Be Header(int version, int channels, int w, int h, int depth, int mode) =>
        new Be().Ascii("8BPS").U16(version).U32(0).U16(0).U16(channels).U32(h).U32(w).U16(depth).U16(mode).U32(0).U32(0);

    internal static byte[] Rgba(int w, int h, Func<int, int, (byte R, byte G, byte B, byte A)> f)
    {
        var o = new byte[w * h * 4];
        for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
            {
                var (r, g, b, a) = f(x, y);
                var i = (y * w + x) * 4;
                o[i] = r;
                o[i + 1] = g;
                o[i + 2] = b;
                o[i + 3] = a;
            }
        return o;
    }

    internal static byte[] Save(PsdFile psd)
    {
        using var ms = new MemoryStream();
        Psd.Write(ms, psd);
        return ms.ToArray();
    }

    /// <summary>Documento con todo: grupo con máscara y recorte, nombres Unicode, rectángulos parciales.</summary>
    internal static PsdFile Sample()
    {
        const int w = 40, h = 30;
        var psd = new PsdFile { Width = w, Height = h, Composite = Rgba(w, h, (x, y) => ((byte)(x * 6), (byte)(y * 8), 90, 255)) };
        psd.Layers.Add(new PsdLayer
        {
            Name = "Fondo", Left = 0, Top = 0, Right = w, Bottom = h,
            Rgba = Rgba(w, h, (_, _) => (250, 245, 235, 255)),
        });
        psd.Layers.Add(new PsdLayer { Kind = PsdLayerKind.FolderEnd });
        psd.Layers.Add(new PsdLayer
        {
            Name = "Línea — ñandú 🦤", Left = 3, Top = 4, Right = 23, Bottom = 14, Opacity = 200, BlendKey = "mul ",
            Rgba = Rgba(20, 10, (x, y) => ((byte)(x * 12), (byte)(y * 25), 7, (byte)(x * 13))),
            Mask = new PsdMask(0, 0, w, h, 255, Enumerable.Range(0, w * h).Select(i => (byte)(i % 256)).ToArray()),
        });
        psd.Layers.Add(new PsdLayer
        {
            Name = "Sombra", Left = 10, Top = 10, Right = 12, Bottom = 11, Clipping = true, Visible = false, BlendKey = "lum ",
            Rgba = [1, 2, 3, 4, 5, 6, 7, 8],
        });
        psd.Layers.Add(new PsdLayer { Name = "Personaje", Kind = PsdLayerKind.FolderClosed, Opacity = 128, BlendKey = "scrn", Visible = false });
        return psd;
    }

    // ---------- EX-01.1 ----------

    [Fact]
    public void PackBitsRoundTrips()
    {
        var rng = new Random(7);
        List<byte[]> rows =
        [
            [], [5], [5, 5], [5, 5, 5], [1, 2, 3], Enumerable.Repeat((byte)9, 128).ToArray(),
            Enumerable.Repeat((byte)9, 129).ToArray(), Enumerable.Repeat((byte)9, 1000).ToArray(),
            Enumerable.Range(0, 300).Select(i => (byte)i).ToArray(),
        ];
        for (var i = 0; i < 200; i++)
        {
            var row = new byte[rng.Next(1, 700)];
            // mezcla de tramos repetidos y ruido, que es lo que más pone a prueba los límites de 128
            for (var k = 0; k < row.Length; k++) row[k] = rng.Next(3) == 0 ? (byte)rng.Next(256) : (byte)(k / rng.Next(1, 200) % 3);
            rows.Add(row);
        }
        foreach (var row in rows)
        {
            var packed = new List<byte>();
            Psd.PackBits(row, packed);
            var back = new byte[row.Length];
            Psd.UnpackBits(packed.ToArray(), back);
            Assert.Equal(row, back);
            // nunca crece más de lo que permite PackBits: 1 byte por cada 128
            Assert.True(packed.Count <= row.Length + (row.Length + 127) / 128);
        }
        // 1000 bytes iguales caben en 8 repeticiones de 2 bytes
        var flat = new List<byte>();
        Psd.PackBits(Enumerable.Repeat((byte)0, 1000).ToArray(), flat);
        Assert.Equal(16, flat.Count);
    }

    // ---------- EX-01.2 ----------

    [Fact]
    public void PsdRoundTripPreservesEverything()
    {
        var src = Sample();
        var back = Psd.Read(Save(src));
        Assert.Equal(src.Width, back.Width);
        Assert.Equal(src.Height, back.Height);
        Assert.Equal(src.Composite, back.Composite);
        Assert.Equal(src.Layers.Count, back.Layers.Count);
        for (var i = 0; i < src.Layers.Count; i++)
        {
            var a = src.Layers[i];
            var b = back.Layers[i];
            Assert.Equal(a.Kind, b.Kind);
            Assert.Equal(a.Kind == PsdLayerKind.FolderEnd ? "</Layer group>" : a.Name, b.Name);
            Assert.Equal(a.Opacity, b.Opacity);
            Assert.Equal(a.Visible, b.Visible);
            Assert.Equal(a.BlendKey, b.BlendKey);
            Assert.Equal(a.Clipping, b.Clipping);
            if (a.Kind != PsdLayerKind.Pixels) continue;
            Assert.Equal((a.Left, a.Top, a.Right, a.Bottom), (b.Left, b.Top, b.Right, b.Bottom));
            Assert.Equal(a.Rgba, b.Rgba);
            Assert.Equal(a.Mask?.Gray, b.Mask?.Gray);
            Assert.Equal(a.Mask?.DefaultColor, b.Mask?.DefaultColor);
        }

        // y la estructura de Trazo: un grupo contraído e invisible con las dos capas de dentro
        var t = Psd.ToTrazo(back);
        Assert.Equal(["Fondo", "Línea — ñandú 🦤", "Sombra"], t.Layers.Select(l => l.Layer.Name));
        Assert.Equal([null, 0, 0], t.Layers.Select(l => l.Group));
        var g = Assert.Single(t.Groups);
        Assert.Equal(("Personaje", (byte)128, "scrn", false, true), (g.Name, g.Opacity, g.BlendKey, g.Visible, g.Collapsed));
        Assert.Empty(t.Warnings);
    }

    [Fact]
    public void NestedFoldersFlattenToOuterGroupWithWarning()
    {
        var psd = new PsdFile { Width = 2, Height = 2, Composite = new byte[16] };
        PsdLayer Px(string n) => new() { Name = n, Right = 2, Bottom = 2, Rgba = new byte[16] };
        psd.Layers.Add(Px("suelta"));
        psd.Layers.Add(new PsdLayer { Kind = PsdLayerKind.FolderEnd });
        psd.Layers.Add(Px("a"));
        psd.Layers.Add(new PsdLayer { Kind = PsdLayerKind.FolderEnd });
        psd.Layers.Add(Px("b"));
        psd.Layers.Add(new PsdLayer { Name = "interior", Kind = PsdLayerKind.FolderOpen, BlendKey = "pass" });
        psd.Layers.Add(Px("c"));
        psd.Layers.Add(new PsdLayer { Name = "exterior", Kind = PsdLayerKind.FolderOpen, BlendKey = "pass" });
        psd.Layers.Add(new PsdLayer { Kind = PsdLayerKind.FolderEnd });
        psd.Layers.Add(new PsdLayer { Name = "ajuste", NoPixels = true, Right = 2, Bottom = 2, Rgba = new byte[16] });
        psd.Layers.Add(new PsdLayer { Name = "solo ajustes", Kind = PsdLayerKind.FolderOpen });

        var read = Psd.Read(Save(psd));
        // Trazo no escribe capas de ajuste: la marca se copia del original
        for (var i = 0; i < psd.Layers.Count; i++) read.Layers[i].NoPixels = psd.Layers[i].NoPixels;
        var t = Psd.ToTrazo(read);
        Assert.Equal(["suelta", "a", "b", "c"], t.Layers.Select(l => l.Layer.Name));
        Assert.Equal([null, 0, 0, 0], t.Layers.Select(l => l.Group));
        Assert.Equal("exterior", Assert.Single(t.Groups).Name);
        Assert.Contains(t.Warnings, w => w.Contains("anidados"));
        Assert.Contains(t.Warnings, w => w.Contains("Pasar a través"));
        Assert.Contains(t.Warnings, w => w.Contains("ajuste"));
    }

    // ---------- EX-01.3 ----------

    [Fact]
    public void BlendKeysMapBothWays()
    {
        var keys = new HashSet<string>();
        foreach (var m in Enum.GetValues<BlendMode>())
        {
            var k = Psd.BlendKey(m);
            Assert.Equal(4, k.Length);
            Assert.True(keys.Add(k), $"clave repetida {k}");
            Assert.Equal((m, false), Psd.FromBlendKey(k));
        }
        Assert.Equal(16, keys.Count);
        Assert.Equal((BlendMode.ColorBurn, true), Psd.FromBlendKey("lbrn"));
        Assert.Equal((BlendMode.ColorDodge, true), Psd.FromBlendKey("lddg"));
        Assert.Equal((BlendMode.Darken, true), Psd.FromBlendKey("dkCl"));
        Assert.Equal((BlendMode.Lighten, true), Psd.FromBlendKey("lgCl"));
        foreach (var k in new[] { "vLit", "lLit", "pLit", "hMix" }) Assert.Equal((BlendMode.HardLight, true), Psd.FromBlendKey(k));
        Assert.Equal((BlendMode.Difference, true), Psd.FromBlendKey("fsub"));
        Assert.Equal((BlendMode.ColorDodge, true), Psd.FromBlendKey("fdiv"));
        Assert.Equal((BlendMode.Normal, true), Psd.FromBlendKey("diss"));
        Assert.Equal((BlendMode.Normal, false), Psd.FromBlendKey("pass")); // tiene su propio aviso
        Assert.Equal((BlendMode.Normal, true), Psd.FromBlendKey("????"));

        var psd = new PsdFile { Width = 1, Height = 1, Composite = new byte[4] };
        psd.Layers.Add(new PsdLayer { Name = "x", Right = 1, Bottom = 1, Rgba = new byte[4], BlendKey = "vLit" });
        var t = Psd.ToTrazo(Psd.Read(Save(psd)));
        Assert.Contains(t.Warnings, w => w.Contains("aproximaron"));
    }

    // ---------- EX-01.4 ----------

    [Fact]
    public void LayerRectsArePlacedAndClipped()
    {
        // capa de 4×3 que sale por la izquierda y por arriba de un lienzo de 5×4
        var src = Rgba(4, 3, (x, y) => ((byte)(10 * x + y), 0, 0, 255));
        var o = Psd.Place(src, -1, -1, 4, 3, 5, 4, 4, 0);
        for (var y = 0; y < 4; y++)
            for (var x = 0; x < 5; x++)
            {
                var inside = x < 3 && y < 2;
                Assert.Equal(inside ? (byte)(10 * (x + 1) + y + 1) : (byte)0, o[(y * 5 + x) * 4]);
                Assert.Equal(inside ? (byte)255 : (byte)0, o[(y * 5 + x) * 4 + 3]);
            }
        // por la derecha y por abajo
        o = Psd.Place(src, 3, 2, 4, 3, 5, 4, 4, 0);
        Assert.Equal(0, o[(2 * 5 + 3) * 4]);   // (0,0) de la capa
        Assert.Equal(11, o[(3 * 5 + 4) * 4]);  // (1,1) de la capa
        Assert.Equal(0, o[(1 * 5 + 4) * 4 + 3]);
        // totalmente fuera: lienzo vacío, sin excepción
        Assert.All(Psd.Place(src, 50, 50, 4, 3, 5, 4, 4, 0), b => Assert.Equal(0, b));
        // máscara: fuera de su rectángulo vale el color por defecto
        var m = Psd.Place([0, 0], 1, 1, 2, 1, 3, 3, 1, 255);
        Assert.Equal([255, 255, 255, 255, 0, 0, 255, 255, 255], m);

        // recorte al exportar: solo el rectángulo con contenido
        var canvas = new byte[6 * 5 * 4];
        canvas[(1 * 6 + 2) * 4 + 3] = 9;
        canvas[(3 * 6 + 4) * 4 + 3] = 9;
        Assert.Equal((2, 1, 5, 4), Psd.OpaqueBounds(canvas, 6, 5));
        var layer = new PsdLayer();
        Psd.SetPixelsCropped(layer, canvas, 6, 5);
        Assert.Equal((2, 1, 5, 4), (layer.Left, layer.Top, layer.Right, layer.Bottom));
        Assert.Equal(Psd.Place(layer.Rgba, 2, 1, 3, 3, 6, 5, 4, 0), canvas);
        Psd.SetPixelsCropped(layer, new byte[6 * 5 * 4], 6, 5);
        Assert.Equal((0, 0), (layer.Width, layer.Height));
        // y una capa vacía sobrevive al viaje de ida y vuelta
        var psd = new PsdFile { Width = 6, Height = 5, Composite = new byte[120] };
        psd.Layers.Add(layer);
        Assert.Equal(0, Assert.Single(Psd.Read(Save(psd)).Layers).Width);
    }

    [Fact]
    public void AlphaConversionRoundTripsGpuPixels()
    {
        // todo píxel premultiplicado válido (canal ≤ alfa) sobrevive a ir al PSD y volver: el PSD no pierde nada de la capa
        var bgra = new byte[4];
        var rgba = new byte[4];
        var back = new byte[4];
        for (var a = 0; a < 256; a++)
            for (var c = 0; c <= a; c++)
            {
                bgra[0] = (byte)c;
                bgra[1] = (byte)(a - c);
                bgra[2] = (byte)(c / 2);
                bgra[3] = (byte)a;
                Psd.ToStraightRgba(bgra, rgba);
                Psd.ToPremultipliedBgra(rgba, back);
                Assert.Equal(a == 0 ? [0, 0, 0, 0] : bgra, back);
            }
        // y el sentido contrario redondea al más cercano
        Psd.ToPremultipliedBgra([255, 128, 1, 128], back);
        Assert.Equal([1, 64, 128, 128], back);

        // el bloqueo de transparencia viaja en el archivo
        var psd = new PsdFile { Width = 1, Height = 1, Composite = new byte[4] };
        psd.Layers.Add(new PsdLayer { Name = "x", Right = 1, Bottom = 1, Rgba = new byte[4], TransparencyLocked = true });
        Assert.True(Assert.Single(Psd.Read(Save(psd)).Layers).TransparencyLocked);
    }

    // ---------- EX-01.5 ----------

    [Fact]
    public void ImportsGrayscale16BitAndFlat()
    {
        // gris de 16 bits sin capas y sin comprimir: 3×1 con los valores altos 0x10, 0x80, 0xFF
        var flat = Header(1, 1, 3, 1, 16, 1).U32(0).U16(0).Raw([0x10, 0x55, 0x80, 0x00, 0xFF, 0xFF]).ToArray();
        var psd = Psd.Read(flat);
        Assert.True(psd.SourceGrayscale);
        Assert.Equal(16, psd.SourceDepth);
        Assert.Empty(psd.Layers);
        Assert.Equal([0x10, 0x10, 0x10, 255, 0x80, 0x80, 0x80, 255, 0xFF, 0xFF, 0xFF, 255], psd.Composite);

        // RGB de 8 bits sin capas con RLE
        var rle = new List<byte>();
        var counts = new Be();
        foreach (var v in new byte[] { 1, 2, 3 })
        {
            var p = new List<byte>();
            Psd.PackBits([v, v], p);
            counts.U16(p.Count);
            rle.AddRange(p);
        }
        var rgb = Header(1, 3, 2, 1, 8, 3).U32(0).U16(1).Raw(counts.B).Raw(rle).ToArray();
        Assert.Equal([1, 2, 3, 255, 1, 2, 3, 255], Psd.Read(rgb).Composite);

        // gris de 16 bits con las capas en el bloque «Lr16», como lo guarda Photoshop
        var layer = new Be()
            .U16(1)                                   // una capa
            .U32(0).U32(0).U32(1).U32(2)              // arriba, izquierda, abajo, derecha
            .U16(2).U16(0xFFFF).U32(6).U16(0).U32(6)  // canales −1 y 0, 6 bytes cada uno
            .Ascii("8BIMnorm").U8(255).U8(0).U8(0).U8(0)
            .U32(12).U32(0).U32(0).U8(1).Ascii("g").U16(0)
            .U16(0).Raw([0xFF, 0, 0x40, 0])          // alfa: 255, 64
            .U16(0).Raw([0x20, 0xAA, 0xC0, 0xBB]);   // gris: 0x20, 0xC0
        if (layer.B.Count % 2 != 0) layer.U8(0);
        var section = new Be().U32(0).U32(0).Ascii("8BIMLr16").U32(layer.B.Count).Raw(layer.B);
        var file = Header(1, 1, 2, 1, 16, 1).U32(section.B.Count).Raw(section.B).U16(0).Raw([0, 0, 0, 0]).ToArray();
        var l = Assert.Single(Psd.Read(file).Layers);
        Assert.Equal("g", l.Name);
        Assert.Equal([0x20, 0x20, 0x20, 0xFF, 0xC0, 0xC0, 0xC0, 0x40], l.Rgba);
    }

    [Fact]
    public void RejectsUnsupportedFlavors()
    {
        void Rejects(byte[] data, string message) =>
            Assert.Contains(message, Assert.Throws<InvalidDataException>(() => Psd.Read(data)).Message);

        Rejects(Header(2, 3, 2, 2, 8, 3).ToArray(), "PSB");
        Rejects(Header(1, 4, 2, 2, 8, 4).ToArray(), "CMYK");
        Rejects(Header(1, 3, 2, 2, 8, 9).ToArray(), "Lab");
        Rejects(Header(1, 1, 2, 2, 8, 2).ToArray(), "indexado");
        Rejects(Header(1, 3, 2, 2, 32, 3).ToArray(), "32 bits");
        Rejects(Header(1, 3, 2, 2, 8, 3).U32(0).U16(2).ToArray(), "ZIP");
        Rejects(Header(1, 3, 0, 2, 8, 3).ToArray(), "Tamaño");
        Rejects(Header(1, 3, 40000, 2, 8, 3).ToArray(), "Tamaño");
        Rejects(Encoding.ASCII.GetBytes("GIF89a, no soy un PSD de ninguna manera"), "No es un archivo PSD");
    }

    // ---------- EX-01.6 ----------

    [Fact]
    public void MalformedFilesFailCleanly()
    {
        var good = Save(Sample());
        var rng = new Random(2026);
        var cases = 0;
        void Try(byte[] data)
        {
            cases++;
            try
            {
                var psd = Psd.Read(data);
                Psd.ToTrazo(psd);
            }
            catch (InvalidDataException) { }
        }
        // todos los cortes en los primeros 400 bytes (cabecera y registros) y después cada 37
        for (var n = 0; n < good.Length; n += n < 400 ? 1 : 37) Try(good[..n]);
        // bytes cambiados al azar
        for (var i = 0; i < 20000; i++)
        {
            var bad = (byte[])good.Clone();
            for (var k = rng.Next(1, 4); k > 0; k--) bad[rng.Next(bad.Length)] = (byte)rng.Next(256);
            Try(bad);
        }
        // longitudes gigantes: una capa que dice medir 20000×20000 con 10 bytes de datos
        var huge = new Be().Ascii("8BPS").U16(1).U32(0).U16(0).U16(3).U32(4).U32(4).U16(8).U16(3).U32(0).U32(0);
        var rec = new Be().U16(1).U32(0).U32(0).U32(20000).U32(20000).U16(1).U16(0).U32(10)
            .Ascii("8BIMnorm").U8(255).U8(0).U8(0).U8(0).U32(8).U32(0).U32(0).Raw(new byte[10]);
        huge.U32(rec.B.Count + 4).U32(rec.B.Count).Raw(rec.B);
        Assert.Throws<InvalidDataException>(() => Psd.Read(huge.ToArray()));
        Assert.True(cases > 2000);
    }

    [Fact]
    public void WriteRejectsMismatchedBuffers()
    {
        var psd = new PsdFile { Width = 2, Height = 2, Composite = new byte[16] };
        psd.Layers.Add(new PsdLayer { Right = 2, Bottom = 2, Rgba = new byte[3] });
        Assert.Throws<ArgumentException>(() => Save(psd));
        Assert.Throws<ArgumentException>(() => Save(new PsdFile { Width = 2, Height = 2, Composite = new byte[3] }));
    }
}
