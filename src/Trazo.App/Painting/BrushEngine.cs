using System.Numerics;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Effects;
using Microsoft.UI;
using Trazo.Core.Brushes;
using Trazo.Core.Imaging;
using Trazo.Core.Strokes;
using Windows.Foundation;
using Windows.Graphics.DirectX;
using Windows.UI;

namespace Trazo.App.Painting;

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
public sealed class BrushEngine(PaintDocument doc, LayerOperations ops, TipCache tips)
{
    private CanvasRenderTarget? _buffer;
    private CanvasRenderTarget? _patch, _mask;
    private readonly List<Dab> _dabs = [];
    private readonly List<Dab> _expanded = [];

    private Layer? _layer;
    private ToolKind _tool;
    private BrushPreset? _brush;
    private Color _color;
    private float _size, _opacity;
    private SymmetryMode _symmetry;
    private StreamLineSmoother? _smoother;
    private StrokeStamper? _stamper;
    private readonly List<InputPoint> _raw = [];
    private Rect _dirty = Rect.Empty;
    private int _patchSize;

    public bool IsActive => _layer is not null;
    public QuickShape? Shape { get; private set; }
    public Layer? Layer => _layer;

    /// <summary>Empieza un trazo. Devuelve false si la capa activa está oculta.</summary>
    public bool Begin(InputPoint p, ToolKind tool, BrushPreset brush, Color color, float size, float opacity, SymmetryMode symmetry, float viewScale)
    {
        var layer = doc.ActiveLayer;
        if (!layer.Visible) return false;
        _layer = layer;
        _tool = tool;
        _brush = brush;
        _color = tool == ToolKind.Eraser ? Colors.Black : color;
        _size = size;
        _opacity = opacity;
        _symmetry = tool == ToolKind.Smudge ? SymmetryMode.None : symmetry;
        _smoother = new StreamLineSmoother(brush.StreamLine * 26 / viewScale);
        _stamper = new StrokeStamper(brush, size, tool == ToolKind.Smudge ? Math.Max(brush.Spacing, 0.12f) : null);
        _raw.Clear();
        _dirty = Rect.Empty;
        Shape = null;
        if (_patch is not null) { _patch.Dispose(); _mask!.Dispose(); _patch = _mask = null; }

        if (_buffer is null || _buffer.SizeInPixels.Width != doc.Width || _buffer.SizeInPixels.Height != doc.Height)
        {
            _buffer?.Dispose();
            _buffer = new CanvasRenderTarget(doc.Device, doc.Width, doc.Height, 96);
        }
        using (var ds = _buffer.CreateDrawingSession()) ds.Clear(Colors.Transparent);
        ops.Snapshot(layer);
        Add(p);
        return true;
    }

    public void Add(InputPoint p)
    {
        if (_layer is null) return;
        _raw.Add(p);
        if (Shape is not null)
        {
            Shape.Adjust(new Vector2(p.X, p.Y));
            RedrawShape();
            return;
        }
        if (_smoother!.Push(p) is { } s) StampTo(s);
    }

    private void StampTo(InputPoint p)
    {
        _dabs.Clear();
        _stamper!.LineTo(p, _dabs);
        if (_dabs.Count > 0) Render(_dabs);
    }

    private void Render(List<Dab> dabs)
    {
        if (_tool == ToolKind.Smudge)
        {
            foreach (var d in dabs) Smudge(d);
            return;
        }
        _expanded.Clear();
        float cx = doc.Width / 2f, cy = doc.Height / 2f;
        foreach (var d in dabs) Symmetry.Expand(d, _symmetry, cx, cy, _expanded);
        var tip = tips.Get(_brush!.Tip, _color);
        using var ds = _buffer!.CreateDrawingSession();
        foreach (var d in _expanded)
        {
            TipCache.DrawDab(ds, tip, d);
            Grow(d.X, d.Y, d.Size * 1.5f);
        }
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
        var src = _layer!.Surface;
        if (_patch is null)
        {
            _patchSize = Math.Max(4, (int)MathF.Ceiling(_size));
            _patch = new CanvasRenderTarget(doc.Device, _patchSize, _patchSize, 96);
            _mask = new CanvasRenderTarget(doc.Device, _patchSize, _patchSize, 96);
            using var pds = _patch.CreateDrawingSession();
            pds.Clear(Colors.Transparent);
            pds.DrawImage(src, new Rect(0, 0, _patchSize, _patchSize), Around(d.X, d.Y, _patchSize));
            Grow(d.X, d.Y, _patchSize);
            return;
        }
        var S = _patchSize;
        using (var mds = _mask!.CreateDrawingSession())
        {
            mds.Clear(Colors.Transparent);
            mds.DrawImage(_patch);
            var tip = tips.Get(_brush!.Tip, Colors.White);
            mds.Transform = Matrix3x2.CreateScale(S / (float)TipGenerator.DefaultSize);
            mds.DrawImage(tip, Vector2.Zero, TipCache.TipRect, 1, CanvasImageInterpolation.Linear, CanvasComposite.DestinationIn);
        }
        using (var lds = src.CreateDrawingSession())
        {
            var alpha = Math.Clamp(_opacity * (0.35f + 0.65f * d.Alpha) * 0.6f, 0, 1);
            lds.DrawImage(_mask, new Rect(d.X - d.Size / 2, d.Y - d.Size / 2, d.Size, d.Size), new Rect(0, 0, S, S), alpha,
                CanvasImageInterpolation.Linear, _layer.AlphaLock ? CanvasComposite.SourceAtop : CanvasComposite.SourceOver);
        }
        using (var pds = _patch.CreateDrawingSession())
            pds.DrawImage(src, new Rect(0, 0, S, S), Around(d.X, d.Y, S), 0.35f - 0.3f * _opacity);
        Grow(d.X, d.Y, d.Size);
    }

    private static Rect Around(float x, float y, int s) => new(x - s / 2f, y - s / 2f, s, s);

    private CanvasComposite Composite => _tool == ToolKind.Eraser
        ? CanvasComposite.DestinationOut
        : _layer!.AlphaLock ? CanvasComposite.SourceAtop : CanvasComposite.SourceOver;

    /// <summary>Imagen de vista previa de la capa con el trazo en curso.</summary>
    public ICanvasImage? Preview(List<IDisposable> owned)
    {
        if (_layer is null || _tool == ToolKind.Smudge || _buffer is null) return null;
        var buf = new OpacityEffect { Source = _buffer, Opacity = _opacity };
        var comp = new CompositeEffect { Mode = Composite, Sources = { _layer.Surface, buf } };
        owned.Add(buf);
        owned.Add(comp);
        return comp;
    }

    public void End()
    {
        if (_layer is null) return;
        // StreamLine: alcanzar el último punto real
        if (Shape is null && _raw.Count > 1 && _smoother!.Radius > 0)
        {
            var last = _raw[^1];
            StampTo(last with { Pressure = _smoother.Current?.Pressure ?? last.Pressure });
        }
        var layer = _layer;
        if (_tool != ToolKind.Smudge && !_dirty.IsEmpty)
        {
            var r = PixelPatchAction.Clip(_dirty, doc.Width, doc.Height);
            if (!r.IsEmpty)
            {
                using var ds = layer.Surface.CreateDrawingSession();
                ds.DrawImage(_buffer, new Vector2((float)r.X, (float)r.Y), r, _opacity, CanvasImageInterpolation.NearestNeighbor, Composite);
            }
        }
        if (!_dirty.IsEmpty)
            ops.CommitPixels(layer, _dirty, _tool switch { ToolKind.Eraser => "Borrar", ToolKind.Smudge => "Difuminar", _ => "Pintar" });
        _layer = null;
        Shape = null;
    }

    public void Cancel()
    {
        if (_layer is null) return;
        // difuminar escribe directamente en la capa: se restaura desde la copia previa
        if (_tool == ToolKind.Smudge && ops.CurrentSnapshot is { } snapshot)
        {
            _layer.Surface.CopyPixelsFromBitmap(snapshot);
            _layer.Touch();
        }
        _layer = null;
        Shape = null;
    }

    // ---------- QuickShape ----------

    public bool TrySnapShape(float viewScale)
    {
        if (_layer is null || _tool == ToolKind.Smudge || Shape is not null) return false;
        Shape = QuickShape.Detect(_raw, viewScale);
        if (Shape is null) return false;
        RedrawShape();
        return true;
    }

    private void RedrawShape()
    {
        using (var ds = _buffer!.CreateDrawingSession()) ds.Clear(Colors.Transparent);
        _dirty = Rect.Empty;
        _stamper!.Reset();
        foreach (var v in Shape!.Path())
        {
            _dabs.Clear();
            _stamper.LineTo(new InputPoint(v.X, v.Y, Shape.Pressure), _dabs);
            if (_dabs.Count > 0) Render(_dabs);
        }
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
        var bgra = FloodFill.ToBgra(mask, doc.Width, color.R, color.G, color.B);
        var b = mask.Bounds;
        using var bmp = CanvasBitmap.CreateFromBytes(doc.Device, bgra, b.Width, b.Height, DirectXPixelFormat.B8G8R8A8UIntNormalized, 96, CanvasAlphaMode.Premultiplied);
        ops.Snapshot(layer);
        using (var ds = layer.Surface.CreateDrawingSession())
            ds.DrawImage(bmp, new Vector2(b.X, b.Y), new Rect(0, 0, b.Width, b.Height), 1, CanvasImageInterpolation.NearestNeighbor,
                layer.AlphaLock ? CanvasComposite.SourceAtop : CanvasComposite.SourceOver);
        ops.CommitPixels(layer, new Rect(b.X, b.Y, b.Width, b.Height), "Relleno");
        return true;
    }

    // ---------- Vista previa de pinceles ----------

    public static void DrawPreview(CanvasDrawingSession ds, TipCache tips, BrushPreset brush, float w, float h, Color color)
    {
        var tip = tips.Get(brush.Tip, color);
        var size = Math.Min(h * 0.42f, Math.Max(6, brush.Size * 0.6f));
        var stamper = new StrokeStamper(brush, size, rng: new Random(3));
        var dabs = new List<Dab>();
        const int n = 160;
        for (var i = 0; i <= n; i++)
        {
            var t = i / (float)n;
            stamper.LineTo(new InputPoint(10 + t * (w - 20), h / 2 + MathF.Sin(t * MathF.Tau) * h * 0.22f, MathF.Sin(t * MathF.PI)), dabs);
        }
        foreach (var d in dabs)
            TipCache.DrawDab(ds, tip, brush.Flow < 0.3f ? d with { Alpha = Math.Min(1, d.Alpha * 2) } : d);
        ds.Transform = Matrix3x2.Identity;
    }
}
