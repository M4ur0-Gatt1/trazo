using System.Numerics;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Effects;
using Trazo.Core.Brushes;
using Trazo.Core.Geometry;
using Trazo.Core.Imaging;
using Trazo.Core.Strokes;
using Windows.Foundation;
using Windows.Graphics.DirectX;
using Windows.UI;
using static Trazo.Core.Localization.Tr;

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

    /// <summary>Máscara de cobertura de la punta (tamaño × tamaño, 0–255).</summary>
    public byte[] Mask(TipShape shape)
    {
        if (!_masks.TryGetValue(shape, out var mask)) _masks[shape] = mask = TipGenerator.Generate(shape);
        return mask;
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
    private CanvasRenderTarget? _capBuffer;  // tope de opacidad por presión (BR-06), en la GPU
    private OpacityCap? _cap;                // el mismo tope, calculado en la CPU
    private WetMixer? _wet;                  // mezcla húmeda (BR-04)
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
    private GuideAssist? _assist;
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
    public bool Begin(InputPoint p, ToolKind tool, BrushPreset brush, Color color, float size, float opacity, SymmetryMode symmetry, float viewScale, int? seed = null, bool hasPressure = true, GuideAssist? assist = null)
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
        // Asistente de dibujo (DG-01): los puntos se pegan a la guía antes de la simetría
        _assist = assist;
        _assist?.Begin(new Vector2(p.X, p.Y));
        LastSeed = seed ?? ++_strokeCounter;
        _builder = new StrokeBuilder(brush, size, viewScale, LastSeed, tool == ToolKind.Smudge ? Math.Max(brush.Spacing, 0.12f) : null, hasPressure);
        _raster?.Dispose();
        var wet = tool == ToolKind.Brush && brush.Wet.IsActive;
        _raster = new StrokeRasterizer(tips, _grains, brush, _color, size, LastSeed, allowGrain: tool != ToolKind.Smudge, tinted: wet);
        _wet = null;
        _dirty = Rect.Empty;
        _tailRect = Rect.Empty;
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
        if (brush.HasOpacityCap && tool != ToolKind.Smudge)
        {
            if (_capBuffer is null || _capBuffer.SizeInPixels != _buffer.SizeInPixels)
            {
                _capBuffer?.Dispose();
                _capBuffer = new CanvasRenderTarget(doc.Device, doc.Width, doc.Height, 96);
            }
            using (var cs = _capBuffer.CreateDrawingSession()) cs.Clear(KnownColors.Transparent);
            if (_cap is null || _cap.Width != doc.Width || _cap.Height != doc.Height) _cap = new OpacityCap(doc.Width, doc.Height);
            else _cap.Clear();
        }
        var snapshot = ops.Snapshot(layer);
        if (wet)
        {
            // el depósito mezcla con la capa tal como estaba al empezar el trazo
            var sampler = new LayerSampler(snapshot, doc.Width, doc.Height);
            _wet = new WetMixer(brush.Wet, new Vector3(_color.R, _color.G, _color.B) / 255f, size, sampler.Sample);
        }
        Add(p);
        return true;
    }

    public void Add(InputPoint p)
    {
        if (_layer is null || _builder is null) return;
        if (_assist is not null)
        {
            var c = _assist.Constrain(new Vector2(p.X, p.Y));
            p = p with { X = c.X, Y = c.Y };
        }
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
        if (_wet is not null) for (var i = 0; i < _tailDabs.Count; i++) _tailDabs[i] = _wet.Peek(_tailDabs[i]);
        var expanded = Expand(_tailDabs);
        _raster!.Draw(ds, expanded);
        DrawCap(expanded);
        _tailRect = Rect.Empty;
        foreach (var d in expanded)
        {
            var hs = d.Size * 0.75f + 2;
            var r = new Rect(d.X - hs, d.Y - hs, hs * 2, hs * 2);
            if (_tailRect.IsEmpty) _tailRect = r;
            else _tailRect.Union(r);
        }
    }

    private Rect _tailRect = Rect.Empty;

    private bool CapActive => _capBuffer is not null && _brush?.HasOpacityCap == true && _tool != ToolKind.Smudge;

    private void DrawCap(List<Dab> dabs)
    {
        if (!CapActive || _cap is null) return;
        var mask = tips.Mask(_brush!.Tip);
        int x0 = int.MaxValue, y0 = int.MaxValue, x1 = 0, y1 = 0;
        foreach (var d in dabs)
        {
            var r = _cap.Stamp(d, mask, TipGenerator.DefaultSize);
            if (r.IsEmpty) continue;
            x0 = Math.Min(x0, r.X);
            y0 = Math.Min(y0, r.Y);
            x1 = Math.Max(x1, r.Right);
            y1 = Math.Max(y1, r.Bottom);
        }
        if (x1 <= x0 || y1 <= y0) return;
        var rect = new PixelRect(x0, y0, x1 - x0, y1 - y0);
        _capBuffer!.SetPixelBytes(_cap.ToBgra(rect), rect.X, rect.Y, rect.Width, rect.Height);
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
        if (_wet is not null) for (var i = 0; i < dabs.Count; i++) dabs[i] = _wet.Process(dabs[i]);
        var expanded = Expand(dabs);
        using (var ds = _buffer!.CreateDrawingSession()) _raster!.Draw(ds, expanded);
        DrawCap(expanded);
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
        // PF-01: el grano y el tope solo se calculan donde hay trazo (fuera de ahí el búfer es transparente)
        var area = _dirty;
        if (TailActive && !_tailRect.IsEmpty)
        {
            if (area.IsEmpty) area = _tailRect;
            else area.Union(_tailRect);
        }
        if (!area.IsEmpty)
        {
            var crop = new CropEffect { Source = stroke, SourceRectangle = area };
            owned.Add(crop);
            stroke = crop;
        }
        var buf = new OpacityEffect { Source = _raster!.Finish(stroke, owned, CapActive ? _capBuffer : null), Opacity = _opacity };
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
                    ds.DrawImage(_raster!.Finish(_buffer!, owned, CapActive ? _capBuffer : null), new Vector2((float)r.X, (float)r.Y), r, _opacity, CanvasImageInterpolation.NearestNeighbor, Composite);
                owned.ForEach(o => o.Dispose());
            }
        }
        if (!_dirty.IsEmpty)
            ops.CommitPixels(layer, _dirty, _tool switch { ToolKind.Eraser => T("Borrar"), ToolKind.Smudge => T("Difuminar"), _ => T("Pintar") });
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
        if (CapActive)
        {
            using (var cs = _capBuffer!.CreateDrawingSession()) cs.Clear(KnownColors.Transparent);
            _cap?.Clear();
        }
        _dirty = Rect.Empty;
        _tailRect = Rect.Empty;
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
        ops.CommitPixels(layer, new Rect(b.X, b.Y, b.Width, b.Height), T("Relleno"));
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
        if (brush.Wet.IsActive)
        {
            var mixer = new WetMixer(brush.Wet, new Vector3(color.R, color.G, color.B) / 255f, size, _ => default);
            dabs = dabs.Select(mixer.Process).ToList();
        }
        if (brush.Flow < 0.3f) dabs = dabs.Select(d => d with { Alpha = Math.Min(1, d.Alpha * 2) }).ToList();

        using var raster = new StrokeRasterizer(tips, grains, brush, color, size, seed: 3, tinted: brush.Wet.IsActive);
        using var stroke = new CanvasRenderTarget(ds, w, h);
        using (var sds = stroke.CreateDrawingSession())
        {
            sds.Clear(KnownColors.Transparent);
            raster.Draw(sds, dabs);
        }
        using var cap = brush.HasOpacityCap ? new CanvasRenderTarget(ds, w, h) : null;
        if (cap is not null)
        {
            var px = cap.SizeInPixels;
            var cpu = new OpacityCap((int)px.Width, (int)px.Height);
            var mask = tips.Mask(brush.Tip);
            var k = px.Width / w; // la vista previa puede tener otra escala de píxeles
            foreach (var d in dabs) cpu.Stamp(d with { X = d.X * k, Y = d.Y * k, Size = d.Size * k }, mask, TipGenerator.DefaultSize);
            cap.SetPixelBytes(cpu.ToBgra(new PixelRect(0, 0, cpu.Width, cpu.Height)));
        }
        var owned = new List<IDisposable>();
        ds.Transform = Matrix3x2.Identity;
        ds.DrawImage(raster.Finish(stroke, owned, cap));
        owned.ForEach(o => o.Dispose());
    }
}
