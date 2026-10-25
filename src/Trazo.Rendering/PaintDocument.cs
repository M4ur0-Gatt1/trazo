using System.Collections.ObjectModel;
using System.Runtime.InteropServices.WindowsRuntime;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Effects;
using Trazo.Core.Colors;
using Trazo.Core.Documents;
using Trazo.Core.Geometry;
using Trazo.Core.History;
using Trazo.Core.IO;
using Trazo.Core.Selection;
using Windows.Foundation;
using Windows.Graphics.DirectX;
using Windows.Storage.Streams;
using Windows.UI;

namespace Trazo.Rendering;

/// <summary>Clave de sustitución para la imagen de la máscara de una capa (vista previa al pintarla).</summary>
public sealed record MaskKey(Layer Layer);

/// <summary>Capa con sus píxeles (y su máscara opcional) en texturas de la GPU.</summary>
public sealed partial class Layer : ObservableObject, IDisposable
{
    private string _name;
    private float _opacity = 1;
    private BlendMode _blend;
    private bool _visible = true;
    private bool _alphaLock;
    private bool _clip;
    private int _revision;
    private CanvasRenderTarget? _mask;
    private bool _editingMask;
    private string? _groupId;

    public Layer(CanvasDevice device, int width, int height, string name)
    {
        Surface = new CanvasRenderTarget(device, width, height, 96);
        using (var ds = Surface.CreateDrawingSession()) ds.Clear(KnownColors.Transparent);
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
    public string? GroupId { get => _groupId; set => SetProperty(ref _groupId, value); }
    public string BlendShort => _blend.ShortName();

    /// <summary>Máscara de capa (LY-02): textura opaca en gris, blanco = visible, negro = oculto.</summary>
    public CanvasRenderTarget? Mask
    {
        get => _mask;
        set
        {
            if (!SetProperty(ref _mask, value)) return;
            if (value is null) EditingMask = false;
            OnPropertyChanged(nameof(HasMask));
        }
    }

    public bool HasMask => _mask is not null;

    /// <summary>Las herramientas de píxeles trabajan sobre la máscara en lugar de sobre la capa.</summary>
    public bool EditingMask { get => _editingMask; set => SetProperty(ref _editingMask, value && _mask is not null); }

    /// <summary>Textura sobre la que actúan las herramientas: la máscara si se está editando, si no la capa.</summary>
    public CanvasRenderTarget Target => _editingMask && _mask is { } m ? m : Surface;

    /// <summary>Clave para sustituir la imagen de <see cref="Target"/> en la composición (vista previa).</summary>
    public object TargetKey => _editingMask && _mask is not null ? new MaskKey(this) : this;

    /// <summary>El bloqueo alfa solo se aplica a los píxeles de la capa, no a la máscara.</summary>
    public bool TargetAlphaLock => !_editingMask && _alphaLock;

    /// <summary>Aumenta con cada cambio de píxeles (para refrescar miniaturas).</summary>
    public int Revision { get => _revision; private set => SetProperty(ref _revision, value); }
    public void Touch() => Revision++;

    public static CanvasRenderTarget CreateMask(CanvasDevice device, int width, int height, Color fill)
    {
        var m = new CanvasRenderTarget(device, width, height, 96);
        using var ds = m.CreateDrawingSession();
        ds.Clear(fill);
        return m;
    }

    public LayerProperties ToProperties() => new()
    {
        Id = Id, Name = Name, Opacity = Opacity, Blend = Blend, Visible = Visible, AlphaLock = AlphaLock, Clip = Clip, GroupId = GroupId,
    };

    public void Apply(LayerProperties p)
    {
        Name = p.Name; Opacity = p.Opacity; Blend = p.Blend; Visible = p.Visible; AlphaLock = p.AlphaLock; Clip = p.Clip; GroupId = p.GroupId;
    }

    public Layer Duplicate(CanvasDevice device)
    {
        var l = new Layer(device, (int)Surface.SizeInPixels.Width, (int)Surface.SizeInPixels.Height, Name + " copia");
        l.Apply(ToProperties() with { Id = l.Id, Name = Name + " copia" });
        l.Surface.CopyPixelsFromBitmap(Surface);
        if (_mask is not null)
        {
            l.Mask = new CanvasRenderTarget(device, _mask.SizeInPixels.Width, _mask.SizeInPixels.Height, 96);
            l.Mask.CopyPixelsFromBitmap(_mask);
        }
        return l;
    }

    public void Dispose()
    {
        Surface.Dispose();
        _mask?.Dispose();
    }
}

/// <summary>Grupo de capas (LY-03): se compone aislado y se aplica con su opacidad y modo.</summary>
public sealed partial class LayerGroup : ObservableObject
{
    private string _name = "Grupo";
    private float _opacity = 1;
    private BlendMode _blend;
    private bool _visible = true;
    private bool _collapsed;

    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public string Name { get => _name; set => SetProperty(ref _name, value); }
    public float Opacity { get => _opacity; set => SetProperty(ref _opacity, Math.Clamp(value, 0, 1)); }
    public BlendMode Blend { get => _blend; set => SetProperty(ref _blend, value); }
    public bool Visible { get => _visible; set => SetProperty(ref _visible, value); }
    public bool Collapsed { get => _collapsed; set => SetProperty(ref _collapsed, value); }

    public GroupProperties ToProperties() => new() { Id = Id, Name = Name, Opacity = Opacity, Blend = Blend, Visible = Visible, Collapsed = Collapsed };

    public static LayerGroup From(GroupProperties p) => new()
    {
        Id = p.Id, Name = p.Name, Opacity = p.Opacity, Blend = p.Blend, Visible = p.Visible, Collapsed = p.Collapsed,
    };
}

/// <summary>Documento abierto: capas, grupos, fondo, selección, historial y archivo asociado.</summary>
public sealed partial class PaintDocument : ObservableObject, IDisposable
{
    private int _activeIndex;
    private Color _background = KnownColors.White;
    private bool _backgroundVisible = true;
    private string _name;
    private SelectionMask? _selection;
    private CanvasBitmap? _selectionBitmap;

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
    public ObservableCollection<LayerGroup> Groups { get; } = [];

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
    public IReadOnlyList<string?> GroupIds => Layers.Select(l => l.GroupId).ToList();

    public LayerGroup? Group(string? id) => id is null ? null : Groups.FirstOrDefault(g => g.Id == id);

    public Layer CreateLayer(string? name = null) =>
        new(Device, Width, Height, name ?? CanvasLimits.NextLayerName(Layers.Select(l => l.Name)));

    public static PaintDocument CreateBlank(CanvasDevice device, int width, int height, string name)
    {
        var d = new PaintDocument(device, width, height, name);
        d.Layers.Add(d.CreateLayer());
        return d;
    }

    /// <summary>Guía de dibujo del lienzo (DG-01), o null si no hay. Se guarda con el documento.</summary>
    public DrawingGuide? Guide { get; set; }

    // ---------- Selección (SE-01) ----------

    /// <summary>Selección activa, o null si no hay (todo el lienzo es editable).</summary>
    public SelectionMask? Selection
    {
        get => _selection;
        set
        {
            if (value is not null && (value.Width != Width || value.Height != Height)) throw new ArgumentException("Tamaño de selección incorrecto");
            _selection = value is { IsEmpty: true } ? null : value;
            _selectionBitmap?.Dispose();
            _selectionBitmap = null;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasSelection));
        }
    }

    public bool HasSelection => _selection is not null;

    /// <summary>La selección como imagen de la GPU (blanco con alfa = cobertura).</summary>
    public CanvasBitmap? SelectionBitmap
    {
        get
        {
            if (_selection is null) return null;
            if (_selectionBitmap is not null) return _selectionBitmap;
            var bgra = new byte[_selection.Data.Length * 4];
            for (var i = 0; i < _selection.Data.Length; i++)
                bgra[i * 4] = bgra[i * 4 + 1] = bgra[i * 4 + 2] = bgra[i * 4 + 3] = _selection.Data[i];
            _selectionBitmap = CanvasBitmap.CreateFromBytes(Device, bgra, Width, Height, DirectXPixelFormat.B8G8R8A8UIntNormalized, 96, CanvasAlphaMode.Premultiplied);
            return _selectionBitmap;
        }
    }

    /// <summary>
    /// Mezcla según la selección: nuevo × m + original × (1 − m). Sin selección devuelve <paramref name="modified"/>.
    /// Es la regla única que hace que todas las herramientas respeten la selección.
    /// </summary>
    public ICanvasImage SelectionBlend(ICanvasImage original, ICanvasImage modified, List<IDisposable> owned)
    {
        if (SelectionBitmap is not { } sel) return modified;
        var inside = Track(owned, new CompositeEffect { Mode = CanvasComposite.DestinationIn, Sources = { modified, sel } });
        var outside = Track(owned, new CompositeEffect { Mode = CanvasComposite.DestinationOut, Sources = { original, sel } });
        return Track(owned, new ArithmeticCompositeEffect { Source1 = inside, Source2 = outside, MultiplyAmount = 0, Source1Amount = 1, Source2Amount = 1, Offset = 0, ClampOutput = true });
    }

    // ---------- Composición ----------

    /// <summary>
    /// Construye el grafo de efectos que compone el documento. Las capas (o <see cref="MaskKey"/>) en
    /// <paramref name="overrides"/> se sustituyen por otra imagen (vista previa de trazos, ajustes o
    /// transformaciones). Los efectos creados se añaden a <paramref name="owned"/> para liberarlos después.
    /// Orden: máscara → recorte → opacidad y fusión de la capa → grupo aislado con su opacidad y fusión.
    /// </summary>
    public ICanvasImage Compose(IReadOnlyDictionary<object, ICanvasImage>? overrides, List<IDisposable> owned, bool includeBackground = true)
    {
        var acc = Solid(includeBackground && BackgroundVisible ? Background : KnownColors.Transparent, owned);
        var i = 0;
        while (i < Layers.Count)
        {
            var gid = Layers[i].GroupId;
            var j = i;
            while (j + 1 < Layers.Count && Layers[j + 1].GroupId == gid) j++;
            if (gid is null)
            {
                acc = ComposeRange(i, j, acc, overrides, owned);
            }
            else
            {
                var g = Group(gid);
                if (g is null || (g.Visible && g.Opacity > 0))
                {
                    var sub = ComposeRange(i, j, Solid(KnownColors.Transparent, owned), overrides, owned);
                    if (g is not null && g.Opacity < 1) sub = Track(owned, new OpacityEffect { Source = sub, Opacity = g.Opacity });
                    acc = BlendOver(acc, sub, g?.Blend ?? BlendMode.Normal, owned);
                }
            }
            i = j + 1;
        }
        return acc;
    }

    private ICanvasImage Solid(Color c, List<IDisposable> owned) =>
        Track(owned, new CropEffect { Source = Track(owned, new ColorSourceEffect { Color = c }), SourceRectangle = Bounds });

    private static ICanvasImage BlendOver(ICanvasImage acc, ICanvasImage img, BlendMode mode, List<IDisposable> owned) =>
        mode == BlendMode.Normal
            ? Track(owned, new CompositeEffect { Mode = CanvasComposite.SourceOver, Sources = { acc, img } })
            : Track(owned, new BlendEffect { Background = acc, Foreground = img, Mode = ToEffect(mode) });

    /// <summary>Imagen de una capa con su máscara aplicada (sin opacidad ni modo).</summary>
    public ICanvasImage LayerImage(Layer l, IReadOnlyDictionary<object, ICanvasImage>? overrides, List<IDisposable> owned)
    {
        ICanvasImage img = overrides is not null && overrides.TryGetValue(l, out var o) ? o : l.Surface;
        if (l.Mask is null) return img;
        ICanvasImage mask = overrides is not null && overrides.TryGetValue(new MaskKey(l), out var mo) ? mo : l.Mask;
        return Track(owned, new CompositeEffect { Mode = CanvasComposite.DestinationIn, Sources = { img, MaskToAlpha(mask, owned) } });
    }

    /// <summary>alfa = luminancia Rec. 709 de la máscara (blanco = visible).</summary>
    public static ICanvasImage MaskToAlpha(ICanvasImage mask, List<IDisposable> owned) => Track(owned, new ColorMatrixEffect
    {
        Source = mask,
        ClampOutput = true,
        ColorMatrix = new Matrix5x4 { M14 = 0.2126f, M24 = 0.7152f, M34 = 0.0722f, M51 = 1, M52 = 1, M53 = 1 },
    });

    /// <summary>Compone las capas [from, to] sobre transparente (contenido de un grupo, sin su opacidad ni su modo).</summary>
    public ICanvasImage ComposeLayers(int from, int to, List<IDisposable> owned) =>
        ComposeRange(from, to, Solid(KnownColors.Transparent, owned), null, owned);

    private ICanvasImage ComposeRange(int from, int to, ICanvasImage acc, IReadOnlyDictionary<object, ICanvasImage>? overrides, List<IDisposable> owned)
    {
        var i = from;
        while (i <= to)
        {
            var baseLayer = Layers[i];
            var j = i + 1;
            while (j <= to && Layers[j].Clip) j++;
            if (baseLayer.Visible && baseLayer.Opacity > 0)
            {
                var img = LayerImage(baseLayer, overrides, owned);
                for (var k = i + 1; k < j; k++)
                {
                    var c = Layers[k];
                    if (!c.Visible || c.Opacity <= 0) continue;
                    var ci = LayerImage(c, overrides, owned);
                    var top = c.Opacity < 1 ? Track(owned, new OpacityEffect { Source = ci, Opacity = c.Opacity }) : ci;
                    img = Track(owned, new CompositeEffect { Mode = CanvasComposite.SourceAtop, Sources = { img, top } });
                }
                if (baseLayer.Opacity < 1) img = Track(owned, new OpacityEffect { Source = img, Opacity = baseLayer.Opacity });
                acc = BlendOver(acc, img, baseLayer.Blend, owned);
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
    public CanvasRenderTarget Flatten(bool includeBackground = true, IReadOnlyDictionary<object, ICanvasImage>? overrides = null)
    {
        var rt = new CanvasRenderTarget(Device, Width, Height, 96);
        var owned = new List<IDisposable>();
        using (var ds = rt.CreateDrawingSession())
        {
            ds.Clear(KnownColors.Transparent);
            ds.DrawImage(Compose(overrides, owned, includeBackground));
        }
        owned.ForEach(o => o.Dispose());
        return rt;
    }

    /// <summary>Color visible en un punto del documento (null si es transparente).</summary>
    public Color? SampleColor(float x, float y, IReadOnlyDictionary<object, ICanvasImage>? overrides)
    {
        if (x < 0 || y < 0 || x >= Width || y >= Height) return null;
        using var rt = new CanvasRenderTarget(Device, 1, 1, 96);
        var owned = new List<IDisposable>();
        using (var ds = rt.CreateDrawingSession())
        {
            ds.Clear(KnownColors.Transparent);
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
            layers.Add(new LayerData(l.ToProperties(), await EncodePngAsync(l.Surface), l.Mask is null ? null : await EncodePngAsync(l.Mask)));
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
            Groups = Groups.Where(g => Layers.Any(l => l.GroupId == g.Id)).Select(g => g.ToProperties()).ToList(),
            Guide = Guide,
            ThumbnailPng = thumb,
            Created = Created,
        };
    }

    public static async Task<PaintDocument> FromProjectDataAsync(CanvasDevice device, ProjectData p, string? path)
    {
        var d = new PaintDocument(device, p.Width, p.Height, p.Name) { FilePath = path, Created = p.Created };
        if (Rgba.TryParseHex(p.BackgroundColor, out var bg)) d.Background = Color.FromArgb(255, bg.R, bg.G, bg.B);
        d.BackgroundVisible = p.BackgroundVisible;
        d.Guide = p.Guide;
        foreach (var g in p.Groups) d.Groups.Add(LayerGroup.From(g));
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
            if (ld.MaskPng is { Length: > 0 } maskPng)
            {
                layer.Mask = Layer.CreateMask(device, p.Width, p.Height, KnownColors.White);
                using var mbmp = await DecodeAsync(device, maskPng);
                using var ds = layer.Mask.CreateDrawingSession();
                ds.DrawImage(mbmp);
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
        ds.Clear(KnownColors.Transparent);
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
        _selectionBitmap?.Dispose();
    }
}
