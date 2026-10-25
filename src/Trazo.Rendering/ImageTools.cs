using System.Numerics;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Effects;
using Trazo.Core.Imaging;
using Windows.Foundation;

namespace Trazo.Rendering;

public sealed record AdjustParam(string Key, string Label, double Min, double Max, double Default, string Unit = "");

/// <summary>Ajuste de imagen: parámetros y grafo de efectos de Direct2D.</summary>
public sealed record Adjustment(string Id, string Name, IReadOnlyList<AdjustParam> Params,
    Func<ICanvasImage, IReadOnlyDictionary<string, double>, List<IDisposable>, ICanvasImage> Build)
{
    private static T Own<T>(List<IDisposable> o, T e) where T : IDisposable
    {
        o.Add(e);
        return e;
    }

    private static Matrix5x4 Saturation(float s)
    {
        // matriz de saturación con pesos de luminancia Rec. 709
        float r = 0.2126f * (1 - s), g = 0.7152f * (1 - s), b = 0.0722f * (1 - s);
        return new Matrix5x4
        {
            M11 = r + s, M12 = r, M13 = r,
            M21 = g, M22 = g + s, M23 = g,
            M31 = b, M32 = b, M33 = b + s,
            M44 = 1,
        };
    }

    private static Matrix5x4 Brightness(float k) => new() { M11 = k, M22 = k, M33 = k, M44 = 1 };

    private static ICanvasImage Mix(ICanvasImage original, ICanvasImage effect, double amount, List<IDisposable> o) =>
        amount >= 1 ? effect : Own(o, new CrossFadeEffect { Source1 = effect, Source2 = original, CrossFade = (float)amount });

    public static readonly IReadOnlyList<Adjustment> All =
    [
        new("hsb", "Tono, saturación y brillo",
            [new("h", "Tono", -180, 180, 0, "°"), new("s", "Saturación", 0, 200, 100, " %"), new("b", "Brillo", 0, 200, 100, " %")],
            (src, v, o) =>
            {
                ICanvasImage img = Own(o, new HueRotationEffect { Source = src, Angle = (float)(v["h"] * Math.PI / 180) });
                img = Own(o, new ColorMatrixEffect { Source = img, ColorMatrix = Saturation((float)(v["s"] / 100)), ClampOutput = true });
                return Own(o, new ColorMatrixEffect { Source = img, ColorMatrix = Brightness((float)(v["b"] / 100)), ClampOutput = true });
            }),
        new("contrast", "Contraste", [new("c", "Contraste", -100, 100, 0, " %")],
            (src, v, o) => Own(o, new ContrastEffect { Source = src, Contrast = (float)(v["c"] / 100) })),
        new("exposure", "Exposición", [new("e", "Exposición", -2, 2, 0, " EV")],
            (src, v, o) => Own(o, new ExposureEffect { Source = src, Exposure = (float)v["e"] })),
        new("temperature", "Temperatura y matiz", [new("t", "Temperatura", -100, 100, 0), new("m", "Matiz", -100, 100, 0)],
            (src, v, o) => Own(o, new TemperatureAndTintEffect { Source = src, Temperature = (float)(v["t"] / 100), Tint = (float)(v["m"] / 100) })),
        new("blur", "Desenfoque gaussiano", [new("r", "Radio", 0, 100, 0, " px")],
            (src, v, o) => Own(o, new GaussianBlurEffect { Source = src, BlurAmount = (float)v["r"] / 2, BorderMode = EffectBorderMode.Hard, Optimization = EffectOptimization.Quality })),
        new("sharpen", "Enfocar", [new("a", "Intensidad", 0, 100, 0, " %")],
            (src, v, o) => Own(o, new SharpenEffect { Source = src, Amount = (float)(v["a"] / 10) })),
        new("noise", "Ruido", [new("a", "Cantidad", 0, 100, 0, " %")],
            (src, v, o) =>
            {
                var turb = Own(o, new TurbulenceEffect { Frequency = new Vector2(0.9f), Octaves = 1, Seed = 7, Size = new Vector2(16384) });
                var gray = Own(o, new GrayscaleEffect { Source = turb });
                var weak = Own(o, new OpacityEffect { Source = gray, Opacity = (float)(v["a"] / 100) });
                var blended = Own(o, new BlendEffect { Background = src, Foreground = weak, Mode = BlendEffectMode.Overlay });
                return Own(o, new CompositeEffect { Mode = CanvasComposite.DestinationIn, Sources = { blended, src } });
            }),
        new("invert", "Invertir colores", [new("a", "Intensidad", 0, 100, 100, " %")],
            (src, v, o) => Mix(src, Own(o, new InvertEffect { Source = src }), v["a"] / 100, o)),
        new("gray", "Escala de grises", [new("a", "Intensidad", 0, 100, 100, " %")],
            (src, v, o) => Mix(src, Own(o, new GrayscaleEffect { Source = src }), v["a"] / 100, o)),
        new("sepia", "Sepia", [new("a", "Intensidad", 0, 100, 100, " %")],
            (src, v, o) => Own(o, new SepiaEffect { Source = src, Intensity = (float)(v["a"] / 100) })),
        new("opacity", "Opacidad de píxeles", [new("a", "Opacidad", 0, 100, 100, " %")],
            (src, v, o) => Own(o, new OpacityEffect { Source = src, Opacity = (float)(v["a"] / 100) })),
    ];
}

/// <summary>Sesión de ajuste sobre la capa activa con vista previa en vivo.</summary>
public sealed class AdjustmentSession(PaintDocument doc, Layer layer, Adjustment adjustment)
{
    public Layer Layer { get; } = layer;
    public Adjustment Adjustment { get; } = adjustment;
    public Dictionary<string, double> Values { get; } = adjustment.Params.ToDictionary(p => p.Key, p => p.Default);

    /// <summary>Textura sobre la que actúa el ajuste (la capa o su máscara) y su clave de sustitución.</summary>
    public CanvasRenderTarget Target { get; } = layer.Target;
    public object TargetKey { get; } = layer.TargetKey;

    /// <summary>Vista previa: el ajuste solo se ve dentro de la selección, igual que se confirmará.</summary>
    public ICanvasImage Preview(List<IDisposable> owned) => doc.SelectionBlend(Target, Adjustment.Build(Target, Values, owned), owned);

    public void Apply(LayerOperations ops)
    {
        var owned = new List<IDisposable>();
        ops.ReplaceWith(Layer, Adjustment.Build(Target, Values, owned), Adjustment.Name);
        owned.ForEach(o => o.Dispose());
    }
}

/// <summary>Transformar la capa activa: mover, escalar, girar y voltear.</summary>
public sealed class TransformSession : IDisposable
{
    private readonly PaintDocument _doc;
    private Matrix3x2 _anchorMatrix;
    private Vector2[] _anchorPoints = [];
    private uint[] _anchorIds = [];

    private TransformSession(PaintDocument doc, Layer layer, CanvasRenderTarget source, CanvasRenderTarget? background, PixelRect bounds)
    {
        _doc = doc;
        Layer = layer;
        Source = source;
        Background = background;
        Bounds = bounds;
        TargetKey = layer.TargetKey;
    }

    /// <summary>Lo que queda fijo: el resto de la capa si hay selección, o blanco si se transforma una máscara.</summary>
    public CanvasRenderTarget? Background { get; }
    public object TargetKey { get; }

    public Layer Layer { get; }
    public CanvasRenderTarget Source { get; }
    public PixelRect Bounds { get; }
    public Matrix3x2 Matrix { get; set; } = Matrix3x2.Identity;
    public bool Smooth { get; set; } = true;

    /// <summary>
    /// Empieza a transformar la textura de trabajo de la capa. Con selección (SE-01) se levanta solo el
    /// contenido seleccionado y el resto queda en su sitio.
    /// </summary>
    public static TransformSession? Start(PaintDocument doc, Layer layer)
    {
        var target = layer.Target;
        var source = new CanvasRenderTarget(doc.Device, doc.Width, doc.Height, 96);
        CanvasRenderTarget? background = null;
        if (doc.SelectionBitmap is { } sel)
        {
            background = new CanvasRenderTarget(doc.Device, doc.Width, doc.Height, 96);
            using (var lifted = new CompositeEffect { Mode = CanvasComposite.DestinationIn, Sources = { target, sel } })
            using (var ds = source.CreateDrawingSession()) { ds.Clear(KnownColors.Transparent); ds.DrawImage(lifted); }
            using (var rest = new CompositeEffect { Mode = CanvasComposite.DestinationOut, Sources = { target, sel } })
            using (var ds = background.CreateDrawingSession()) { ds.Clear(KnownColors.Transparent); ds.DrawImage(rest); }
        }
        else
        {
            source.CopyPixelsFromBitmap(target);
            if (layer.EditingMask) background = Layer.CreateMask(doc.Device, doc.Width, doc.Height, KnownColors.White);
        }
        var bounds = FloodFill.ContentBounds(source.GetPixelBytes(), doc.Width, doc.Height);
        if (bounds.IsEmpty)
        {
            source.Dispose();
            background?.Dispose();
            return null;
        }
        return new TransformSession(doc, layer, source, background, bounds);
    }

    public ICanvasImage Preview(List<IDisposable> owned)
    {
        var fx = new Transform2DEffect
        {
            Source = Source,
            TransformMatrix = Matrix,
            InterpolationMode = Smooth ? CanvasImageInterpolation.HighQualityCubic : CanvasImageInterpolation.NearestNeighbor,
            BorderMode = EffectBorderMode.Hard,
        };
        owned.Add(fx);
        var crop = new CropEffect { Source = fx, SourceRectangle = _doc.Bounds };
        owned.Add(crop);
        if (Background is null) return crop;
        var over = new CompositeEffect { Sources = { Background, crop } };
        owned.Add(over);
        return over;
    }

    public Vector2[] Corners()
    {
        var b = Bounds;
        Vector2[] c = [new(b.X, b.Y), new(b.Right, b.Y), new(b.Right, b.Bottom), new(b.X, b.Bottom)];
        return c.Select(p => Vector2.Transform(p, Matrix)).ToArray();
    }

    public Vector2 Center
    {
        get
        {
            var c = Corners();
            return (c[0] + c[2]) / 2;
        }
    }

    private void Around(Vector2 pivot, Matrix3x2 m) =>
        Matrix = Matrix * Matrix3x2.CreateTranslation(-pivot) * m * Matrix3x2.CreateTranslation(pivot);

    public void Flip(bool horizontal) => Around(Center, Matrix3x2.CreateScale(horizontal ? -1 : 1, horizontal ? 1 : -1));
    public void Rotate(float degrees) => Around(Center, Matrix3x2.CreateRotation(degrees * MathF.PI / 180));
    public void ScaleAt(Vector2 pivot, float k) => Around(pivot, Matrix3x2.CreateScale(k));
    public void RotateAt(Vector2 pivot, float degrees) => Around(pivot, Matrix3x2.CreateRotation(degrees * MathF.PI / 180));
    public void Reset() => Matrix = Matrix3x2.Identity;

    public void FitToCanvas()
    {
        var b = Bounds;
        var k = Math.Min((float)_doc.Width / b.Width, (float)_doc.Height / b.Height);
        Matrix = Matrix3x2.CreateTranslation(-b.X, -b.Y) * Matrix3x2.CreateScale(k)
                 * Matrix3x2.CreateTranslation((_doc.Width - b.Width * k) / 2, (_doc.Height - b.Height * k) / 2);
    }

    // ---- Gestos: los puntos llegan en coordenadas del documento ----

    public void Anchor(IReadOnlyList<(uint Id, Vector2 DocPoint)> pointers)
    {
        _anchorMatrix = Matrix;
        _anchorIds = pointers.Take(2).Select(p => p.Id).ToArray();
        _anchorPoints = pointers.Take(2).Select(p => p.DocPoint).ToArray();
    }

    public void Move(IReadOnlyDictionary<uint, Vector2> current)
    {
        if (_anchorIds.Length == 0) return;
        if (_anchorIds.Length >= 2 && current.TryGetValue(_anchorIds[0], out var a1) && current.TryGetValue(_anchorIds[1], out var b1))
        {
            Vector2 a0 = _anchorPoints[0], b0 = _anchorPoints[1];
            var c0 = (a0 + b0) / 2;
            var c1 = (a1 + b1) / 2;
            var k = Vector2.Distance(a1, b1) / Math.Max(1e-3f, Vector2.Distance(a0, b0));
            var da = MathF.Atan2(b1.Y - a1.Y, b1.X - a1.X) - MathF.Atan2(b0.Y - a0.Y, b0.X - a0.X);
            Matrix = _anchorMatrix * Matrix3x2.CreateTranslation(-c0) * Matrix3x2.CreateScale(k) * Matrix3x2.CreateRotation(da) * Matrix3x2.CreateTranslation(c1);
        }
        else if (current.TryGetValue(_anchorIds[0], out var p))
        {
            Matrix = _anchorMatrix * Matrix3x2.CreateTranslation(p - _anchorPoints[0]);
        }
    }

    public void Apply(LayerOperations ops)
    {
        var owned = new List<IDisposable>();
        // lo movido puede quedar fuera de la selección original: no se recorta
        ops.ReplaceWith(Layer, Preview(owned), "Transformar", clipToSelection: false);
        owned.ForEach(o => o.Dispose());
        // la selección se mueve con su contenido
        if (_doc.SelectionBitmap is { } sel && Matrix != Matrix3x2.Identity)
        {
            using var moved = new CanvasRenderTarget(_doc.Device, _doc.Width, _doc.Height, 96);
            using (var fx = new Transform2DEffect { Source = sel, TransformMatrix = Matrix, InterpolationMode = CanvasImageInterpolation.Linear })
            using (var ds = moved.CreateDrawingSession()) { ds.Clear(KnownColors.Transparent); ds.DrawImage(fx); }
            var px = moved.GetPixelBytes();
            var mask = new Trazo.Core.Selection.SelectionMask(_doc.Width, _doc.Height);
            for (var i = 0; i < mask.Data.Length; i++) mask.Data[i] = px[i * 4 + 3];
            ops.SetSelection(mask, "Mover selección");
        }
    }

    public void Dispose()
    {
        Source.Dispose();
        Background?.Dispose();
    }

    public static Rect ToRect(PixelRect r) => new(r.X, r.Y, r.Width, r.Height);
}
