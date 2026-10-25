using System.Numerics;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Effects;
using Trazo.Core.Brushes;
using Trazo.Core.Imaging;
using Trazo.Core.Strokes;
using Windows.Foundation;
using Windows.Graphics.DirectX;
using Windows.UI;

namespace Trazo.Rendering;

public enum ToolKind
{
    Brush,
    Smudge,
    Eraser,
}

/// <summary>Puntas teñidas por color, generadas una vez y guardadas en la GPU.</summary>
public sealed class TipCache(CanvasDevice device)
{
    private readonly Dictionary<TipShape, byte[]> _masks = [];
    private readonly Dictionary<(TipShape, Color), CanvasBitmap> _tinted = [];

    public static readonly Rect TipRect = new(0, 0, TipGenerator.DefaultSize, TipGenerator.DefaultSize);

    public CanvasBitmap Get(TipShape shape, Color color)
    {
        if (_tinted.TryGetValue((shape, color), out var bmp)) return bmp;
        if (_tinted.Count > 64)
        {
            foreach (var b in _tinted.Values) b.Dispose();
            _tinted.Clear();
        }
        if (!_masks.TryGetValue(shape, out var mask)) _masks[shape] = mask = TipGenerator.Generate(shape);
        var bytes = TipGenerator.Tint(mask, color.R, color.G, color.B);
        bmp = CanvasBitmap.CreateFromBytes(device, bytes, TipGenerator.DefaultSize, TipGenerator.DefaultSize,
            DirectXPixelFormat.B8G8R8A8UIntNormalized, 96, CanvasAlphaMode.Premultiplied);
        _tinted[(shape, color)] = bmp;
        return bmp;
    }

    /// <summary>Dibuja un sello centrado en (x, y).</summary>
    public static void DrawDab(CanvasDrawingSession ds, CanvasBitmap tip, in Dab d)
    {
        ds.Transform = Matrix3x2.CreateRotation(d.Rotation) * Matrix3x2.CreateTranslation(d.X, d.Y);
        var h = d.Size / 2;
        ds.DrawImage(tip, new Rect(-h, -h, d.Size, d.Size), TipRect, Math.Clamp(d.Alpha, 0, 1), CanvasImageInterpolation.Linear);
    }
}

/// <summary>
/// Motor de pintura. Cada trazo se acumula en un búfer y se compone sobre la capa
/// al terminar, con la opacidad de la herramienta (como en Procreate). Difuminar
/// trabaja directamente sobre la capa.
/// </summary>
public sealed class BrushEngine(PaintDocument doc, LayerOperations ops, TipCache tips, GrainCache? grainCache = null)
{
    private readonly GrainCache _grains = grainCache ?? new GrainCache(doc.Device);
    private CanvasRenderTarget? _buffer;
    private CanvasRenderTarget? _tailBuffer; // cola provisional del afinado final
    private StrokeRasterizer? _raster;
    private readonly List<Dab> _tailDabs = [];
    private CanvasRenderTarget? _patch, _mask;
    private readonly List<Dab> _dabs = [];
    private readonly List<Dab> _expanded = [];

    private Layer? _layer;
    private ToolKind _tool;
    private BrushPreset? _brush;
    private Color _color;
    private float _size, _opacity;
    private SymmetryMode _symmetry;
    private StrokeBuilder? _builder;
    private Rect _dirty = Rect.Empty;
    private int _patchSize;
    private int _strokeCounter;

    public bool IsActive => _layer is not null;
    public QuickShape? Shape => _builder?.Shape;
    public Layer? Layer => _layer;
    /// <summary>Clave de la imagen que sustituye la vista previa del trazo (la capa o su máscara).</summary>
    public object? PreviewKey => _layer?.TargetKey;
    private bool OnMask => _layer?.EditingMask == true;
    /// <summary>Semilla del último trazo (para reproducirlo exactamente).</summary>
    public int LastSeed { get; private set; }

    /// <summary>Empieza un trazo. Devuelve false si la capa activa está oculta.</summary>
    /// <param name="seed">Semilla de la variación aleatoria; si es null se usa un contador determinista.</param>
    /// <param name="hasPressure">El dispositivo informa presión (lápiz); si no, se usa el afinado táctil.</param>
    public bool Begin(InputPoint p, ToolKind tool, BrushPreset brush, Color color, float size, float opacity, SymmetryMode symmetry, float viewScale, int? seed = null, bool hasPressure = true)
    {
        var layer = doc.ActiveLayer;
        if (!layer.Visible) return false;
        _layer = layer;
        _tool = tool;
        _brush = brush;
        // En una máscara se pinta la luminancia del color (negro oculta, blanco muestra) y el borrador restaura blanco
        _color = layer.EditingMask
            ? tool == ToolKind.Eraser ? KnownColors.White : LayerOperations.ToMaskGray(color)
            : tool == ToolKind.Eraser ? KnownColors.Black : color;
        _size = size;
        _opacity = opacity;
        _symmetry = tool == ToolKind.Smudge ? SymmetryMode.None : symmetry;
        LastSeed = seed ?? ++_strokeCounter;
        _builder = new StrokeBuilder(brush, size, viewScale, LastSeed, tool == ToolKind.Smudge ? Math.Max(brush.Spacing, 0.12f) : null, hasPressure);
        _raster?.Dispose();
        _raster = new StrokeRasterizer(tips, _grains, brush, _color, size, LastSeed, allowGrain: tool != ToolKind.Smudge);
        _dirty = Rect.Empty;
        if (_patch is not null) { _patch.Dispose(); _mask!.Dispose(); _patch = _mask = null; }

        if (_buffer is null || _buffer.SizeInPixels.Width != doc.Width || _buffer.SizeInPixels.Height != doc.Height)
        {
            _buffer?.Dispose();
            _buffer = new CanvasRenderTarget(doc.Device, doc.Width, doc.Height, 96);
        }
        using (var ds = _buffer.CreateDrawingSession()) ds.Clear(KnownColors.Transparent);
        if (_builder.Taper.End > 0 && tool != ToolKind.Smudge)
        {
            if (_tailBuffer is null || _tailBuffer.SizeInPixels != _buffer.SizeInPixels)
            {
                _tailBuffer?.Dispose();
                _tailBuffer = new CanvasRenderTarget(doc.Device, doc.Width, doc.Height, 96);
            }
            using var ts = _tailBuffer.CreateDrawingSession();
            ts.Clear(KnownColors.Transparent);
        }
        ops.Snapshot(layer);
        Add(p);
        return true;
    }

    public void Add(InputPoint p)
    {
        if (_layer is null || _builder is null) return;
        _dabs.Clear();
        if (!_builder.Add(p, _dabs)) return;
        if (_builder.Shape is not null) RedrawShape();
        else
        {
            if (_dabs.Count > 0) Render(_dabs);
            if (_builder.TailChanged) RenderTail();
        }
    }

    private bool TailActive => _tailBuffer is not null && _builder?.Taper.End > 0 && _tool != ToolKind.Smudge;

    // La cola se redibuja entera en cada cambio: son los últimos End × diámetro px
    private void RenderTail()
    {
        if (!TailActive) return;
        _tailDabs.Clear();
        _builder!.TailPreview(_tailDabs);
        using var ds = _tailBuffer!.CreateDrawingSession();
        ds.Clear(KnownColors.Transparent);
        _raster!.Draw(ds, Expand(_tailDabs));
    }

    private List<Dab> Expand(List<Dab> dabs)
    {
        _expanded.Clear();
        float cx = doc.Width / 2f, cy = doc.Height / 2f;
        foreach (var d in dabs) Symmetry.Expand(d, _symmetry, cx, cy, _expanded);
        return _expanded;
    }

    private void Render(List<Dab> dabs)
    {
        if (_tool == ToolKind.Smudge)
        {
            foreach (var d in dabs) Smudge(d);
            return;
        }
        var expanded = Expand(dabs);
        using var ds = _buffer!.CreateDrawingSession();
        _raster!.Draw(ds, expanded);
        foreach (var d in expanded) Grow(d.X, d.Y, d.Size * 1.5f);
    }

    private void Grow(float x, float y, float size)
    {
        var h = size / 2 + 2;
        var r = new Rect(x - h, y - h, h * 2, h * 2);
        if (_dirty.IsEmpty) _dirty = r;
        else _dirty.Union(r);
    }

    // Difuminar: arrastra un parche de píxeles que se mezcla poco a poco con lo que encuentra
    private void Smudge(Dab d)
    {
        var src = _layer!.Target;
        if (_patch is null)
        {
            _patchSize = Math.Max(4, (int)MathF.Ceiling(_size));
            _patch = new CanvasRenderTarget(doc.Device, _patchSize, _patchSize, 96);
            _mask = new CanvasRenderTarget(doc.Device, _patchSize, _patchSize, 96);
            using var pds = _patch.CreateDrawingSession();
            pds.Clear(KnownColors.Transparent);
            pds.DrawImage(src, new Rect(0, 0, _patchSize, _patchSize), Around(d.X, d.Y, _patchSize));
            Grow(d.X, d.Y, _patchSize);
            return;
        }
        var S = _patchSize;
        using (var mds = _mask!.CreateDrawingSession())
        {
            mds.Clear(KnownColors.Transparent);
            mds.DrawImage(_patch);
            var tip = tips.Get(_brush!.Tip, KnownColors.White);
            mds.Transform = Matrix3x2.CreateScale(S / (float)TipGenerator.DefaultSize);
            mds.DrawImage(tip, Vector2.Zero, TipCache.TipRect, 1, CanvasImageInterpolation.Linear, CanvasComposite.DestinationIn);
        }
        using (var lds = src.CreateDrawingSession())
        {
            var alpha = Math.Clamp(_opacity * (0.35f + 0.65f * d.Alpha) * 0.6f, 0, 1);
            lds.DrawImage(_mask, new Rect(d.X - d.Size / 2, d.Y - d.Size / 2, d.Size, d.Size), new Rect(0, 0, S, S), alpha,
                CanvasImageInterpolation.Linear, _layer.TargetAlphaLock ? CanvasComposite.SourceAtop : CanvasComposite.SourceOver);
        }
        using (var pds = _patch.CreateDrawingSession())
            pds.DrawImage(src, new Rect(0, 0, S, S), Around(d.X, d.Y, S), 0.35f - 0.3f * _opacity);
        Grow(d.X, d.Y, d.Size);
    }

    private static Rect Around(float x, float y, int s) => new(x - s / 2f, y - s / 2f, s, s);

    private CanvasComposite Composite => _tool == ToolKind.Eraser && !OnMask
        ? CanvasComposite.DestinationOut
        : _layer!.TargetAlphaLock ? CanvasComposite.SourceAtop : CanvasComposite.SourceOver;

    /// <summary>Imagen de vista previa de la capa con el trazo en curso.</summary>
    public ICanvasImage? Preview(List<IDisposable> owned)
    {
        if (_layer is null || _tool == ToolKind.Smudge || _buffer is null) return null;
        ICanvasImage stroke = _buffer;
        if (TailActive)
        {
            var withTail = new CompositeEffect { Sources = { _buffer, _tailBuffer } };
            owned.Add(withTail);
            stroke = withTail;
        }
        var buf = new OpacityEffect { Source = _raster!.Finish(stroke, owned), Opacity = _opacity };
        var comp = new CompositeEffect { Mode = Composite, Sources = { _layer.Target, buf } };
        owned.Add(buf);
        owned.Add(comp);
        // con selección, la vista previa muestra solo lo que se confirmará (SE-01)
        return doc.SelectionBlend(_layer.Target, comp, owned);
    }

    public void End()
    {
        if (_layer is null) return;
        // StreamLine: alcanzar el último punto real
        _dabs.Clear();
        _builder!.End(_dabs);
        if (_dabs.Count > 0) Render(_dabs);
        var layer = _layer;
        if (_tool != ToolKind.Smudge && !_dirty.IsEmpty)
        {
            var r = PixelPatchAction.Clip(_dirty, doc.Width, doc.Height);
            if (!r.IsEmpty)
            {
                var owned = new List<IDisposable>();
                using (var ds = layer.Target.CreateDrawingSession())
                    ds.DrawImage(_raster!.Finish(_buffer!, owned), new Vector2((float)r.X, (float)r.Y), r, _opacity, CanvasImageInterpolation.NearestNeighbor, Composite);
                owned.ForEach(o => o.Dispose());
            }
        }
        if (!_dirty.IsEmpty)
            ops.CommitPixels(layer, _dirty, _tool switch { ToolKind.Eraser => "Borrar", ToolKind.Smudge => "Difuminar", _ => "Pintar" });
        _layer = null;
        _builder = null;
        _raster?.Dispose();
        _raster = null;
    }

    public void Cancel()
    {
        if (_layer is null) return;
        // difuminar escribe directamente en la capa: se restaura desde la copia previa
        if (_tool == ToolKind.Smudge && ops.CurrentSnapshot is { } snapshot)
        {
            _layer.Target.CopyPixelsFromBitmap(snapshot);
            _layer.Touch();
        }
        _layer = null;
        _builder = null;
        _raster?.Dispose();
        _raster = null;
    }

    // ---------- QuickShape ----------

    public bool TrySnapShape(float viewScale)
    {
        if (_layer is null || _builder is null || _tool == ToolKind.Smudge) return false;
        if (!_builder.TrySnapShape(viewScale)) return false;
        RedrawShape();
        return true;
    }

    private void RedrawShape()
    {
        using (var ds = _buffer!.CreateDrawingSession()) ds.Clear(KnownColors.Transparent);
        if (TailActive) using (var ts = _tailBuffer!.CreateDrawingSession()) ts.Clear(KnownColors.Transparent);
        _dirty = Rect.Empty;
        _dabs.Clear();
        _builder!.ShapeDabs(_dabs);
        if (_dabs.Count > 0) Render(_dabs);
    }

    // ---------- Relleno ----------

    public bool Fill(float x, float y, Color color, int tolerance)
    {
        var layer = doc.ActiveLayer;
        if (!layer.Visible) return false;
        int ix = (int)MathF.Floor(x), iy = (int)MathF.Floor(y);
        if (ix < 0 || iy < 0 || ix >= doc.Width || iy >= doc.Height) return false;
        byte[] pixels;
        using (var flat = doc.Flatten()) pixels = flat.GetPixelBytes();
        var mask = FloodFill.Compute(pixels, doc.Width, doc.Height, ix, iy, tolerance);
        if (mask is null) return false;
        if (layer.EditingMask) color = LayerOperations.ToMaskGray(color);
        var bgra = FloodFill.ToBgra(mask, doc.Width, color.R, color.G, color.B);
        var b = mask.Bounds;
        using var bmp = CanvasBitmap.CreateFromBytes(doc.Device, bgra, b.Width, b.Height, DirectXPixelFormat.B8G8R8A8UIntNormalized, 96, CanvasAlphaMode.Premultiplied);
        ops.Snapshot(layer);
        using (var ds = layer.Target.CreateDrawingSession())
            ds.DrawImage(bmp, new Vector2(b.X, b.Y), new Rect(0, 0, b.Width, b.Height), 1, CanvasImageInterpolation.NearestNeighbor,
                layer.TargetAlphaLock ? CanvasComposite.SourceAtop : CanvasComposite.SourceOver);
        ops.CommitPixels(layer, new Rect(b.X, b.Y, b.Width, b.Height), "Relleno");
        return true;
    }

    // ---------- Vista previa de pinceles ----------

    /// <summary>Trazo de muestra para la biblioteca, con el mismo pipeline que el lienzo (afinado y grano incluidos).</summary>
    public static void DrawPreview(CanvasDrawingSession ds, TipCache tips, GrainCache grains, BrushPreset brush, float w, float h, Color color)
    {
        var size = Math.Min(h * 0.42f, Math.Max(6, brush.Size * 0.6f));
        const int n = 160;
        var samples = Enumerable.Range(0, n + 1).Select(i =>
        {
            var t = i / (float)n;
            return new InputPoint(10 + t * (w - 20), h / 2 + MathF.Sin(t * MathF.Tau) * h * 0.22f, MathF.Sin(t * MathF.PI));
        });
        var dabs = StrokeBuilder.Build(brush, size, 1, seed: 3, samples, out _);
        if (brush.Flow < 0.3f) dabs = dabs.Select(d => d with { Alpha = Math.Min(1, d.Alpha * 2) }).ToList();

        using var raster = new StrokeRasterizer(tips, grains, brush, color, size, seed: 3);
        using var stroke = new CanvasRenderTarget(ds, w, h);
        using (var sds = stroke.CreateDrawingSession())
        {
            sds.Clear(KnownColors.Transparent);
            raster.Draw(sds, dabs);
        }
        var owned = new List<IDisposable>();
        ds.Transform = Matrix3x2.Identity;
        ds.DrawImage(raster.Finish(stroke, owned));
        owned.ForEach(o => o.Dispose());
    }
}
