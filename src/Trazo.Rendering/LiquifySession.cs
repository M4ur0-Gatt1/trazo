using System.Numerics;
using Microsoft.Graphics.Canvas;
using Trazo.Core.Imaging;

namespace Trazo.Rendering;

/// <summary>
/// Sesión de Licuar (LQ-01) sobre la textura activa de la capa (la capa o su máscara). El campo y el
/// remuestreo se calculan en la CPU y solo se sube a la GPU la zona que cambia.
/// </summary>
public sealed class LiquifySession : IDisposable
{
    private readonly PaintDocument _doc;
    private readonly byte[] _source;
    private readonly byte[] _result;
    private Vector2 _last;
    private bool _stroking;
    private float _amount = 1;

    private LiquifySession(PaintDocument doc, Layer layer)
    {
        _doc = doc;
        Layer = layer;
        Target = layer.Target;
        TargetKey = layer.TargetKey;
        _source = Target.GetPixelBytes();
        _result = (byte[])_source.Clone();
        Field = new LiquifyField(doc.Width, doc.Height);
        Result = new CanvasRenderTarget(doc.Device, doc.Width, doc.Height, 96);
        Result.CopyPixelsFromBitmap(Target);
    }

    public static LiquifySession Start(PaintDocument doc, Layer layer) => new(doc, layer);

    public Layer Layer { get; }
    public CanvasRenderTarget Target { get; }
    public object TargetKey { get; }
    public LiquifyField Field { get; }
    /// <summary>Resultado en la GPU, al día con el campo.</summary>
    public CanvasRenderTarget Result { get; }

    public LiquifyMode Mode { get; set; } = LiquifyMode.Push;
    /// <summary>Diámetro del pincel en px del documento.</summary>
    public float Size { get; set; } = 200;
    /// <summary>Intensidad de 0 a 1 (se multiplica por la presión).</summary>
    public float Strength { get; set; } = 0.6f;

    /// <summary>Ajustar: escala todo el campo (0 = original, 1 = todo lo hecho).</summary>
    public float Amount
    {
        get => _amount;
        set
        {
            _amount = Math.Clamp(value, 0, 1);
            Redraw(Field.Full);
        }
    }

    public bool HasChanges => !Field.IsZero && _amount > 0;

    public void BeginStroke(Vector2 p, float pressure)
    {
        _last = p;
        _stroking = true;
        // los modos que no empujan actúan ya en el primer toque
        if (Mode != LiquifyMode.Push) DabAt(p, pressure);
    }

    public void ContinueStroke(Vector2 p, float pressure)
    {
        if (!_stroking) return;
        var radius = Size / 2;
        var dirty = default(PixelRect);
        var previous = _last;
        foreach (var c in Liquify.DabsAlong(ref _last, p, radius))
        {
            dirty = Union(dirty, Field.Dab(Mode, c, previous, radius, Strength * pressure));
            previous = c;
        }
        Redraw(dirty);
    }

    /// <summary>Un toque en el sitio (al mantener el pincel quieto en Girar, Pellizcar, Expandir o Reconstruir).</summary>
    public void DabAt(Vector2 p, float pressure)
    {
        if (Mode == LiquifyMode.Push) return;
        Redraw(Field.Dab(Mode, p, p, Size / 2, Strength * pressure));
    }

    public void EndStroke() => _stroking = false;

    public void Reset()
    {
        Field.Clear();
        _amount = 1;
        Redraw(Field.Full);
    }

    private static PixelRect Union(PixelRect a, PixelRect b)
    {
        if (a.IsEmpty) return b;
        if (b.IsEmpty) return a;
        var x0 = Math.Min(a.X, b.X);
        var y0 = Math.Min(a.Y, b.Y);
        return new PixelRect(x0, y0, Math.Max(a.Right, b.Right) - x0, Math.Max(a.Bottom, b.Bottom) - y0);
    }

    private void Redraw(PixelRect rect)
    {
        if (rect.IsEmpty) return;
        Liquify.Render(_source, _result, Field, rect, _amount);
        var w = _doc.Width;
        var rows = new byte[rect.Width * rect.Height * 4];
        for (var y = 0; y < rect.Height; y++)
            Buffer.BlockCopy(_result, ((rect.Y + y) * w + rect.X) * 4, rows, y * rect.Width * 4, rect.Width * 4);
        Result.SetPixelBytes(rows, rect.X, rect.Y, rect.Width, rect.Height);
    }

    /// <summary>Vista previa: el resultado solo se ve dentro de la selección, igual que se confirmará.</summary>
    public ICanvasImage Preview(List<IDisposable> owned) => _doc.SelectionBlend(Target, Result, owned);

    public void Apply(LayerOperations ops)
    {
        if (!HasChanges) return;
        ops.ReplaceWith(Layer, Result, "Licuar");
    }

    public void Dispose() => Result.Dispose();
}
