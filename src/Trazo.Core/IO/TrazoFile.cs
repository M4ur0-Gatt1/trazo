using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Numerics;
using Trazo.Core.Documents;
using Trazo.Core.Geometry;

namespace Trazo.Core.IO;

/// <summary>Una capa serializada: propiedades + PNG (no premultiplicado) + máscara opcional (PNG en gris, blanco = visible).</summary>
public sealed record LayerData(LayerProperties Properties, byte[] Png, byte[]? MaskPng = null);

/// <summary>Contenido completo de un archivo .trazo.</summary>
public sealed record ProjectData
{
    public string Name { get; init; } = "Sin título";
    public int Width { get; init; }
    public int Height { get; init; }
    public string BackgroundColor { get; init; } = "#ffffff";
    public bool BackgroundVisible { get; init; } = true;
    public int ActiveLayer { get; init; }
    /// <summary>De abajo hacia arriba.</summary>
    public IReadOnlyList<LayerData> Layers { get; init; } = [];
    /// <summary>Grupos de capas (v3). Las capas indican su grupo con <see cref="LayerProperties.GroupId"/>.</summary>
    public IReadOnlyList<GroupProperties> Groups { get; init; } = [];
    /// <summary>Guía de dibujo (DG-01); null si no hay ninguna. Campo opcional: no cambia la versión del formato.</summary>
    public DrawingGuide? Guide { get; init; }
    public byte[]? ThumbnailPng { get; init; }
    public DateTimeOffset Created { get; init; } = DateTimeOffset.Now;
}

/// <summary>
/// Formato .trazo v3: ZIP con <c>manifest.json</c>, <c>thumbnail.png</c>, <c>layers/NNN.png</c> y, si hay
/// máscaras, <c>layers/NNN-mask.png</c>; el manifiesto incluye los grupos. Lee también v2 y el v1 (JSON con
/// PNG en base64) de la versión web.
/// </summary>
public static class TrazoFile
{
    public const string Extension = ".trazo";
    public const int CurrentVersion = 3;

    public static void Write(Stream output, ProjectData project)
    {
        using var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true);
        var manifest = new ManifestV2
        {
            Format = "trazo",
            Version = CurrentVersion,
            Name = project.Name,
            Width = project.Width,
            Height = project.Height,
            Background = new BackgroundV2 { Color = project.BackgroundColor, Visible = project.BackgroundVisible },
            Active = project.ActiveLayer,
            Created = project.Created,
            Layers = project.Layers.Select((l, i) => new LayerV2
            {
                Id = l.Properties.Id,
                Name = l.Properties.Name,
                Opacity = l.Properties.Opacity,
                Blend = l.Properties.Blend.ToString(),
                Visible = l.Properties.Visible,
                AlphaLock = l.Properties.AlphaLock,
                Clip = l.Properties.Clip,
                GroupId = l.Properties.GroupId,
                File = $"layers/{i:D3}.png",
                Mask = l.MaskPng is null ? null : $"layers/{i:D3}-mask.png",
            }).ToList(),
            Groups = project.Groups.Count == 0 ? null : project.Groups.Select(g => new GroupV3
            {
                Id = g.Id, Name = g.Name, Opacity = g.Opacity, Blend = g.Blend.ToString(), Visible = g.Visible, Collapsed = g.Collapsed,
            }).ToList(),
            Guide = project.Guide is not { Kind: not GuideKind.None } guide ? null : new GuideV3
            {
                Kind = guide.Kind.ToString(), Size = guide.Size, Thickness = guide.Thickness, Opacity = guide.Opacity, Color = guide.Color,
                Assist = guide.Assist, Points = guide.VanishingPoints.SelectMany(p => new[] { p.X, p.Y }).ToList(),
            },
        };
        // PNG ya está comprimido: se guarda sin compresión adicional para que sea rápido
        for (var i = 0; i < project.Layers.Count; i++)
        {
            WriteEntry(zip, manifest.Layers[i].File, project.Layers[i].Png, CompressionLevel.NoCompression);
            if (project.Layers[i].MaskPng is { } mask) WriteEntry(zip, manifest.Layers[i].Mask!, mask, CompressionLevel.NoCompression);
        }
        if (project.ThumbnailPng is { } thumb)
            WriteEntry(zip, "thumbnail.png", thumb, CompressionLevel.NoCompression);
        WriteEntry(zip, "manifest.json", JsonSerializer.SerializeToUtf8Bytes(manifest, TrazoJson.Default.ManifestV2), CompressionLevel.Optimal);
    }

    private static DrawingGuide? ReadGuide(GuideV3? g)
    {
        if (g is null || !Enum.TryParse<GuideKind>(g.Kind, out var kind) || kind == GuideKind.None) return null;
        var pts = g.Points ?? [];
        var vps = Enumerable.Range(0, Math.Min(3, pts.Count / 2)).Select(i => new Vector2(pts[2 * i], pts[2 * i + 1]))
            .Where(p => float.IsFinite(p.X) && float.IsFinite(p.Y)).ToList();
        return new DrawingGuide
        {
            Kind = kind,
            Size = float.IsFinite(g.Size) ? Math.Clamp(g.Size, 4, 10000) : 100,
            Thickness = float.IsFinite(g.Thickness) ? Math.Clamp(g.Thickness, 0.5f, 10) : 1,
            Opacity = float.IsFinite(g.Opacity) ? Math.Clamp(g.Opacity, 0, 1) : 0.4f,
            Color = g.Color is { Length: 6 } c && c.All(Uri.IsHexDigit) ? c.ToUpperInvariant() : "00E5FF",
            Assist = g.Assist,
            VanishingPoints = vps,
        };
    }

    public static ProjectData Read(Stream input)
    {
        using var buffer = new MemoryStream();
        input.CopyTo(buffer);
        var bytes = buffer.ToArray();
        if (bytes.Length > 1 && bytes[0] == (byte)'P' && bytes[1] == (byte)'K')
            return ReadV2(new MemoryStream(bytes));
        return ReadV1(bytes);
    }

    /// <summary>Lee solo la miniatura (para la galería) sin descomprimir las capas.</summary>
    public static byte[]? ReadThumbnail(Stream input)
    {
        try
        {
            using var zip = new ZipArchive(input, ZipArchiveMode.Read, leaveOpen: true);
            var e = zip.GetEntry("thumbnail.png");
            return e is null ? null : ReadEntry(e);
        }
        catch (InvalidDataException)
        {
            return null;
        }
    }

    private static ProjectData ReadV2(Stream s)
    {
        using var zip = new ZipArchive(s, ZipArchiveMode.Read);
        var me = zip.GetEntry("manifest.json") ?? throw new InvalidDataException("Falta manifest.json");
        var m = JsonSerializer.Deserialize(ReadEntry(me), TrazoJson.Default.ManifestV2)
                ?? throw new InvalidDataException("Manifiesto vacío");
        if (m.Format != "trazo") throw new InvalidDataException("No es un archivo .trazo");
        if (m.Version > CurrentVersion) throw new InvalidDataException($"Archivo de una versión más nueva de Trazo (v{m.Version})");
        var layers = m.Layers.Select(l => new LayerData(
            new LayerProperties
            {
                Id = l.Id ?? Guid.NewGuid().ToString("N"),
                Name = l.Name ?? "Capa",
                Opacity = Math.Clamp(l.Opacity, 0, 1),
                Blend = Enum.TryParse<BlendMode>(l.Blend, out var b) ? b : BlendMode.Normal,
                Visible = l.Visible,
                AlphaLock = l.AlphaLock,
                Clip = l.Clip,
                GroupId = string.IsNullOrEmpty(l.GroupId) ? null : l.GroupId,
            },
            ReadEntry(zip.GetEntry(l.File) ?? throw new InvalidDataException($"Falta {l.File}")),
            l.Mask is null ? null : ReadEntry(zip.GetEntry(l.Mask) ?? throw new InvalidDataException($"Falta {l.Mask}")))).ToList();
        var groups = (m.Groups ?? []).Select(g => new GroupProperties
        {
            Id = g.Id ?? Guid.NewGuid().ToString("N"),
            Name = g.Name ?? "Grupo",
            Opacity = Math.Clamp(g.Opacity, 0, 1),
            Blend = Enum.TryParse<BlendMode>(g.Blend, out var gb) ? gb : BlendMode.Normal,
            Visible = g.Visible,
            Collapsed = g.Collapsed,
        }).ToList();
        return Validate(new ProjectData
        {
            Name = m.Name ?? "Sin título",
            Width = m.Width,
            Height = m.Height,
            BackgroundColor = m.Background?.Color ?? "#ffffff",
            BackgroundVisible = m.Background?.Visible ?? true,
            ActiveLayer = m.Active,
            Created = m.Created ?? DateTimeOffset.Now,
            Layers = layers,
            Groups = groups,
            Guide = ReadGuide(m.Guide),
            ThumbnailPng = zip.GetEntry("thumbnail.png") is { } t ? ReadEntry(t) : null,
        });
    }

    private static ProjectData ReadV1(byte[] bytes)
    {
        ManifestV1? m;
        try
        {
            m = JsonSerializer.Deserialize(bytes, TrazoJson.Default.ManifestV1);
        }
        catch (JsonException e)
        {
            throw new InvalidDataException("El archivo no es un proyecto de Trazo válido", e);
        }
        if (m is null || m.Format != "trazo") throw new InvalidDataException("No es un archivo .trazo");
        var layers = (m.Layers ?? []).Select(l => new LayerData(
            new LayerProperties
            {
                Name = l.Name ?? "Capa",
                Opacity = Math.Clamp(l.Opacity ?? 1, 0, 1),
                Blend = BlendModes.FromCssName(l.Blend),
                Visible = l.Visible ?? true,
                AlphaLock = l.AlphaLock ?? false,
                Clip = l.Clip ?? false,
            },
            DecodeDataUrl(l.Png))).ToList();
        return Validate(new ProjectData
        {
            Name = m.Name ?? "Sin título",
            Width = m.Width,
            Height = m.Height,
            BackgroundColor = m.Bg?.Color ?? "#ffffff",
            BackgroundVisible = m.Bg?.Visible ?? true,
            ActiveLayer = m.Active ?? 0,
            Layers = layers,
        });
    }

    private static ProjectData Validate(ProjectData p)
    {
        if (p.Width < CanvasLimits.MinSide || p.Height < CanvasLimits.MinSide ||
            p.Width > CanvasLimits.MaxSide || p.Height > CanvasLimits.MaxSide)
            throw new InvalidDataException($"Tamaño de lienzo no admitido: {p.Width}×{p.Height}");
        // capas que apuntan a un grupo inexistente quedan fuera de grupo; los grupos deben ser contiguos
        var known = p.Groups.Select(g => g.Id).ToHashSet();
        var layers = p.Layers.Select(l => l.Properties.GroupId is { } gid && !known.Contains(gid) ? l with { Properties = l.Properties with { GroupId = null } } : l).ToList();
        if (!GroupRules.AreContiguous(layers.Select(l => l.Properties.GroupId).ToList()))
            throw new InvalidDataException("Grupos de capas no contiguos");
        return p with { Layers = layers, ActiveLayer = Math.Clamp(p.ActiveLayer, 0, Math.Max(0, p.Layers.Count - 1)) };
    }

    private static byte[] DecodeDataUrl(string? url)
    {
        if (string.IsNullOrEmpty(url)) return [];
        var comma = url.IndexOf(',');
        return Convert.FromBase64String(comma >= 0 ? url[(comma + 1)..] : url);
    }

    private static void WriteEntry(ZipArchive zip, string name, byte[] data, CompressionLevel level)
    {
        var e = zip.CreateEntry(name, level);
        using var s = e.Open();
        s.Write(data);
    }

    private static byte[] ReadEntry(ZipArchiveEntry e)
    {
        using var s = e.Open();
        using var ms = new MemoryStream();
        s.CopyTo(ms);
        return ms.ToArray();
    }
}

internal sealed class GuideV3
{
    public string? Kind { get; set; }
    public float Size { get; set; } = 100;
    public float Thickness { get; set; } = 1;
    public float Opacity { get; set; } = 0.4f;
    public string? Color { get; set; }
    public bool Assist { get; set; }
    public List<float>? Points { get; set; }
}

internal sealed class ManifestV2
{
    public string Format { get; set; } = "trazo";
    public int Version { get; set; }
    public string? Name { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public BackgroundV2? Background { get; set; }
    public int Active { get; set; }
    public DateTimeOffset? Created { get; set; }
    public List<LayerV2> Layers { get; set; } = [];
    public List<GroupV3>? Groups { get; set; }
    public GuideV3? Guide { get; set; }
}

internal sealed class GroupV3
{
    public string? Id { get; set; }
    public string? Name { get; set; }
    public float Opacity { get; set; } = 1;
    public string? Blend { get; set; }
    public bool Visible { get; set; } = true;
    public bool Collapsed { get; set; }
}

internal sealed class BackgroundV2
{
    public string Color { get; set; } = "#ffffff";
    public bool Visible { get; set; } = true;
}

internal sealed class LayerV2
{
    public string? Id { get; set; }
    public string? Name { get; set; }
    public float Opacity { get; set; } = 1;
    public string? Blend { get; set; }
    public bool Visible { get; set; } = true;
    public bool AlphaLock { get; set; }
    public bool Clip { get; set; }
    public string? GroupId { get; set; }
    public string File { get; set; } = "";
    public string? Mask { get; set; }
}

internal sealed class ManifestV1
{
    public string? Format { get; set; }
    public string? Name { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public BackgroundV2? Bg { get; set; }
    public int? Active { get; set; }
    public List<LayerV1>? Layers { get; set; }
}

internal sealed class LayerV1
{
    public string? Name { get; set; }
    public float? Opacity { get; set; }
    public string? Blend { get; set; }
    public bool? Visible { get; set; }
    public bool? AlphaLock { get; set; }
    public bool? Clip { get; set; }
    public string? Png { get; set; }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(ManifestV2))]
[JsonSerializable(typeof(ManifestV1))]
internal sealed partial class TrazoJson : JsonSerializerContext;
