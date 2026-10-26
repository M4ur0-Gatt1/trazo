using Microsoft.Graphics.Canvas;
using Trazo.Core.Documents;
using Trazo.Core.IO;
using Windows.UI;
using static Trazo.Core.Localization.Tr;

namespace Trazo.Rendering;

/// <summary>EX-01 — Paso entre <see cref="PaintDocument"/> y <see cref="PsdFile"/> (docs/specs/EX-01-psd.md).</summary>
public static class PsdInterop
{
    public const string Extension = ".psd";

    /// <summary>Nombre de la capa que representa el color de fondo de Trazo en el PSD.</summary>
    public static string BackgroundLayerName => T("Fondo");

    // ---------- Exportar ----------

    public static PsdFile ToPsd(PaintDocument doc)
    {
        int w = doc.Width, h = doc.Height;
        var psd = new PsdFile { Width = w, Height = h };
        var rgba = new byte[w * h * 4];

        if (doc.BackgroundVisible)
        {
            var bg = doc.Background;
            var fill = new byte[w * h * 4];
            for (var i = 0; i < fill.Length; i += 4)
            {
                fill[i] = bg.R;
                fill[i + 1] = bg.G;
                fill[i + 2] = bg.B;
                fill[i + 3] = 255;
            }
            psd.Layers.Add(new PsdLayer { Name = BackgroundLayerName, Right = w, Bottom = h, Rgba = fill });
        }

        string? openGroup = null;
        for (var i = 0; i < doc.Layers.Count; i++)
        {
            var l = doc.Layers[i];
            if (l.GroupId != openGroup)
            {
                if (openGroup is not null) psd.Layers.Add(Folder(doc.Group(openGroup)));
                if (l.GroupId is not null) psd.Layers.Add(new PsdLayer { Kind = PsdLayerKind.FolderEnd });
                openGroup = l.GroupId;
            }
            Psd.ToStraightRgba(l.Surface.GetPixelBytes(), rgba);
            var layer = new PsdLayer
            {
                Name = l.Name,
                Opacity = ToByte(l.Opacity),
                Visible = l.Visible,
                BlendKey = Psd.BlendKey(l.Blend),
                // en PSD el recorte se apoya en la capa de abajo del mismo grupo; sin ella, Trazo no recorta
                Clipping = l.Clip && i > 0 && doc.Layers[i - 1].GroupId == l.GroupId,
                TransparencyLocked = l.AlphaLock,
            };
            Psd.SetPixelsCropped(layer, rgba, w, h);
            if (l.Mask is { } mask)
            {
                var bytes = mask.GetPixelBytes();
                var gray = new byte[w * h];
                for (var k = 0; k < gray.Length; k++) gray[k] = bytes[k * 4 + 2];
                layer.Mask = new PsdMask(0, 0, w, h, 255, gray);
            }
            psd.Layers.Add(layer);
        }
        if (openGroup is not null) psd.Layers.Add(Folder(doc.Group(openGroup)));

        // imagen compuesta: lo que ve quien abra el archivo sin capas, sobre blanco si el fondo está oculto
        using (var flat = doc.Flatten(includeBackground: false))
        {
            Psd.ToStraightRgba(flat.GetPixelBytes(), rgba);
            var bg = doc.BackgroundVisible ? doc.Background : Color.FromArgb(255, 255, 255, 255);
            for (var i = 0; i < rgba.Length; i += 4)
            {
                var a = rgba[i + 3];
                rgba[i] = Over(rgba[i], bg.R, a);
                rgba[i + 1] = Over(rgba[i + 1], bg.G, a);
                rgba[i + 2] = Over(rgba[i + 2], bg.B, a);
                rgba[i + 3] = 255;
            }
        }
        psd.Composite = rgba;
        return psd;
    }

    private static PsdLayer Folder(LayerGroup? g) => new()
    {
        Name = g?.Name ?? T("Grupo"),
        Kind = g?.Collapsed == true ? PsdLayerKind.FolderClosed : PsdLayerKind.FolderOpen,
        Opacity = ToByte(g?.Opacity ?? 1),
        Visible = g?.Visible ?? true,
        BlendKey = Psd.BlendKey(g?.Blend ?? BlendMode.Normal),
    };

    private static byte ToByte(float v) => (byte)Math.Round(Math.Clamp(v, 0, 1) * 255);

    private static byte Over(byte c, byte bg, byte a) => (byte)((c * a + bg * (255 - a) + 127) / 255);

    public static void Write(PaintDocument doc, Stream stream) => Psd.Write(stream, ToPsd(doc));

    // ---------- Importar ----------

    /// <summary>Resultado de abrir un PSD: el documento y los avisos de lo que no se pudo conservar igual.</summary>
    public sealed record ImportResult(PaintDocument Document, IReadOnlyList<string> Warnings);

    /// <summary>Crea un documento a partir de un PSD leído. Lanza InvalidDataException si el lienzo no cabe en Trazo.</summary>
    public static ImportResult FromPsd(CanvasDevice device, PsdFile psd, string name)
    {
        int w = psd.Width, h = psd.Height;
        if (w < CanvasLimits.MinSide || h < CanvasLimits.MinSide || w > CanvasLimits.MaxSide || h > CanvasLimits.MaxSide || (long)w * h > CanvasLimits.MaxArea)
            throw new InvalidDataException(F("El lienzo del PSD ({0} × {1}) no cabe en Trazo: los lados van de {2} a {3} píxeles.", w, h, CanvasLimits.MinSide, CanvasLimits.MaxSide));

        var structure = Psd.ToTrazo(psd);
        var warnings = new List<string>(structure.Warnings);
        if (psd.SourceDepth == 16) warnings.Add(T("El archivo era de 16 bits por canal y se importó en 8."));
        var doc = new PaintDocument(device, w, h, name);
        var bgra = new byte[w * h * 4];

        var layers = structure.Layers;
        var max = doc.MaxLayers;
        if (layers.Count > max)
        {
            warnings.Add(F("El PSD tiene {0} capas y en este tamaño Trazo admite {1}: se importó la imagen aplanada.", layers.Count, max));
            layers = [];
        }
        if (layers.Count == 0)
        {
            var only = doc.CreateLayer(T("Imagen"));
            Psd.ToPremultipliedBgra(psd.Composite, bgra);
            only.Surface.SetPixelBytes(bgra);
            doc.Layers.Add(only);
            return new ImportResult(doc, warnings);
        }

        var groupIds = new List<string>();
        foreach (var g in structure.Groups)
        {
            var group = new LayerGroup
            {
                Name = g.Name,
                Opacity = g.Opacity / 255f,
                Blend = Psd.FromBlendKey(g.BlendKey).Mode,
                Visible = g.Visible,
                Collapsed = g.Collapsed,
            };
            doc.Groups.Add(group);
            groupIds.Add(group.Id);
        }
        foreach (var (src, group) in layers)
        {
            var layer = doc.CreateLayer(src.Name.Length > 0 ? src.Name : null);
            layer.Opacity = src.Opacity / 255f;
            layer.Visible = src.Visible;
            layer.Blend = Psd.FromBlendKey(src.BlendKey).Mode;
            layer.Clip = src.Clipping;
            layer.AlphaLock = src.TransparencyLocked;
            layer.GroupId = group is { } gi ? groupIds[gi] : null;
            Psd.ToPremultipliedBgra(Psd.Place(src.Rgba, src.Left, src.Top, src.Width, src.Height, w, h, 4, 0), bgra);
            layer.Surface.SetPixelBytes(bgra);
            if (src.Mask is { } m)
            {
                var gray = Psd.Place(m.Gray, m.Left, m.Top, m.Width, m.Height, w, h, 1, m.DefaultColor);
                var mb = new byte[w * h * 4];
                for (var i = 0; i < gray.Length; i++)
                {
                    mb[i * 4] = mb[i * 4 + 1] = mb[i * 4 + 2] = gray[i];
                    mb[i * 4 + 3] = 255;
                }
                layer.Mask = Layer.CreateMask(device, w, h, Color.FromArgb(255, 255, 255, 255));
                layer.Mask.SetPixelBytes(mb);
            }
            doc.Layers.Add(layer);
        }
        doc.ActiveIndex = doc.Layers.Count - 1;
        return new ImportResult(doc, warnings);
    }

    public static ImportResult Read(CanvasDevice device, Stream stream, string name) => FromPsd(device, Psd.Read(stream), name);
}
