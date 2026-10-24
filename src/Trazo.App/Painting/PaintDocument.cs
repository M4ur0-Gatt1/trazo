using System.Collections.ObjectModel;
using System.Runtime.InteropServices.WindowsRuntime;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Effects;
using Microsoft.UI;
using Trazo.Core.Colors;
using Trazo.Core.Documents;
using Trazo.Core.History;
using Trazo.Core.IO;
using Windows.Foundation;
using Windows.Storage.Streams;
using Windows.UI;

namespace Trazo.App.Painting;

/// <summary>Capa con sus píxeles en una textura de la GPU.</summary>
public sealed partial class Layer : ObservableObject, IDisposable
{
    private string _name;
    private float _opacity = 1;
    private BlendMode _blend;
    private bool _visible = true;
    private bool _alphaLock;
    private bool _clip;
    private int _revision;

    public Layer(CanvasDevice device, int width, int height, string name)
    {
        Surface = new CanvasRenderTarget(device, width, height, 96);
        using (var ds = Surface.CreateDrawingSession()) ds.Clear(Colors.Transparent);
        _name = name;
    }

    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public CanvasRenderTarget Surface { get; }

    public string Name { get => _name; set => SetProperty(ref _name, value); }
    public float Opacity { get => _opacity; set => SetProperty(ref _opacity, Math.Clamp(value, 0, 1)); }
    public BlendMode Blend { get => _blend; set { if (SetProperty(ref _blend, value)) OnPropertyChanged(nameof(BlendShort)); } }
    public bool Visible { get => _visible; set => SetProperty(ref _visible, value); }
    public bool AlphaLock { get => _alphaLock; set => SetProperty(ref _alphaLock, value); }
    public bool Clip { get => _clip; set => SetProperty(ref _clip, value); }
    public string BlendShort => _blend.ShortName();

    /// <summary>Aumenta con cada cambio de píxeles (para refrescar miniaturas).</summary>
    public int Revision { get => _revision; private set => SetProperty(ref _revision, value); }
    public void Touch() => Revision++;

    public LayerProperties ToProperties() => new()
    {
        Id = Id, Name = Name, Opacity = Opacity, Blend = Blend, Visible = Visible, AlphaLock = AlphaLock, Clip = Clip,
    };

    public void Apply(LayerProperties p)
    {
        Name = p.Name; Opacity = p.Opacity; Blend = p.Blend; Visible = p.Visible; AlphaLock = p.AlphaLock; Clip = p.Clip;
    }

    public Layer Duplicate(CanvasDevice device)
    {
        var l = new Layer(device, (int)Surface.SizeInPixels.Width, (int)Surface.SizeInPixels.Height, Name + " copia");
        l.Apply(ToProperties() with { Id = l.Id, Name = Name + " copia" });
        using var ds = l.Surface.CreateDrawingSession();
        ds.DrawImage(Surface);
        return l;
    }

    public void Dispose() => Surface.Dispose();
}

/// <summary>Documento abierto: capas, fondo, historial y archivo asociado.</summary>
public sealed partial class PaintDocument : ObservableObject, IDisposable
{
    private int _activeIndex;
    private Color _background = Colors.White;
    private bool _backgroundVisible = true;
    private string _name;

    public PaintDocument(CanvasDevice device, int width, int height, string name)
    {
        Device = device;
        Width = width;
        Height = height;
        _name = name;
        History = new UndoStack(maxSteps: 150, maxBytes: 1536L * 1024 * 1024);
    }

    public CanvasDevice Device { get; }
    public int Width { get; }
    public int Height { get; }
    public Size Size => new(Width, Height);
    public Rect Bounds => new(0, 0, Width, Height);
    public UndoStack History { get; }
    public string? FilePath { get; set; }
    public DateTimeOffset Created { get; set; } = DateTimeOffset.Now;
    public bool IsDirty { get; set; }

    /// <summary>De abajo hacia arriba.</summary>
    public ObservableCollection<Layer> Layers { get; } = [];

    public string Name { get => _name; set => SetProperty(ref _name, value); }
    public Color Background { get => _background; set => SetProperty(ref _background, value); }
    public bool BackgroundVisible { get => _backgroundVisible; set => SetProperty(ref _backgroundVisible, value); }

    public int ActiveIndex
    {
        get => _activeIndex;
        set { if (SetProperty(ref _activeIndex, Math.Clamp(value, 0, Math.Max(0, Layers.Count - 1)))) OnPropertyChanged(nameof(ActiveLayer)); }
    }

    public Layer ActiveLayer => Layers[Math.Clamp(_activeIndex, 0, Layers.Count - 1)];
    public int MaxLayers => CanvasLimits.MaxLayers(Width, Height);

    public Layer CreateLayer(string? name = null) =>
        new(Device, Width, Height, name ?? CanvasLimits.NextLayerName(Layers.Select(l => l.Name)));

    public static PaintDocument CreateBlank(CanvasDevice device, int width, int height, string name)
    {
        var d = new PaintDocument(device, width, height, name);
        d.Layers.Add(d.CreateLayer());
        return d;
    }

    // ---------- Composición ----------

    /// <summary>
    /// Construye el grafo de efectos que compone el documento. Las capas en
    /// <paramref name="overrides"/> se sustituyen por otra imagen (vista previa de
    /// trazos, ajustes o transformaciones). Los efectos creados se añaden a
    /// <paramref name="owned"/> para liberarlos después de dibujar.
    /// </summary>
    public ICanvasImage Compose(IReadOnlyDictionary<Layer, ICanvasImage>? overrides, List<IDisposable> owned, bool includeBackground = true)
    {
        ICanvasImage acc = Track(owned, new CropEffect
        {
            Source = Track(owned, new ColorSourceEffect { Color = includeBackground && BackgroundVisible ? Background : Colors.Transparent }),
            SourceRectangle = Bounds,
        });
        ICanvasImage Src(Layer l) => overrides is not null && overrides.TryGetValue(l, out var o) ? o : l.Surface;

        var i = 0;
        while (i < Layers.Count)
        {
            var baseLayer = Layers[i];
            var j = i + 1;
            while (j < Layers.Count && Layers[j].Clip) j++;
            if (baseLayer.Visible && baseLayer.Opacity > 0)
            {
                var img = Src(baseLayer);
                for (var k = i + 1; k < j; k++)
                {
                    var c = Layers[k];
                    if (!c.Visible || c.Opacity <= 0) continue;
                    var top = c.Opacity < 1 ? Track(owned, new OpacityEffect { Source = Src(c), Opacity = c.Opacity }) : Src(c);
                    img = Track(owned, new CompositeEffect { Mode = CanvasComposite.SourceAtop, Sources = { img, top } });
                }
                if (baseLayer.Opacity < 1) img = Track(owned, new OpacityEffect { Source = img, Opacity = baseLayer.Opacity });
                acc = baseLayer.Blend == BlendMode.Normal
                    ? Track(owned, new CompositeEffect { Mode = CanvasComposite.SourceOver, Sources = { acc, img } })
                    : Track(owned, new BlendEffect { Background = acc, Foreground = img, Mode = ToEffect(baseLayer.Blend) });
            }
            i = j;
        }
        return acc;
    }

    private static T Track<T>(List<IDisposable> owned, T effect) where T : IDisposable
    {
        owned.Add(effect);
        return effect;
    }

    public static BlendEffectMode ToEffect(BlendMode m) => m switch
    {
        BlendMode.Multiply => BlendEffectMode.Multiply,
        BlendMode.Darken => BlendEffectMode.Darken,
        BlendMode.ColorBurn => BlendEffectMode.ColorBurn,
        BlendMode.Screen => BlendEffectMode.Screen,
        BlendMode.Lighten => BlendEffectMode.Lighten,
        BlendMode.ColorDodge => BlendEffectMode.ColorDodge,
        BlendMode.Overlay => BlendEffectMode.Overlay,
        BlendMode.SoftLight => BlendEffectMode.SoftLight,
        BlendMode.HardLight => BlendEffectMode.HardLight,
        BlendMode.Difference => BlendEffectMode.Difference,
        BlendMode.Exclusion => BlendEffectMode.Exclusion,
        BlendMode.Hue => BlendEffectMode.Hue,
        BlendMode.Saturation => BlendEffectMode.Saturation,
        BlendMode.Color => BlendEffectMode.Color,
        BlendMode.Luminosity => BlendEffectMode.Luminosity,
        _ => BlendEffectMode.Multiply,
    };

    /// <summary>Imagen aplanada del documento.</summary>
    public CanvasRenderTarget Flatten(bool includeBackground = true, IReadOnlyDictionary<Layer, ICanvasImage>? overrides = null)
    {
        var rt = new CanvasRenderTarget(Device, Width, Height, 96);
        var owned = new List<IDisposable>();
        using (var ds = rt.CreateDrawingSession())
        {
            ds.Clear(Colors.Transparent);
            ds.DrawImage(Compose(overrides, owned, includeBackground));
        }
        owned.ForEach(o => o.Dispose());
        return rt;
    }

    /// <summary>Color visible en un punto del documento (null si es transparente).</summary>
    public Color? SampleColor(float x, float y, IReadOnlyDictionary<Layer, ICanvasImage>? overrides)
    {
        if (x < 0 || y < 0 || x >= Width || y >= Height) return null;
        using var rt = new CanvasRenderTarget(Device, 1, 1, 96);
        var owned = new List<IDisposable>();
        using (var ds = rt.CreateDrawingSession())
        {
            ds.Clear(Colors.Transparent);
            ds.Transform = System.Numerics.Matrix3x2.CreateTranslation(-MathF.Floor(x), -MathF.Floor(y));
            ds.DrawImage(Compose(overrides, owned));
        }
        owned.ForEach(o => o.Dispose());
        var p = rt.GetPixelBytes();
        if (p[3] == 0) return null;
        // BGRA premultiplicado → sin premultiplicar
        byte Un(byte v) => (byte)Math.Min(255, v * 255 / p[3]);
        return Color.FromArgb(255, Un(p[2]), Un(p[1]), Un(p[0]));
    }

    // ---------- Archivo ----------

    public async Task<ProjectData> ToProjectDataAsync()
    {
        var layers = new List<LayerData>();
        foreach (var l in Layers)
            layers.Add(new LayerData(l.ToProperties(), await EncodePngAsync(l.Surface)));
        byte[] thumb;
        using (var flat = Flatten())
        using (var small = Thumbnail(flat, 480))
            thumb = await EncodePngAsync(small);
        return new ProjectData
        {
            Name = Name,
            Width = Width,
            Height = Height,
            BackgroundColor = ToRgba(Background).ToHex(),
            BackgroundVisible = BackgroundVisible,
            ActiveLayer = ActiveIndex,
            Layers = layers,
            ThumbnailPng = thumb,
            Created = Created,
        };
    }

    public static async Task<PaintDocument> FromProjectDataAsync(CanvasDevice device, ProjectData p, string? path)
    {
        var d = new PaintDocument(device, p.Width, p.Height, p.Name) { FilePath = path, Created = p.Created };
        if (Rgba.TryParseHex(p.BackgroundColor, out var bg)) d.Background = Color.FromArgb(255, bg.R, bg.G, bg.B);
        d.BackgroundVisible = p.BackgroundVisible;
        foreach (var ld in p.Layers)
        {
            var layer = new Layer(device, p.Width, p.Height, ld.Properties.Name) { Id = ld.Properties.Id };
            layer.Apply(ld.Properties);
            if (ld.Png.Length > 0)
            {
                using var bmp = await DecodeAsync(device, ld.Png);
                using var ds = layer.Surface.CreateDrawingSession();
                ds.DrawImage(bmp);
            }
            d.Layers.Add(layer);
        }
        if (d.Layers.Count == 0) d.Layers.Add(d.CreateLayer());
        d.ActiveIndex = p.ActiveLayer;
        return d;
    }

    public static CanvasRenderTarget Thumbnail(CanvasBitmap source, float maxSide)
    {
        var s = source.SizeInPixels;
        var k = Math.Min(1, maxSide / Math.Max(s.Width, s.Height));
        var rt = new CanvasRenderTarget(source.Device, Math.Max(1, s.Width * k), Math.Max(1, s.Height * k), 96);
        using var ds = rt.CreateDrawingSession();
        ds.Clear(Colors.Transparent);
        ds.DrawImage(source, new Rect(0, 0, rt.Size.Width, rt.Size.Height), new Rect(0, 0, s.Width, s.Height), 1, CanvasImageInterpolation.HighQualityCubic);
        return rt;
    }

    public static async Task<byte[]> EncodePngAsync(CanvasBitmap bitmap)
    {
        using var stream = new InMemoryRandomAccessStream();
        await bitmap.SaveAsync(stream, CanvasBitmapFileFormat.Png);
        var bytes = new byte[stream.Size];
        stream.Seek(0);
        await stream.ReadAsync(bytes.AsBuffer(), (uint)stream.Size, InputStreamOptions.None);
        return bytes;
    }

    public static async Task<byte[]> EncodeAsync(CanvasBitmap bitmap, CanvasBitmapFileFormat format, float quality = 0.92f)
    {
        using var stream = new InMemoryRandomAccessStream();
        await bitmap.SaveAsync(stream, format, quality);
        var bytes = new byte[stream.Size];
        stream.Seek(0);
        await stream.ReadAsync(bytes.AsBuffer(), (uint)stream.Size, InputStreamOptions.None);
        return bytes;
    }

    public static async Task<CanvasBitmap> DecodeAsync(CanvasDevice device, byte[] data)
    {
        using var stream = new InMemoryRandomAccessStream();
        await stream.WriteAsync(data.AsBuffer());
        stream.Seek(0);
        return await CanvasBitmap.LoadAsync(device, stream, 96);
    }

    public static Rgba ToRgba(Color c) => new(c.R, c.G, c.B, c.A);
    public static Color ToColor(Rgba c) => Color.FromArgb(c.A, c.R, c.G, c.B);

    public void Dispose()
    {
        History.Clear();
        foreach (var l in Layers) l.Dispose();
        Layers.Clear();
    }
}
