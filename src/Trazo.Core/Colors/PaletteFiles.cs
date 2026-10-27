using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using static Trazo.Core.Localization.Tr;

namespace Trazo.Core.Colors;

/// <summary>Una paleta: nombre y colores en orden.</summary>
public sealed record Palette(string Name, IReadOnlyList<Rgba> Colors);

/// <summary>Resultado de importar: las paletas y si algún color se convirtió de forma aproximada (CMYK o Lab).</summary>
public sealed record PaletteImport(IReadOnlyList<Palette> Palettes, bool Approximated);

/// <summary>
/// CO-02: paletas de Procreate (<c>.swatches</c>) y de Adobe (<c>.ase</c>). Los archivos dañados lanzan
/// InvalidDataException y nunca otra cosa.
/// </summary>
public static class PaletteFiles
{
    public const int MaxColors = 256;
    public const int MaxName = 64;

    public static string CleanName(string? name, string fallback)
    {
        var n = new string((name ?? "").Where(c => !char.IsControl(c)).ToArray()).Trim();
        if (n.Length > MaxName) n = n[..MaxName];
        return n.Length == 0 ? fallback : n;
    }

    // ---------- Procreate .swatches ----------

    /// <summary>Lee un .swatches: un ZIP con Swatches.json (nombre y colores en HSB de 0 a 1; puede haber huecos null).</summary>
    public static Palette ReadSwatches(Stream stream, string fallbackName)
    {
        try
        {
            using var zip = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
            var entry = zip.Entries.FirstOrDefault(e => e.FullName.EndsWith("Swatches.json", StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidDataException(T("El archivo no tiene Swatches.json: no es una paleta de Procreate."));
            if (entry.Length > 4 * 1024 * 1024) throw new InvalidDataException(T("La paleta es demasiado grande."));
            using var json = JsonDocument.Parse(entry.Open(), new JsonDocumentOptions { MaxDepth = 16 });
            var root = json.RootElement;
            // Procreate guarda una lista con una paleta; también se acepta el objeto suelto
            var pal = root.ValueKind == JsonValueKind.Array ? root.EnumerateArray().FirstOrDefault() : root;
            if (pal.ValueKind != JsonValueKind.Object) throw new InvalidDataException(T("Swatches.json no tiene el formato de Procreate."));
            var name = pal.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString() : null;
            var colors = new List<Rgba>();
            if (pal.TryGetProperty("swatches", out var sw) && sw.ValueKind == JsonValueKind.Array)
                foreach (var s in sw.EnumerateArray())
                {
                    if (s.ValueKind != JsonValueKind.Object) continue; // hueco vacío de la cuadrícula
                    double Get(string key) => s.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : 0;
                    double Unit(double v) => double.IsFinite(v) ? Math.Clamp(v, 0, 1) : 0;
                    colors.Add(new Hsv(Unit(Get("hue")) * 360, Unit(Get("saturation")), Unit(Get("brightness"))).ToRgba());
                    if (colors.Count == MaxColors) break;
                }
            return new Palette(CleanName(name, fallbackName), colors);
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or ArgumentException or IOException and not FileNotFoundException)
        {
            throw new InvalidDataException(T("El archivo no es una paleta de Procreate válida."), e);
        }
    }

    public static void WriteSwatches(Palette palette, Stream stream)
    {
        using var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true);
        using var w = new Utf8JsonWriter(zip.CreateEntry("Swatches.json").Open());
        w.WriteStartArray();
        w.WriteStartObject();
        w.WriteString("name", palette.Name);
        w.WriteStartArray("swatches");
        foreach (var c in palette.Colors.Take(MaxColors))
        {
            var hsv = ColorMath.RgbToHsv(c);
            w.WriteStartObject();
            w.WriteNumber("hue", hsv.H / 360);
            w.WriteNumber("saturation", hsv.S);
            w.WriteNumber("brightness", hsv.V);
            w.WriteNumber("alpha", 1);
            w.WriteNumber("colorSpace", 0);
            w.WriteEndObject();
        }
        w.WriteEndArray();
        w.WriteEndObject();
        w.WriteEndArray();
    }

    // ---------- Adobe .ase ----------

    private const ushort ColorEntry = 0x0001, GroupStart = 0xC001, GroupEnd = 0xC002;

    /// <summary>Lee un .ase: cada grupo es una paleta; los colores fuera de grupos van a una con <paramref name="fallbackName"/>.</summary>
    public static PaletteImport ReadAse(ReadOnlySpan<byte> data, string fallbackName)
    {
        if (data.Length < 12 || !data[..4].SequenceEqual("ASEF"u8)) throw new InvalidDataException(T("No es un archivo de muestras de Adobe (.ase)."));
        if (BinaryPrimitives.ReadUInt16BigEndian(data[4..]) != 1) throw new InvalidDataException(T("Versión de .ase no soportada."));
        var blocks = BinaryPrimitives.ReadUInt32BigEndian(data[8..]);
        var pos = 12;
        var loose = new List<Rgba>();
        var palettes = new List<Palette>();
        string? group = null;
        List<Rgba>? groupColors = null;
        var approximated = false;
        for (var b = 0u; b < blocks && pos < data.Length; b++)
        {
            if (pos + 6 > data.Length) throw Truncated();
            var type = BinaryPrimitives.ReadUInt16BigEndian(data[pos..]);
            var len = BinaryPrimitives.ReadUInt32BigEndian(data[(pos + 2)..]);
            pos += 6;
            if (len > data.Length - pos) throw Truncated();
            var block = data.Slice(pos, (int)len);
            pos += (int)len;
            switch (type)
            {
                case GroupStart:
                    group = ReadName(block, out _);
                    groupColors = [];
                    break;
                case GroupEnd:
                    if (group is not null && groupColors is { Count: > 0 }) palettes.Add(new Palette(CleanName(group, fallbackName), groupColors));
                    group = null;
                    groupColors = null;
                    break;
                case ColorEntry:
                    ReadName(block, out var used);
                    var color = ReadColor(block[used..], ref approximated);
                    var target = groupColors ?? loose;
                    if (target.Count < MaxColors) target.Add(color);
                    break;
                // otros bloques se ignoran
            }
        }
        // un grupo sin cierre también cuenta
        if (group is not null && groupColors is { Count: > 0 }) palettes.Add(new Palette(CleanName(group, fallbackName), groupColors));
        if (loose.Count > 0) palettes.Insert(0, new Palette(CleanName(fallbackName, fallbackName), loose));
        if (palettes.Count == 0) throw new InvalidDataException(T("El archivo .ase no tiene colores."));
        return new PaletteImport(palettes, approximated);
    }

    private static InvalidDataException Truncated() => new(T("El archivo .ase está truncado o dañado."));

    private static string ReadName(ReadOnlySpan<byte> block, out int used)
    {
        if (block.Length < 2) throw Truncated();
        var chars = BinaryPrimitives.ReadUInt16BigEndian(block);
        used = 2 + chars * 2;
        if (used > block.Length) throw Truncated();
        var name = Encoding.BigEndianUnicode.GetString(block.Slice(2, chars * 2));
        return name.TrimEnd('\0');
    }

    private static Rgba ReadColor(ReadOnlySpan<byte> b, ref bool approximated)
    {
        if (b.Length < 4) throw Truncated();
        var model = Encoding.ASCII.GetString(b[..4]);
        var values = new float[Math.Min(4, (b.Length - 4) / 4)];
        for (var i = 0; i < values.Length; i++)
        {
            var v = BinaryPrimitives.ReadSingleBigEndian(b[(4 + i * 4)..]);
            values[i] = float.IsFinite(v) ? v : 0;
        }
        float F(int i) => i < values.Length ? values[i] : throw Truncated();
        static byte U(double v) => (byte)Math.Round(Math.Clamp(v, 0, 1) * 255);
        switch (model)
        {
            case "RGB ":
                return new Rgba(U(F(0)), U(F(1)), U(F(2)));
            case "Gray":
                var g = U(F(0));
                return new Rgba(g, g, g);
            case "CMYK":
                approximated = true;
                double c = Math.Clamp(F(0), 0, 1), m = Math.Clamp(F(1), 0, 1), y = Math.Clamp(F(2), 0, 1), k = Math.Clamp(F(3), 0, 1);
                return new Rgba(U((1 - c) * (1 - k)), U((1 - m) * (1 - k)), U((1 - y) * (1 - k)));
            case "LAB ":
                approximated = true;
                return LabToRgb(F(0), F(1), F(2));
            default:
                throw new InvalidDataException(T("Modelo de color de .ase desconocido."));
        }
    }

    /// <summary>Lab (D50, L en 0–1 como lo guarda Adobe, o en 0–100) → sRGB, con adaptación de Bradford a D65.</summary>
    public static Rgba LabToRgb(double l, double a, double bb)
    {
        if (l <= 1.0001) l *= 100;
        var fy = (l + 16) / 116;
        var fx = fy + a / 500;
        var fz = fy - bb / 200;
        static double Inv(double t) => t * t * t > 0.008856 ? t * t * t : (116 * t - 16) / 903.3;
        double x = 0.9642 * Inv(fx), y = 1.0 * (l > 8 ? fy * fy * fy : l / 903.3), z = 0.8249 * Inv(fz);
        // D50 → D65 (Bradford) y XYZ → sRGB lineal
        double xd = 0.9555766 * x - 0.0230393 * y + 0.0631636 * z;
        double yd = -0.0282895 * x + 1.0099416 * y + 0.0210077 * z;
        double zd = 0.0122982 * x - 0.0204830 * y + 1.3299098 * z;
        double r = 3.2404542 * xd - 1.5371385 * yd - 0.4985314 * zd;
        double g = -0.9692660 * xd + 1.8760108 * yd + 0.0415560 * zd;
        double b = 0.0556434 * xd - 0.2040259 * yd + 1.0572252 * zd;
        static byte Gamma(double v)
        {
            v = Math.Clamp(v, 0, 1);
            v = v <= 0.0031308 ? 12.92 * v : 1.055 * Math.Pow(v, 1 / 2.4) - 0.055;
            return (byte)Math.Round(v * 255);
        }
        return new Rgba(Gamma(r), Gamma(g), Gamma(b));
    }

    /// <summary>Escribe la paleta como un grupo de colores RGB.</summary>
    public static void WriteAse(Palette palette, Stream stream)
    {
        var colors = palette.Colors.Take(MaxColors).ToList();
        var body = new MemoryStream();
        void U16(int v) { Span<byte> t = stackalloc byte[2]; BinaryPrimitives.WriteUInt16BigEndian(t, (ushort)v); body.Write(t); }
        void U32(long v) { Span<byte> t = stackalloc byte[4]; BinaryPrimitives.WriteUInt32BigEndian(t, (uint)v); body.Write(t); }
        void F32(float v) { Span<byte> t = stackalloc byte[4]; BinaryPrimitives.WriteSingleBigEndian(t, v); body.Write(t); }
        byte[] Name(string s) => Encoding.BigEndianUnicode.GetBytes(s + "\0");
        void Block(ushort type, Action write)
        {
            U16(type);
            var lenAt = body.Position;
            U32(0);
            var start = body.Position;
            write();
            var end = body.Position;
            body.Position = lenAt;
            U32(end - start);
            body.Position = end;
        }
        void WriteName(string s)
        {
            var n = Name(s);
            U16(n.Length / 2);
            body.Write(n);
        }
        Block(GroupStart, () => WriteName(palette.Name));
        foreach (var c in colors)
            Block(ColorEntry, () =>
            {
                WriteName($"#{c.R:X2}{c.G:X2}{c.B:X2}");
                body.Write("RGB "u8);
                F32(c.R / 255f);
                F32(c.G / 255f);
                F32(c.B / 255f);
                U16(2); // normal
            });
        Block(GroupEnd, () => { });
        Span<byte> head = stackalloc byte[12];
        "ASEF"u8.CopyTo(head);
        BinaryPrimitives.WriteUInt16BigEndian(head[4..], 1);
        BinaryPrimitives.WriteUInt16BigEndian(head[6..], 0);
        BinaryPrimitives.WriteUInt32BigEndian(head[8..], (uint)(colors.Count + 2));
        stream.Write(head);
        body.Position = 0;
        body.CopyTo(stream);
    }
}

/// <summary>Una paleta guardada en las preferencias: identificador estable, nombre y colores en hexadecimal.</summary>
public sealed class PaletteData
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public List<string> Colors { get; set; } = [];
}

/// <summary>CO-02: el conjunto de paletas del usuario y cuál es la predeterminada.</summary>
public static class PaletteSet
{
    /// <summary>
    /// Pasa de las preferencias anteriores (una paleta fija y una propia) al conjunto nuevo, sin perder colores. Si ya
    /// hay paletas, no cambia nada.
    /// </summary>
    public static void Migrate(List<PaletteData> palettes, List<string> oldCustom, IEnumerable<string> builtIn, ref string? defaultId)
    {
        if (palettes.Count > 0) return;
        palettes.Add(new PaletteData { Name = "Trazo", Colors = [.. builtIn] });
        var mine = new PaletteData { Name = T("Mi paleta"), Colors = [.. oldCustom] };
        palettes.Add(mine);
        defaultId = oldCustom.Count > 0 ? mine.Id : palettes[0].Id;
    }

    /// <summary>La predeterminada, o la primera si la predeterminada ya no existe.</summary>
    public static PaletteData? Default(List<PaletteData> palettes, string? defaultId) =>
        palettes.FirstOrDefault(p => p.Id == defaultId) ?? palettes.FirstOrDefault();

    public static PaletteData From(Palette p) => new() { Name = p.Name, Colors = [.. p.Colors.Select(c => c.ToHex())] };

    public static Palette ToPalette(PaletteData d) =>
        new(d.Name, d.Colors.Select(h => Rgba.TryParseHex(h, out var c) ? c : (Rgba?)null).OfType<Rgba>().ToList());
}
