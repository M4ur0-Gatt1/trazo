using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;
using Trazo.Core.Documents;

namespace Trazo.Core.IO;

/// <summary>Una capa serializada: propiedades + PNG (no premultiplicado).</summary>
public sealed record LayerData(LayerProperties Properties, byte[] Png);

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
    public byte[]? ThumbnailPng { get; init; }
    public DateTimeOffset Created { get; init; } = DateTimeOffset.Now;
}

/// <summary>
/// Formato .trazo v2: ZIP con <c>manifest.json</c>, <c>thumbnail.png</c> y
/// <c>layers/NN.png</c>. También lee el formato v1 (JSON con PNG en base64) de la versión web.
/// </summary>
public static class TrazoFile
{
    public const string Extension = ".trazo";
    public const int CurrentVersion = 2;

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
                File = $"layers/{i:D3}.png",
            }).ToList(),
        };
        // PNG ya está comprimido: se guarda sin compresión adicional para que sea rápido
        for (var i = 0; i < project.Layers.Count; i++)
            WriteEntry(zip, manifest.Layers[i].File, project.Layers[i].Png, CompressionLevel.NoCompression);
        if (project.ThumbnailPng is { } thumb)
            WriteEntry(zip, "thumbnail.png", thumb, CompressionLevel.NoCompression);
        WriteEntry(zip, "manifest.json", JsonSerializer.SerializeToUtf8Bytes(manifest, TrazoJson.Default.ManifestV2), CompressionLevel.Optimal);
    }

    public static async Task WriteAtomicAsync(string path, ProjectData project)
    {
        var tmp = path + ".tmp";
        await using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16, useAsync: true))
        {
            using var ms = new MemoryStream();
            Write(ms, project);
            ms.Position = 0;
            await ms.CopyToAsync(fs);
        }
        File.Move(tmp, path, overwrite: true);
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
            },
            ReadEntry(zip.GetEntry(l.File) ?? throw new InvalidDataException($"Falta {l.File}")))).ToList();
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
        return p with { ActiveLayer = Math.Clamp(p.ActiveLayer, 0, Math.Max(0, p.Layers.Count - 1)) };
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
    public string File { get; set; } = "";
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
