using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Effects;
using Trazo.Core.Documents;
using Trazo.Core.History;
using Trazo.Core.Selection;
using Windows.Foundation;
using Windows.UI;
using static Trazo.Core.Localization.Tr;

namespace Trazo.Rendering;

/// <summary>Cambio de píxeles en un rectángulo de una textura (capa o máscara), guardado como dos parches en la GPU.</summary>
public sealed class PixelPatchAction : IUndoableAction, IDisposable
{
    private readonly Layer _layer;
    private readonly CanvasRenderTarget _target;
    private readonly CanvasRenderTarget _before, _after;
    private readonly Rect _rect;

    private PixelPatchAction(string label, Layer layer, CanvasRenderTarget target, Rect rect, CanvasRenderTarget before, CanvasRenderTarget after)
    {
        Label = label;
        _layer = layer;
        _target = target;
        _rect = rect;
        _before = before;
        _after = after;
        Bytes = (long)(rect.Width * rect.Height * 8);
    }

    public string Label { get; }
    public long Bytes { get; }

    /// <summary>Crea la acción a partir de una copia previa de la textura completa.</summary>
    public static PixelPatchAction? Create(string label, Layer layer, CanvasRenderTarget target, CanvasBitmap snapshot, Rect dirty)
    {
        var size = target.SizeInPixels;
        var r = Clip(dirty, size.Width, size.Height);
        if (r.Width <= 0 || r.Height <= 0) return null;
        return new PixelPatchAction(label, layer, target, r, Copy(snapshot, r), Copy(target, r));
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
        _target.CopyPixelsFromBitmap(patch, (int)_rect.X, (int)_rect.Y);
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
public sealed class LayerPropertyAction<T>(string label, Layer layer, string key, T oldValue, T newValue, Action<Layer, T> setter, bool mergeable) : IMergeableAction
{
    private T _new = newValue;
    private readonly DateTime _time = DateTime.UtcNow;

    public string Label { get; } = label;
    public long Bytes => 0;
    public void Undo() => setter(layer, oldValue);
    public void Redo() => setter(layer, _new);

    // Solo las propiedades continuas (arrastrar un deslizador) se fusionan en un paso;
    // los interruptores (visible, recorte...) registran cada cambio por separado.
    public bool TryMerge(IUndoableAction next)
    {
        if (!mergeable || next is not LayerPropertyAction<T> n || !n.Mergeable || n.LayerRef != layer || n.Key != key || (n._time - _time).TotalMilliseconds > 1200) return false;
        _new = n._new;
        return true;
    }

    private bool Mergeable => mergeable;
    private Layer LayerRef => layer;
    private string Key => key;
}

/// <summary>Operaciones de capa, máscara, grupo y selección, todas con deshacer.</summary>
public sealed class LayerOperations(PaintDocument doc)
{
    private CanvasRenderTarget? _snapshot;
    private CanvasRenderTarget? _snapshotOf;
    private long LayerBytes => (long)doc.Width * doc.Height * 4;

    public event Action<string>? Message;

    public CanvasRenderTarget? CurrentSnapshot => _snapshot;

    // ---------- Instantáneas para operaciones de píxeles ----------

    /// <summary>Copia la textura de trabajo de la capa (la capa o su máscara) antes de modificarla.</summary>
    public CanvasRenderTarget Snapshot(Layer layer)
    {
        if (_snapshot is null || _snapshot.SizeInPixels.Width != doc.Width || _snapshot.SizeInPixels.Height != doc.Height)
        {
            _snapshot?.Dispose();
            _snapshot = new CanvasRenderTarget(doc.Device, doc.Width, doc.Height, 96);
        }
        _snapshotOf = layer.Target;
        _snapshot.CopyPixelsFromBitmap(layer.Target);
        return _snapshot;
    }

    /// <summary>
    /// Confirma un cambio de píxeles en la textura de trabajo de la capa. Con una selección activa,
    /// restaura lo que hay fuera de ella (SE-01: regla única para todas las herramientas).
    /// </summary>
    public void CommitPixels(Layer layer, Rect dirty, string label, bool clipToSelection = true)
    {
        var target = layer.Target;
        if (_snapshot is null || _snapshotOf != target)
        {
            layer.Touch();
            return;
        }
        if (clipToSelection && doc.HasSelection) ClipToSelection(target, dirty);
        layer.Touch();
        var a = PixelPatchAction.Create(label, layer, target, _snapshot, dirty);
        if (a is not null) doc.History.Push(a);
        doc.IsDirty = true;
    }

    private void ClipToSelection(CanvasRenderTarget target, Rect dirty)
    {
        var r = PixelPatchAction.Clip(dirty, doc.Width, doc.Height);
        if (r.IsEmpty) return;
        var owned = new List<IDisposable>();
        using var temp = new CanvasRenderTarget(doc.Device, doc.Width, doc.Height, 96);
        using (var current = Clone(target))
        using (var ds = temp.CreateDrawingSession())
        {
            ds.Clear(KnownColors.Transparent);
            ds.DrawImage(doc.SelectionBlend(_snapshot!, current, owned), new System.Numerics.Vector2((float)r.X, (float)r.Y), r);
        }
        owned.ForEach(o => o.Dispose());
        target.CopyPixelsFromBitmap(temp, (int)r.X, (int)r.Y, (int)r.X, (int)r.Y, (int)r.Width, (int)r.Height);
    }

    /// <summary>Aplica una operación sobre la textura de trabajo completa con deshacer.</summary>
    public void PixelOp(Layer layer, string label, Action<CanvasDrawingSession> draw)
    {
        Snapshot(layer);
        using (var ds = layer.Target.CreateDrawingSession()) draw(ds);
        CommitPixels(layer, doc.Bounds, label);
    }

    /// <summary>Sustituye la textura de trabajo por la imagen dada (renderizada aparte para no leer y escribir a la vez).</summary>
    public void ReplaceWith(Layer layer, ICanvasImage image, string label, bool clipToSelection = true)
    {
        using var temp = new CanvasRenderTarget(doc.Device, doc.Width, doc.Height, 96);
        using (var ds = temp.CreateDrawingSession())
        {
            ds.Clear(KnownColors.Transparent);
            ds.DrawImage(image);
        }
        Snapshot(layer);
        layer.Target.CopyPixelsFromBitmap(temp);
        CommitPixels(layer, doc.Bounds, label, clipToSelection);
    }

    // ---------- Estructura ----------

    public Layer? Add(Layer? layer = null, int? at = null)
    {
        if (doc.Layers.Count >= doc.MaxLayers)
        {
            Message?.Invoke(F("Límite de {0} capas para este tamaño de lienzo", doc.MaxLayers));
            return null;
        }
        var l = layer ?? doc.CreateLayer();
        var idx = Math.Clamp(at ?? doc.ActiveIndex + 1, 0, doc.Layers.Count);
        var group = GroupRules.GroupForInsert(doc.GroupIds, idx);
        var prev = doc.ActiveIndex;
        doc.History.Execute(new DelegateAction(T("Nueva capa"),
            () => { doc.Layers.Remove(l); doc.ActiveIndex = prev; },
            () => { l.GroupId = group; doc.Layers.Insert(idx, l); doc.ActiveIndex = idx; },
            LayerBytes));
        doc.IsDirty = true;
        return l;
    }

    public void Remove(int index)
    {
        if (doc.Layers.Count <= 1)
        {
            Clear(index);
            Message?.Invoke(T("Se vació la única capa"));
            return;
        }
        var l = doc.Layers[index];
        var prev = doc.ActiveIndex;
        doc.History.Execute(new DelegateAction(T("Eliminar capa"),
            () => { doc.Layers.Insert(index, l); doc.ActiveIndex = prev; },
            () => { doc.Layers.Remove(l); doc.ActiveIndex = Math.Max(0, index - 1); }));
        doc.IsDirty = true;
    }

    public void Duplicate(int index) => Add(doc.Layers[index].Duplicate(doc.Device), index + 1);

    /// <summary>Mueve una capa; entra o sale de los grupos según <see cref="GroupRules.GroupAfterMove"/>.</summary>
    public void Move(int from, int to)
    {
        if (from == to || to < 0 || to >= doc.Layers.Count) return;
        var layer = doc.Layers[from];
        var oldGroup = layer.GroupId;
        var ids = doc.GroupIds.ToList();
        ids.RemoveAt(from);
        ids.Insert(to, oldGroup);
        var newGroup = GroupRules.GroupAfterMove(ids, to, oldGroup);
        doc.History.Execute(new DelegateAction(T("Mover capa"),
            () => { doc.Layers.Move(to, from); layer.GroupId = oldGroup; doc.ActiveIndex = from; },
            () => { doc.Layers.Move(from, to); layer.GroupId = newGroup; doc.ActiveIndex = to; }));
        doc.IsDirty = true;
    }

    public void Set<T>(Layer layer, string key, T value, Func<Layer, T> getter, Action<Layer, T> setter, string label)
    {
        var old = getter(layer);
        if (EqualityComparer<T>.Default.Equals(old, value)) return;
        setter(layer, value);
        doc.History.Push(new LayerPropertyAction<T>(label, layer, key, old, value, setter, mergeable: value is float));
        doc.IsDirty = true;
    }

    /// <summary>
    /// LY-04: marca <paramref name="layer"/> como capa de referencia (o ninguna con null), quitando la marca a la
    /// anterior. Un solo paso de deshacer.
    /// </summary>
    public void SetReference(Layer? layer)
    {
        var old = doc.ReferenceLayer;
        if (ReferenceEquals(old, layer)) return;
        void Mark(Layer? on)
        {
            foreach (var l in doc.Layers) l.Reference = ReferenceEquals(l, on);
        }
        doc.History.Execute(new DelegateAction(layer is null ? T("Quitar referencia") : T("Referencia"), () => Mark(old), () => Mark(layer)));
        doc.IsDirty = true;
    }

    public void SetBackground(Color color, bool visible)
    {
        var (oc, ov) = (doc.Background, doc.BackgroundVisible);
        if (oc == color && ov == visible) return;
        doc.History.Execute(new DelegateAction(T("Fondo"),
            () => { doc.Background = oc; doc.BackgroundVisible = ov; },
            () => { doc.Background = color; doc.BackgroundVisible = visible; }));
        doc.IsDirty = true;
    }

    /// <summary>Borra el contenido (en la máscara: la deja blanca, todo visible).</summary>
    public void Clear(int index)
    {
        var layer = doc.Layers[index];
        PixelOp(layer, layer.EditingMask ? T("Restablecer máscara") : T("Borrar capa"),
            ds => ds.Clear(layer.EditingMask ? KnownColors.White : KnownColors.Transparent));
    }

    /// <summary>Rellena con el color (en la máscara: con su luminancia en gris).</summary>
    public void Fill(int index, Color color)
    {
        var layer = doc.Layers[index];
        if (layer.EditingMask) color = ToMaskGray(color);
        if (!layer.TargetAlphaLock)
        {
            PixelOp(layer, T("Rellenar capa"), ds => ds.Clear(color));
            return;
        }
        using var solid = new CanvasRenderTarget(doc.Device, doc.Width, doc.Height, 96);
        using (var ds = solid.CreateDrawingSession()) ds.Clear(color);
        PixelOp(layer, T("Rellenar capa"), ds =>
            ds.DrawImage(solid, System.Numerics.Vector2.Zero, doc.Bounds, 1, CanvasImageInterpolation.NearestNeighbor, CanvasComposite.SourceAtop));
    }

    /// <summary>Color gris con la luminancia Rec. 709 del color (para pintar en máscaras).</summary>
    public static Color ToMaskGray(Color c)
    {
        var y = (byte)Math.Clamp(Math.Round(0.2126 * c.R + 0.7152 * c.G + 0.0722 * c.B), 0, 255);
        return Color.FromArgb(255, y, y, y);
    }

    public void Invert(int index)
    {
        var layer = doc.Layers[index];
        // Direct2D invierte los canales de color sin premultiplicar y conserva el alfa
        using var fx = new InvertEffect { Source = layer.Target };
        ReplaceWith(layer, fx, layer.EditingMask ? T("Invertir máscara") : T("Invertir"));
    }

    public void MergeDown(int index)
    {
        if (index <= 0) return;
        var up = doc.Layers[index];
        var down = doc.Layers[index - 1];
        if (up.GroupId != down.GroupId)
        {
            Message?.Invoke(T("Solo se pueden combinar capas del mismo grupo"));
            return;
        }
        var before = Clone(down.Surface);
        var beforeMask = down.Mask;
        CanvasRenderTarget? after = null;
        var prev = doc.ActiveIndex;
        doc.History.Execute(new DisposingAction(T("Combinar abajo"), LayerBytes * 2,
            () =>
            {
                down.Surface.CopyPixelsFromBitmap(before);
                down.Mask = beforeMask;
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
                        ds.Clear(KnownColors.Transparent);
                        // la máscara de cada capa se aplica antes de combinar; el resultado no tiene máscara
                        var bottom = doc.LayerImage(down, null, owned);
                        var upImg = doc.LayerImage(up, null, owned);
                        ICanvasImage top = up.Opacity < 1 ? Own(owned, new OpacityEffect { Source = upImg, Opacity = up.Opacity }) : upImg;
                        ICanvasImage merged = !up.Visible ? bottom
                            : up.Clip ? Own(owned, new CompositeEffect { Mode = CanvasComposite.SourceAtop, Sources = { bottom, top } })
                            : up.Blend == BlendMode.Normal ? Own(owned, new CompositeEffect { Sources = { bottom, top } })
                            : Own(owned, new BlendEffect { Background = bottom, Foreground = top, Mode = PaintDocument.ToEffect(up.Blend) });
                        ds.DrawImage(merged);
                    }
                    owned.ForEach(o => o.Dispose());
                    after = Clone(tmp);
                }
                down.Surface.CopyPixelsFromBitmap(after);
                down.Mask = null;
                down.Touch();
                doc.Layers.Remove(up);
                doc.ActiveIndex = doc.Layers.IndexOf(down);
            },
            [before, () => after]));
        doc.IsDirty = true;
    }

    /// <summary>Voltea todas las capas, sus máscaras y la selección (la operación es su propia inversa).</summary>
    public void FlipCanvas(bool horizontal)
    {
        void FlipTexture(CanvasRenderTarget t, Color clear)
        {
            using var copy = Clone(t);
            using var ds = t.CreateDrawingSession();
            ds.Clear(clear);
            ds.Transform = horizontal
                ? new System.Numerics.Matrix3x2(-1, 0, 0, 1, doc.Width, 0)
                : new System.Numerics.Matrix3x2(1, 0, 0, -1, 0, doc.Height);
            ds.DrawImage(copy);
        }
        void Flip()
        {
            foreach (var l in doc.Layers)
            {
                FlipTexture(l.Surface, KnownColors.Transparent);
                if (l.Mask is { } m) FlipTexture(m, KnownColors.White);
                l.Touch();
            }
            if (doc.Selection is { } s) doc.Selection = FlipSelection(s, horizontal);
        }
        doc.History.Execute(new DelegateAction(horizontal ? T("Voltear horizontal") : T("Voltear vertical"), Flip, Flip));
        doc.IsDirty = true;
    }

    private static SelectionMask FlipSelection(SelectionMask s, bool horizontal)
    {
        var r = new SelectionMask(s.Width, s.Height);
        for (var y = 0; y < s.Height; y++)
        for (var x = 0; x < s.Width; x++)
            r.Data[y * s.Width + x] = horizontal ? s[s.Width - 1 - x, y] : s[x, s.Height - 1 - y];
        return r;
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

    // ---------- Máscaras de capa (LY-02) ----------

    public void AddMask(Layer layer)
    {
        if (layer.HasMask) return;
        var mask = Layer.CreateMask(doc.Device, doc.Width, doc.Height, KnownColors.White);
        doc.History.Execute(new DelegateAction(T("Añadir máscara"),
            () => { layer.Mask = null; layer.Touch(); },
            () => { layer.Mask = mask; layer.EditingMask = true; layer.Touch(); },
            LayerBytes));
        doc.IsDirty = true;
    }

    public void DeleteMask(Layer layer)
    {
        if (layer.Mask is not { } mask) return;
        var editing = layer.EditingMask;
        doc.History.Execute(new DelegateAction(T("Eliminar máscara"),
            () => { layer.Mask = mask; layer.EditingMask = editing; layer.Touch(); },
            () => { layer.Mask = null; layer.Touch(); }));
        doc.IsDirty = true;
    }

    /// <summary>Multiplica la capa por su máscara y elimina la máscara; la imagen compuesta no cambia.</summary>
    public void ApplyMask(Layer layer)
    {
        if (layer.Mask is not { } mask) return;
        var before = Clone(layer.Surface);
        CanvasRenderTarget after;
        using (var tmp = new CanvasRenderTarget(doc.Device, doc.Width, doc.Height, 96))
        {
            var owned = new List<IDisposable>();
            using (var ds = tmp.CreateDrawingSession())
            {
                ds.Clear(KnownColors.Transparent);
                ds.DrawImage(doc.LayerImage(layer, null, owned));
            }
            owned.ForEach(o => o.Dispose());
            after = Clone(tmp);
        }
        var editing = layer.EditingMask;
        doc.History.Execute(new DisposingAction(T("Aplicar máscara"), LayerBytes * 2,
            () => { layer.Surface.CopyPixelsFromBitmap(before); layer.Mask = mask; layer.EditingMask = editing; layer.Touch(); },
            () => { layer.Surface.CopyPixelsFromBitmap(after); layer.Mask = null; layer.Touch(); },
            [before, after]));
        doc.IsDirty = true;
    }

    public void InvertMask(Layer layer)
    {
        if (layer.Mask is null) return;
        var editing = layer.EditingMask;
        layer.EditingMask = true;
        Invert(doc.Layers.IndexOf(layer));
        layer.EditingMask = editing;
    }

    // ---------- Grupos (LY-03) ----------

    /// <summary>Crea un grupo con la capa (que no debe pertenecer a otro).</summary>
    public LayerGroup? CreateGroup(int index)
    {
        var layer = doc.Layers[index];
        if (layer.GroupId is not null)
        {
            Message?.Invoke(T("La capa ya pertenece a un grupo"));
            return null;
        }
        var g = new LayerGroup { Name = NextGroupName() };
        doc.History.Execute(new DelegateAction(T("Nuevo grupo"),
            () => { layer.GroupId = null; doc.Groups.Remove(g); },
            () => { doc.Groups.Add(g); layer.GroupId = g.Id; }));
        doc.IsDirty = true;
        return g;
    }

    /// <summary>Agrupa la capa con la de abajo (une su grupo si la de abajo ya tiene uno).</summary>
    public void GroupWithBelow(int index)
    {
        if (index <= 0) return;
        var layer = doc.Layers[index];
        var below = doc.Layers[index - 1];
        if (layer.GroupId is not null)
        {
            Message?.Invoke(T("La capa ya pertenece a un grupo"));
            return;
        }
        if (below.GroupId is { } existing)
        {
            doc.History.Execute(new DelegateAction(T("Agrupar"),
                () => layer.GroupId = null,
                () => layer.GroupId = existing));
        }
        else
        {
            var g = new LayerGroup { Name = NextGroupName() };
            doc.History.Execute(new DelegateAction(T("Agrupar"),
                () => { layer.GroupId = null; below.GroupId = null; doc.Groups.Remove(g); },
                () => { doc.Groups.Add(g); layer.GroupId = g.Id; below.GroupId = g.Id; }));
        }
        doc.IsDirty = true;
    }

    public void Ungroup(string groupId)
    {
        var g = doc.Group(groupId);
        if (g is null) return;
        var members = doc.Layers.Where(l => l.GroupId == groupId).ToList();
        var gi = doc.Groups.IndexOf(g);
        doc.History.Execute(new DelegateAction(T("Desagrupar"),
            () => { doc.Groups.Insert(gi, g); members.ForEach(l => l.GroupId = groupId); },
            () => { members.ForEach(l => l.GroupId = null); doc.Groups.Remove(g); }));
        doc.IsDirty = true;
    }

    public void DeleteGroup(string groupId)
    {
        var g = doc.Group(groupId);
        if (g is null || GroupRules.Range(doc.GroupIds, groupId) is not { } range) return;
        if (range.Last - range.First + 1 == doc.Layers.Count)
        {
            Message?.Invoke(T("No se puede eliminar el grupo: contiene todas las capas"));
            return;
        }
        var members = doc.Layers.Skip(range.First).Take(range.Last - range.First + 1).ToList();
        var gi = doc.Groups.IndexOf(g);
        var prev = doc.ActiveIndex;
        doc.History.Execute(new DelegateAction(T("Eliminar grupo"),
            () =>
            {
                doc.Groups.Insert(gi, g);
                for (var k = 0; k < members.Count; k++) doc.Layers.Insert(range.First + k, members[k]);
                doc.ActiveIndex = prev;
            },
            () =>
            {
                foreach (var l in members) doc.Layers.Remove(l);
                doc.Groups.Remove(g);
                doc.ActiveIndex = Math.Max(0, range.First - 1);
            }));
        doc.IsDirty = true;
    }

    /// <summary>Combina el grupo en una sola capa con la opacidad y el modo del grupo: la imagen compuesta no cambia.</summary>
    public void MergeGroup(string groupId)
    {
        var g = doc.Group(groupId);
        if (g is null || GroupRules.Range(doc.GroupIds, groupId) is not { } range) return;
        var members = doc.Layers.Skip(range.First).Take(range.Last - range.First + 1).ToList();
        var merged = doc.CreateLayer(g.Name);
        merged.Opacity = g.Opacity;
        merged.Blend = g.Blend;
        merged.Visible = g.Visible;
        var owned = new List<IDisposable>();
        using (var ds = merged.Surface.CreateDrawingSession())
        {
            ds.Clear(KnownColors.Transparent);
            ds.DrawImage(doc.ComposeLayers(range.First, range.Last, owned));
        }
        owned.ForEach(o => o.Dispose());
        var gi = doc.Groups.IndexOf(g);
        var prev = doc.ActiveIndex;
        doc.History.Execute(new DelegateAction(T("Combinar grupo"),
            () =>
            {
                doc.Layers.Remove(merged);
                doc.Groups.Insert(gi, g);
                for (var k = 0; k < members.Count; k++) doc.Layers.Insert(range.First + k, members[k]);
                doc.ActiveIndex = prev;
            },
            () =>
            {
                foreach (var l in members) doc.Layers.Remove(l);
                doc.Groups.Remove(g);
                doc.Layers.Insert(range.First, merged);
                doc.ActiveIndex = range.First;
            },
            LayerBytes));
        doc.IsDirty = true;
    }

    public void SetGroup<T>(LayerGroup g, T value, Func<LayerGroup, T> getter, Action<LayerGroup, T> setter, string label)
    {
        var old = getter(g);
        if (EqualityComparer<T>.Default.Equals(old, value)) return;
        doc.History.Execute(new DelegateAction(label, () => setter(g, old), () => setter(g, value)));
        doc.IsDirty = true;
    }

    private string NextGroupName()
    {
        var names = doc.Groups.Select(g => g.Name).ToHashSet();
        var n = 1;
        while (names.Contains(F("Grupo {0}", n))) n++;
        return F("Grupo {0}", n);
    }

    // ---------- Selección (SE-01) ----------

    /// <summary>Cambia la selección con deshacer (null o vacía = sin selección).</summary>
    public void SetSelection(SelectionMask? selection, string label)
    {
        var old = doc.Selection;
        if (ReferenceEquals(old, selection)) return;
        doc.History.Execute(new DelegateAction(label,
            () => doc.Selection = old,
            () => doc.Selection = selection,
            (long)doc.Width * doc.Height * 2));
    }

    /// <summary>Combina una forma nueva con la selección actual.</summary>
    public void Select(SelectionMask shape, SelectionCombine mode, string label)
    {
        var current = doc.Selection;
        var result = current is null
            ? mode is SelectionCombine.Subtract or SelectionCombine.Intersect ? null : shape
            : current.Combine(shape, mode);
        SetSelection(result, label);
    }

    public void SelectAll() => SetSelection(SelectionMask.All(doc.Width, doc.Height), T("Seleccionar todo"));
    public void Deselect() => SetSelection(null, T("Deseleccionar"));

    public void InvertSelection() =>
        SetSelection((doc.Selection ?? new SelectionMask(doc.Width, doc.Height)).Invert(), T("Invertir selección"));

    public void FeatherSelection(float radius)
    {
        if (doc.Selection is { } s && radius > 0) SetSelection(s.Feather(radius), T("Difuminar selección"));
    }

    /// <summary>Selecciona el contenido de la capa: la cobertura es su alfa.</summary>
    public void SelectLayerContent(Layer layer)
    {
        var px = layer.Surface.GetPixelBytes();
        var data = new byte[doc.Width * doc.Height];
        for (var i = 0; i < data.Length; i++) data[i] = px[i * 4 + 3];
        var mask = new SelectionMask(doc.Width, doc.Height, data);
        if (mask.IsEmpty)
        {
            Message?.Invoke(T("La capa está vacía"));
            return;
        }
        SetSelection(mask, T("Seleccionar contenido"));
    }

    /// <summary>Copia lo seleccionado de la capa activa a una capa nueva encima ("Copiar y pegar").</summary>
    public Layer? CopySelectionToLayer()
    {
        if (doc.SelectionBitmap is not { } sel)
        {
            Message?.Invoke(T("No hay ninguna selección"));
            return null;
        }
        var source = doc.ActiveLayer;
        var l = doc.CreateLayer(source.Name + T(" (selección)"));
        using (var masked = new CompositeEffect { Mode = CanvasComposite.DestinationIn, Sources = { source.Surface, sel } })
        using (var ds = l.Surface.CreateDrawingSession())
        {
            ds.Clear(KnownColors.Transparent);
            ds.DrawImage(masked);
        }
        return Add(l, doc.ActiveIndex + 1);
    }

    // ---------- Utilidades ----------

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
