using System.Numerics;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Effects;
using Trazo.Core.Brushes;
using Trazo.Core.Strokes;
using Windows.Foundation;
using Windows.Graphics.DirectX;
using Windows.UI;

namespace Trazo.Rendering;

/// <summary>Texturas de grano en la GPU, generadas una vez por combinación de tipo, brillo y contraste.</summary>
public sealed class GrainCache(CanvasDevice device)
{
    private readonly Dictionary<(GrainKind, float, float), CanvasBitmap> _cache = [];

    public CanvasBitmap Get(GrainSettings g)
    {
        var key = (g.Kind, MathF.Round(g.Brightness, 2), MathF.Round(g.Contrast, 2));
        if (_cache.TryGetValue(key, out var bmp)) return bmp;
        var lum = GrainGenerator.Generate(g.Kind, key.Item2, key.Item3);
        bmp = CanvasBitmap.CreateFromBytes(device, GrainGenerator.ToBgra(lum), GrainGenerator.Size, GrainGenerator.Size,
            DirectXPixelFormat.B8G8R8A8UIntNormalized, 96, CanvasAlphaMode.Premultiplied);
        _cache[key] = bmp;
        return bmp;
    }
}

/// <summary>
/// Dibuja los sellos de un trazo y aplica el grano (BR-03):
/// <list type="bullet">
/// <item><b>Moving</b>: cada sello = punta × grano muestreado en un origen que mezcla la posición del sello
/// (Movement 0: el grano viaja con el sello) y el lienzo (Movement 1: anclado al lienzo).</item>
/// <item><b>Texturized</b>: el trazo acumulado se recorta con el grano anclado al lienzo, de modo que la
/// textura no se satura aunque los sellos se superpongan.</item>
/// </list>
/// Usa un único grafo de efectos por trazo cuyas transformaciones se actualizan en cada sello.
/// </summary>
public sealed class StrokeRasterizer : IDisposable
{
    private readonly CanvasBitmap _tip;
    private readonly GrainSettings _grain;
    private readonly float _size;
    private readonly Vector2 _jitter;
    private readonly List<IDisposable> _graph = [];
    private readonly Transform2DEffect? _tipT, _grainT;
    private readonly CompositeEffect? _dab;
    private readonly CanvasBitmap? _grainBitmap;
    private readonly CanvasBitmap _whiteTip;
    // mezcla húmeda (BR-04): la punta blanca se tiñe con el color de cada sello
    private readonly ColorMatrixEffect? _tint;
    private readonly Vector3 _color;

    /// <param name="tinted">Cada sello puede llevar su propio color (mezcla húmeda).</param>
    public StrokeRasterizer(TipCache tips, GrainCache grains, BrushPreset brush, Color color, float size, int seed, bool allowGrain = true, bool tinted = false)
    {
        _tip = tips.Get(brush.Tip, color);
        _whiteTip = tips.Get(brush.Tip, KnownColors.White);
        _color = new Vector3(color.R, color.G, color.B) / 255f;
        if (tinted) _tint = Own(new ColorMatrixEffect { Source = _whiteTip, ClampOutput = true });
        _grain = allowGrain ? brush.Grain : GrainSettings.Off;
        _size = size;
        if (!_grain.IsActive) return;
        _grainBitmap = grains.Get(_grain);
        var period = _grain.Period(size);
        _jitter = _grain.OffsetJitter ? new Vector2((float)new Random(seed).NextDouble(), (float)new Random(seed + 7919).NextDouble()) * period : Vector2.Zero;
        if (_grain.Mode != GrainMode.Moving) return;

        _tipT = Own(new Transform2DEffect { Source = _tint is null ? _tip : _tint, InterpolationMode = CanvasImageInterpolation.Linear });
        _grainT = Own(new Transform2DEffect { Source = Own(Wrap(_grainBitmap)), InterpolationMode = CanvasImageInterpolation.Linear });
        _dab = Own(new CompositeEffect { Mode = CanvasComposite.DestinationIn, Sources = { _tipT, Own(DepthToAlpha(_grainT, _grain.Depth)) } });
    }

    private T Own<T>(T e) where T : IDisposable
    {
        _graph.Add(e);
        return e;
    }

    private static BorderEffect Wrap(ICanvasImage src) =>
        new() { Source = src, ExtendX = CanvasEdgeBehavior.Wrap, ExtendY = CanvasEdgeBehavior.Wrap };

    /// <summary>alfa' = Depth · grano + (1 − Depth); color blanco (solo importa el alfa).</summary>
    private static ColorMatrixEffect DepthToAlpha(ICanvasImage grain, float depth) => new()
    {
        Source = grain,
        ClampOutput = true,
        ColorMatrix = new Matrix5x4 { M44 = depth, M51 = 1, M52 = 1, M53 = 1, M54 = 1 - depth },
    };

    private void Tint(in Dab d)
    {
        var c = d.Color ?? _color;
        _tint!.ColorMatrix = new Matrix5x4 { M11 = c.X, M22 = c.Y, M33 = c.Z, M44 = 1 };
    }

    /// <summary>Dibuja los sellos en la sesión (que debe tener la transformación identidad).</summary>
    public void Draw(CanvasDrawingSession ds, IEnumerable<Dab> dabs)
    {
        if (_dab is null)
        {
            foreach (var d in dabs)
            {
                if (_tint is null)
                {
                    TipCache.DrawDab(ds, _tip, d);
                    continue;
                }
                Tint(d);
                ds.Transform = Matrix3x2.CreateRotation(d.Rotation) * Matrix3x2.CreateTranslation(d.X, d.Y);
                var half = d.Size / 2;
                ds.DrawImage(_tint, new Rect(-half, -half, d.Size, d.Size), TipCache.TipRect, Math.Clamp(d.Alpha, 0, 1), CanvasImageInterpolation.Linear);
            }
            ds.Transform = Matrix3x2.Identity;
            return;
        }
        var k = 1 / (float)TipGenerator.DefaultSize;
        var tipCenter = Matrix3x2.CreateTranslation(-TipGenerator.DefaultSize / 2f, -TipGenerator.DefaultSize / 2f);
        var grainScale = Matrix3x2.CreateScale(_grain.Period(_size) / GrainGenerator.Size);
        ds.Transform = Matrix3x2.Identity;
        foreach (var d in dabs)
        {
            _tipT!.TransformMatrix = tipCenter * Matrix3x2.CreateScale(d.Size * k) * Matrix3x2.CreateRotation(d.Rotation) * Matrix3x2.CreateTranslation(d.X, d.Y);
            // Movement 0 → el origen del grano es el sello (viaja con él); 1 → el origen del lienzo (fijo)
            var origin = new Vector2(d.X, d.Y) * (1 - Math.Clamp(_grain.Movement, 0, 1)) + _jitter;
            _grainT!.TransformMatrix = grainScale * Matrix3x2.CreateRotation(_grain.Rotation * d.TravelAngle) * Matrix3x2.CreateTranslation(origin);
            if (_tint is not null) Tint(d);
            var h = d.Size * 0.75f + 1;
            ds.DrawImage(_dab, new Vector2(d.X - h, d.Y - h), new Rect(d.X - h, d.Y - h, h * 2, h * 2), Math.Clamp(d.Alpha, 0, 1));
        }
    }

    /// <summary>Aplica el grano texturizado y el tope de opacidad (si lo hay) al trazo acumulado.</summary>
    public ICanvasImage Finish(ICanvasImage stroke, List<IDisposable> owned, ICanvasImage? cap = null)
    {
        var grained = Grained(stroke, owned);
        // el tope (BR-06) llega como una imagen cuyo alfa es el tope de opacidad
        return cap is null ? grained : Track(owned, new CompositeEffect { Mode = CanvasComposite.DestinationIn, Sources = { grained, cap } });
    }

    private ICanvasImage Grained(ICanvasImage stroke, List<IDisposable> owned)
    {
        if (_grain.Mode != GrainMode.Texturized || _grainBitmap is null) return stroke;
        var wrap = Track(owned, Wrap(_grainBitmap));
        var scaled = Track(owned, new Transform2DEffect
        {
            Source = wrap,
            TransformMatrix = Matrix3x2.CreateScale(_grain.Period(_size) / GrainGenerator.Size) * Matrix3x2.CreateTranslation(_jitter),
            InterpolationMode = CanvasImageInterpolation.Linear,
        });
        var alpha = Track(owned, DepthToAlpha(scaled, _grain.Depth));
        return Track(owned, new CompositeEffect { Mode = CanvasComposite.DestinationIn, Sources = { stroke, alpha } });
    }

    private static T Track<T>(List<IDisposable> owned, T e) where T : IDisposable
    {
        owned.Add(e);
        return e;
    }

    public void Dispose()
    {
        foreach (var e in _graph) e.Dispose();
        _graph.Clear();
    }
}
