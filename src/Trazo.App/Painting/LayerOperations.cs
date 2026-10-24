using Microsoft.Graphics.Canvas;
using Microsoft.UI;
using Trazo.Core.Documents;
using Trazo.Core.History;
using Windows.Foundation;
using Windows.UI;

namespace Trazo.App.Painting;

/// <summary>Cambio de píxeles en un rectángulo de una capa, guardado como dos parches en la GPU.</summary>
public sealed class PixelPatchAction : IUndoableAction, IDisposable
{
    private readonly Layer _layer;
    private readonly CanvasRenderTarget _before, _after;
    private readonly Rect _rect;

    private PixelPatchAction(string label, Layer layer, Rect rect, CanvasRenderTarget before, CanvasRenderTarget after)
    {
        Label = label;
        _layer = layer;
        _rect = rect;
        _before = before;
        _after = after;
        Bytes = (long)(rect.Width * rect.Height * 8);
    }

    public string Label { get; }
    public long Bytes { get; }

    /// <summary>Crea la acción a partir de una copia previa de la capa completa.</summary>
    public static PixelPatchAction? Create(string label, Layer layer, CanvasBitmap snapshot, Rect dirty)
    {
        var size = layer.Surface.SizeInPixels;
        var r = Clip(dirty, size.Width, size.Height);
        if (r.Width <= 0 || r.Height <= 0) return null;
        return new PixelPatchAction(label, layer, r, Copy(snapshot, r), Copy(layer.Surface, r));
    }

    public static Rect Clip(Rect r, double w, double h)
    {
        var x0 = Math.Max(0, Math.Floor(r.X));
        var y0 = Math.Max(0, Math.Floor(r.Y));
        var x1 = Math.Min(w, Math.Ceiling(r.X + r.Width));
        var y1 = Math.Min(h, Math.Ceiling(r.Y + r.Height));
        return x1 <= x0 || y1 <= y0 ? Rect.Empty : new Rect(x0, y0, x1 - x0, y1 - y0);
    }

    private static CanvasRenderTarget Copy(CanvasBitmap src, Rect r)
    {
        var rt = new CanvasRenderTarget(src.Device, (float)r.Width, (float)r.Height, 96);
        rt.CopyPixelsFromBitmap(src, 0, 0, (int)r.X, (int)r.Y, (int)r.Width, (int)r.Height);
        return rt;
    }

    private void Put(CanvasBitmap patch)
    {
        _layer.Surface.CopyPixelsFromBitmap(patch, (int)_rect.X, (int)_rect.Y);
        _layer.Touch();
    }

    public void Undo() => Put(_before);
    public void Redo() => Put(_after);

    public void Dispose()
    {
        _before.Dispose();
        _after.Dispose();
    }
}

/// <summary>Cambio de una propiedad de capa; los cambios seguidos de la misma propiedad se fusionan.</summary>
public sealed class LayerPropertyAction<T>(string label, Layer layer, string key, T oldValue, T newValue, Action<Layer, T> setter) : IMergeableAction
{
    private T _new = newValue;
    private readonly DateTime _time = DateTime.UtcNow;

    public string Label { get; } = label;
    public long Bytes => 0;
    public void Undo() => setter(layer, oldValue);
    public void Redo() => setter(layer, _new);

    public bool TryMerge(IUndoableAction next)
    {
        if (next is not LayerPropertyAction<T> n || n.LayerRef != layer || n.Key != key || (n._time - _time).TotalMilliseconds > 1200) return false;
        _new = n._new;
        return true;
    }

    private Layer LayerRef => layer;
    private string Key => key;
}

/// <summary>Operaciones de capa con deshacer.</summary>
public sealed class LayerOperations(PaintDocument doc)
{
    private CanvasRenderTarget? _snapshot;
    private long LayerBytes => (long)doc.Width * doc.Height * 4;

    public event Action<string>? Message;

    public CanvasRenderTarget? CurrentSnapshot => _snapshot;

    // ---------- Instantáneas para operaciones de píxeles ----------

    /// <summary>Copia la capa antes de modificarla. Reutiliza la textura entre operaciones.</summary>
    public CanvasRenderTarget Snapshot(Layer layer)
    {
        if (_snapshot is null || _snapshot.SizeInPixels.Width != doc.Width || _snapshot.SizeInPixels.Height != doc.Height)
        {
            _snapshot?.Dispose();
            _snapshot = new CanvasRenderTarget(doc.Device, doc.Width, doc.Height, 96);
        }
        _snapshot.CopyPixelsFromBitmap(layer.Surface);
        return _snapshot;
    }

    public void CommitPixels(Layer layer, Rect dirty, string label)
    {
        layer.Touch();
        if (_snapshot is null) return;
        var a = PixelPatchAction.Create(label, layer, _snapshot, dirty);
        if (a is not null) doc.History.Push(a);
        doc.IsDirty = true;
    }

    /// <summary>Aplica una operación sobre la capa completa con deshacer.</summary>
    public void PixelOp(Layer layer, string label, Action<CanvasDrawingSession> draw)
    {
        Snapshot(layer);
        using (var ds = layer.Surface.CreateDrawingSession()) draw(ds);
        CommitPixels(layer, doc.Bounds, label);
    }

    /// <summary>Sustituye la capa por la imagen dada (renderizada fuera de la capa para no leer y escribir a la vez).</summary>
    public void ReplaceWith(Layer layer, ICanvasImage image, string label)
    {
        using var temp = new CanvasRenderTarget(doc.Device, doc.Width, doc.Height, 96);
        using (var ds = temp.CreateDrawingSession())
        {
            ds.Clear(Colors.Transparent);
            ds.DrawImage(image);
        }
        Snapshot(layer);
        layer.Surface.CopyPixelsFromBitmap(temp);
        CommitPixels(layer, doc.Bounds, label);
    }

    // ---------- Estructura ----------

    public Layer? Add(Layer? layer = null, int? at = null)
    {
        if (doc.Layers.Count >= doc.MaxLayers)
        {
            Message?.Invoke($"Límite de {doc.MaxLayers} capas para este tamaño de lienzo");
            return null;
        }
        var l = layer ?? doc.CreateLayer();
        var idx = Math.Clamp(at ?? doc.ActiveIndex + 1, 0, doc.Layers.Count);
        var prev = doc.ActiveIndex;
        doc.History.Execute(new DelegateAction("Nueva capa",
            () => { doc.Layers.Remove(l); doc.ActiveIndex = prev; },
            () => { doc.Layers.Insert(idx, l); doc.ActiveIndex = idx; },
            LayerBytes));
        doc.IsDirty = true;
        return l;
    }

    public void Remove(int index)
    {
        if (doc.Layers.Count <= 1)
        {
            Clear(index);
            Message?.Invoke("Se vació la única capa");
            return;
        }
        var l = doc.Layers[index];
        var prev = doc.ActiveIndex;
        doc.History.Execute(new DelegateAction("Eliminar capa",
            () => { doc.Layers.Insert(index, l); doc.ActiveIndex = prev; },
            () => { doc.Layers.Remove(l); doc.ActiveIndex = Math.Max(0, index - 1); }));
        doc.IsDirty = true;
    }

    public void Duplicate(int index) => Add(doc.Layers[index].Duplicate(doc.Device), index + 1);

    public void Move(int from, int to)
    {
        if (from == to || to < 0 || to >= doc.Layers.Count) return;
        doc.History.Execute(new DelegateAction("Mover capa",
            () => { doc.Layers.Move(to, from); doc.ActiveIndex = from; },
            () => { doc.Layers.Move(from, to); doc.ActiveIndex = to; }));
        doc.IsDirty = true;
    }

    public void Set<T>(Layer layer, string key, T value, Func<Layer, T> getter, Action<Layer, T> setter, string label)
    {
        var old = getter(layer);
        if (EqualityComparer<T>.Default.Equals(old, value)) return;
        setter(layer, value);
        doc.History.Push(new LayerPropertyAction<T>(label, layer, key, old, value, setter));
        doc.IsDirty = true;
    }

    public void SetBackground(Color color, bool visible)
    {
        var (oc, ov) = (doc.Background, doc.BackgroundVisible);
        if (oc == color && ov == visible) return;
        doc.History.Execute(new DelegateAction("Fondo",
            () => { doc.Background = oc; doc.BackgroundVisible = ov; },
            () => { doc.Background = color; doc.BackgroundVisible = visible; }));
        doc.IsDirty = true;
    }

    public void Clear(int index) =>
        PixelOp(doc.Layers[index], "Borrar capa", ds => ds.Clear(Colors.Transparent));

    public void Fill(int index, Color color)
    {
        var layer = doc.Layers[index];
        if (!layer.AlphaLock)
        {
            PixelOp(layer, "Rellenar capa", ds => ds.Clear(color));
            return;
        }
        using var solid = new CanvasRenderTarget(doc.Device, doc.Width, doc.Height, 96);
        using (var ds = solid.CreateDrawingSession()) ds.Clear(color);
        PixelOp(layer, "Rellenar capa", ds =>
            ds.DrawImage(solid, System.Numerics.Vector2.Zero, doc.Bounds, 1, CanvasImageInterpolation.NearestNeighbor, CanvasComposite.SourceAtop));
    }

    public void Invert(int index)
    {
        var layer = doc.Layers[index];
        // Direct2D invierte los canales de color sin premultiplicar y conserva el alfa
        using var fx = new Microsoft.Graphics.Canvas.Effects.InvertEffect { Source = layer.Surface };
        ReplaceWith(layer, fx, "Invertir");
    }

    public void MergeDown(int index)
    {
        if (index <= 0) return;
        var up = doc.Layers[index];
        var down = doc.Layers[index - 1];
        var before = Clone(down.Surface);
        CanvasRenderTarget? after = null;
        var prev = doc.ActiveIndex;
        doc.History.Execute(new DisposingAction("Combinar abajo", LayerBytes * 2,
            () =>
            {
                down.Surface.CopyPixelsFromBitmap(before);
                down.Touch();
                doc.Layers.Insert(doc.Layers.IndexOf(down) + 1, up);
                doc.ActiveIndex = prev;
            },
            () =>
            {
                if (after is null)
                {
                    using var tmp = new CanvasRenderTarget(doc.Device, doc.Width, doc.Height, 96);
                    var owned = new List<IDisposable>();
                    using (var ds = tmp.CreateDrawingSession())
                    {
                        ds.Clear(Colors.Transparent);
                        ICanvasImage top = up.Opacity < 1
                            ? Own(owned, new Microsoft.Graphics.Canvas.Effects.OpacityEffect { Source = up.Surface, Opacity = up.Opacity })
                            : up.Surface;
                        ICanvasImage merged = !up.Visible ? down.Surface
                            : up.Clip ? Own(owned, new Microsoft.Graphics.Canvas.Effects.CompositeEffect { Mode = CanvasComposite.SourceAtop, Sources = { down.Surface, top } })
                            : up.Blend == BlendMode.Normal ? Own(owned, new Microsoft.Graphics.Canvas.Effects.CompositeEffect { Sources = { down.Surface, top } })
                            : Own(owned, new Microsoft.Graphics.Canvas.Effects.BlendEffect { Background = down.Surface, Foreground = top, Mode = PaintDocument.ToEffect(up.Blend) });
                        ds.DrawImage(merged);
                    }
                    owned.ForEach(o => o.Dispose());
                    after = Clone(tmp);
                }
                down.Surface.CopyPixelsFromBitmap(after);
                down.Touch();
                doc.Layers.Remove(up);
                doc.ActiveIndex = doc.Layers.IndexOf(down);
            },
            [before, () => after]));
        doc.IsDirty = true;
    }

    /// <summary>Voltea todas las capas (la operación es su propia inversa).</summary>
    public void FlipCanvas(bool horizontal)
    {
        void Flip()
        {
            foreach (var l in doc.Layers)
            {
                using var copy = Clone(l.Surface);
                using var ds = l.Surface.CreateDrawingSession();
                ds.Clear(Colors.Transparent);
                ds.Transform = horizontal
                    ? new System.Numerics.Matrix3x2(-1, 0, 0, 1, doc.Width, 0)
                    : new System.Numerics.Matrix3x2(1, 0, 0, -1, 0, doc.Height);
                ds.DrawImage(copy);
                l.Touch();
            }
        }
        doc.History.Execute(new DelegateAction(horizontal ? "Voltear horizontal" : "Voltear vertical", Flip, Flip));
        doc.IsDirty = true;
    }

    /// <summary>Inserta una imagen en una capa nueva, ajustada al lienzo y centrada.</summary>
    public Layer? AddImage(CanvasBitmap image, string name)
    {
        var l = doc.CreateLayer(name);
        var s = image.SizeInPixels;
        var k = Math.Min(1, Math.Min((double)doc.Width / s.Width, (double)doc.Height / s.Height));
        var w = s.Width * k;
        var h = s.Height * k;
        using (var ds = l.Surface.CreateDrawingSession())
            ds.DrawImage(image, new Rect((doc.Width - w) / 2, (doc.Height - h) / 2, w, h), new Rect(0, 0, s.Width, s.Height), 1, CanvasImageInterpolation.HighQualityCubic);
        return Add(l);
    }

    private CanvasRenderTarget Clone(CanvasBitmap src)
    {
        var rt = new CanvasRenderTarget(doc.Device, doc.Width, doc.Height, 96);
        rt.CopyPixelsFromBitmap(src);
        return rt;
    }

    private static T Own<T>(List<IDisposable> list, T x) where T : IDisposable
    {
        list.Add(x);
        return x;
    }

    private sealed class DisposingAction(string label, long bytes, Action undo, Action redo, object[] resources) : IUndoableAction, IDisposable
    {
        public string Label { get; } = label;
        public long Bytes { get; } = bytes;
        public void Undo() => undo();
        public void Redo() => redo();

        public void Dispose()
        {
            foreach (var r in resources)
                (r is Func<CanvasRenderTarget?> f ? f() : r as IDisposable)?.Dispose();
        }
    }
}
