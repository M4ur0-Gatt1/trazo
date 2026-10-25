using System.Numerics;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Effects;
using Microsoft.UI;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Trazo.Core.Imaging;
using Trazo.Core.Selection;
using Trazo.Rendering;
using Windows.UI;

namespace Trazo.App.Views;

/// <summary>Herramienta Seleccionar (SE-01): automática, a mano, rectángulo y elipse.</summary>
public sealed partial class StudioView
{
    private enum SelectTool
    {
        Automatic,
        Freehand,
        Rectangle,
        Ellipse,
    }

    private sealed class SelectionDrag
    {
        public uint Pointer;
        public Vector2 Start;     // documento
        public Vector2 Current;   // documento
        public readonly List<Vector2> Path = [];
    }

    private bool _selectMode;
    private SelectTool _selectTool = SelectTool.Freehand;
    private SelectionCombine _selectCombine = SelectionCombine.Replace;
    private int _wandTolerance = 40;
    private float _featherRadius = 12;
    private SelectionDrag? _selDrag;

    private void Select_Click(object sender, RoutedEventArgs e)
    {
        if (_selectMode) ExitSelectMode();
        else EnterSelectMode();
    }

    private void EnterSelectMode()
    {
        if (_doc is null) return;
        if (_transform is not null) ApplyTransform();
        if (_adjust is not null) CloseSheet();
        CancelInput();
        _selectMode = true;
        SelectButton.IsChecked = true;
        BuildSelectionSheet();
        OpenSheet("Seleccionar");
    }

    private void ExitSelectMode()
    {
        _selectMode = false;
        _selDrag = null;
        SelectButton.IsChecked = false;
        Sheet.Visibility = Visibility.Collapsed;
        Canvas.Invalidate();
    }

    private void BuildSelectionSheet()
    {
        SheetBody.Children.Clear();

        // filas de columnas iguales: caben en el ancho de la hoja sin desbordar
        static Grid Row(IReadOnlyList<FrameworkElement> items, double[]? weights = null)
        {
            var g = new Grid { ColumnSpacing = 6 };
            for (var i = 0; i < items.Count; i++)
            {
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(weights?[i] ?? 1, GridUnitType.Star) });
                items[i].HorizontalAlignment = HorizontalAlignment.Stretch;
                Grid.SetColumn(items[i], i);
                g.Children.Add(items[i]);
            }
            return g;
        }
        static T Compact<T>(T b) where T : ButtonBase
        {
            b.Padding = new Thickness(4, 5, 4, 6);
            b.FontSize = 13;
            return b;
        }

        Grid Choice<T>(IEnumerable<(T Value, string Label)> options, Func<T> get, Action<T> set) =>
            Row(options.Select(o =>
            {
                var b = Compact(new ToggleButton { Content = o.Label, IsChecked = EqualityComparer<T>.Default.Equals(o.Value, get()) });
                b.Click += (_, _) =>
                {
                    set(o.Value);
                    BuildSelectionSheet();
                };
                return (FrameworkElement)b;
            }).ToList());

        SheetBody.Children.Add(Choice(
            [(SelectTool.Automatic, "Automática"), (SelectTool.Freehand, "A mano"), (SelectTool.Rectangle, "Rectángulo"), (SelectTool.Ellipse, "Elipse")],
            () => _selectTool, v => _selectTool = v));
        SheetBody.Children.Add(Choice(
            [(SelectionCombine.Replace, "Nueva"), (SelectionCombine.Add, "Añadir"), (SelectionCombine.Subtract, "Restar"), (SelectionCombine.Intersect, "Intersecar")],
            () => _selectCombine, v => _selectCombine = v));

        Button Action(string text, string tip, Action a)
        {
            var b = Compact(new Button { Content = text });
            ToolTipService.SetToolTip(b, tip);
            b.Click += (_, _) => { a(); Canvas.Invalidate(); };
            return b;
        }
        SheetBody.Children.Add(Row(
        [
            Action("Invertir", "Invertir la selección (Ctrl+Mayús+I)", () => _ops?.InvertSelection()),
            Action("Copiar y pegar", "Copia lo seleccionado a una capa nueva", () => { if (_ops?.CopySelectionToLayer() is not null) Window.ShowMessage("Selección copiada a una capa nueva"); }),
            Action("Borrar", "Borra lo seleccionado de la capa activa", () => { if (_doc!.HasSelection) _ops?.Clear(_doc.ActiveIndex); }),
            Action("Todo", "Seleccionar todo (Ctrl+A)", () => _ops?.SelectAll()),
            Action("Ninguna", "Deseleccionar (Ctrl+D)", () => _ops?.Deselect()),
        ], [1, 1.5, 1, 1, 1]));

        // umbral de la automática o difuminado, en una sola fila
        var slider = _selectTool == SelectTool.Automatic
            ? new Slider { Minimum = 0, Maximum = 255, Value = _wandTolerance, VerticalAlignment = VerticalAlignment.Center }
            : new Slider { Minimum = 0, Maximum = 100, Value = _featherRadius, VerticalAlignment = VerticalAlignment.Center };
        var label = new TextBlock { VerticalAlignment = VerticalAlignment.Center, FontSize = 13, Width = 120 };
        void Label() => label.Text = _selectTool == SelectTool.Automatic ? $"Umbral {_wandTolerance}" : $"Difuminar {_featherRadius:0} px";
        slider.ValueChanged += (_, e) =>
        {
            if (_selectTool == SelectTool.Automatic) _wandTolerance = (int)e.NewValue;
            else _featherRadius = (float)e.NewValue;
            Label();
        };
        Label();
        var bottom = new Grid { ColumnSpacing = 10 };
        bottom.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        bottom.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        bottom.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        bottom.Children.Add(label);
        Grid.SetColumn(slider, 1);
        bottom.Children.Add(slider);
        if (_selectTool != SelectTool.Automatic)
        {
            var feather = Action("Difuminar", "Suaviza el borde de la selección", () => _ops?.FeatherSelection(_featherRadius));
            Grid.SetColumn(feather, 2);
            bottom.Children.Add(feather);
        }
        SheetBody.Children.Add(bottom);
    }

    // ---------- Entrada ----------

    /// <summary>Empieza una selección; devuelve false si el puntero debe seguir el camino normal (navegar).</summary>
    private bool TryBeginSelection(PointerPoint pt, PointerDeviceType type, Contact c)
    {
        if (type == PointerDeviceType.Touch)
        {
            // un segundo dedo cancela la forma en curso y pasa a navegar
            if (Touches.Count() >= 2)
            {
                _selDrag = null;
                Canvas.Invalidate();
                return false;
            }
            if (PenOnly) return false;
        }
        if (type == PointerDeviceType.Mouse && (!pt.Properties.IsLeftButtonPressed || _spaceDown)) return false;
        var d = _view.ToDocument(c.Position);
        _selDrag = new SelectionDrag { Pointer = c.Id, Start = d, Current = d };
        _selDrag.Path.Add(d);
        return true;
    }

    private void UpdateSelectionDrag(Vector2 screen)
    {
        var d = _view.ToDocument(screen);
        _selDrag!.Current = d;
        if (Vector2.Distance(d, _selDrag.Path[^1]) * _view.Scale >= 2) _selDrag.Path.Add(d);
        Canvas.Invalidate();
    }

    private void EndSelectionDrag(bool cancelled)
    {
        var drag = _selDrag;
        _selDrag = null;
        Canvas.Invalidate();
        if (cancelled || drag is null || _doc is null || _ops is null) return;
        var doc = _doc;
        var minDrag = 3 / _view.Scale;
        SelectionMask? shape = null;
        string label;
        switch (_selectTool)
        {
            case SelectTool.Automatic:
                byte[] px;
                using (var flat = doc.Flatten()) px = flat.GetPixelBytes();
                var fill = FloodFill.Compute(px, doc.Width, doc.Height, (int)MathF.Floor(drag.Start.X), (int)MathF.Floor(drag.Start.Y), _wandTolerance);
                if (fill is not null) shape = SelectionMask.FromFill(fill, doc.Width, doc.Height);
                label = "Selección automática";
                break;
            case SelectTool.Rectangle:
                if (Vector2.Distance(drag.Start, drag.Current) >= minDrag)
                    shape = SelectionMask.Rectangle(doc.Width, doc.Height, drag.Start.X, drag.Start.Y, drag.Current.X, drag.Current.Y);
                label = "Selección rectangular";
                break;
            case SelectTool.Ellipse:
                if (Vector2.Distance(drag.Start, drag.Current) >= minDrag)
                    shape = SelectionMask.Ellipse(doc.Width, doc.Height, drag.Start.X, drag.Start.Y, drag.Current.X, drag.Current.Y);
                label = "Selección elíptica";
                break;
            default:
                if (drag.Path.Count >= 3) shape = SelectionMask.Polygon(doc.Width, doc.Height, drag.Path);
                label = "Selección a mano";
                break;
        }
        if (shape is null)
        {
            // un toque sin forma con «Nueva» deselecciona, como en Procreate
            if (_selectTool != SelectTool.Automatic && _selectCombine == SelectionCombine.Replace) _ops.Deselect();
            return;
        }
        _ops.Select(shape, _selectCombine, label);
    }

    // ---------- Dibujo ----------

    /// <summary>Oscurece lo no seleccionado y marca el borde en cian (en coordenadas del documento).</summary>
    private void DrawSelectionOverlay(CanvasDrawingSession ds, PaintDocument doc, List<IDisposable> owned)
    {
        if (doc.SelectionBitmap is not { } sel) return;
        T Own<T>(T x) where T : IDisposable
        {
            owned.Add(x);
            return x;
        }
        var shade = Own(new CropEffect { Source = Own(new ColorSourceEffect { Color = Color.FromArgb(_selectMode ? (byte)110 : (byte)60, 0, 0, 0) }), SourceRectangle = doc.Bounds });
        ds.DrawImage(Own(new CompositeEffect { Mode = CanvasComposite.DestinationOut, Sources = { shade, sel } }));
        // borde: dilatación menos la selección, teñido de cian
        var width = (int)Math.Clamp(MathF.Ceiling(1.5f / _view.Scale), 1, 100);
        var dilated = Own(new MorphologyEffect { Source = sel, Mode = MorphologyEffectMode.Dilate, Width = width, Height = width });
        var ring = Own(new CompositeEffect { Mode = CanvasComposite.DestinationOut, Sources = { dilated, sel } });
        var cyan = Own(new ColorMatrixEffect
        {
            Source = ring,
            ColorMatrix = new Matrix5x4 { M44 = 1, M51 = 0, M52 = 229 / 255f, M53 = 1 },
            ClampOutput = true,
        });
        ds.DrawImage(cyan);
    }

    /// <summary>Forma que se está dibujando (en coordenadas de pantalla).</summary>
    private void DrawSelectionDrag(CanvasDrawingSession ds)
    {
        if (_selDrag is not { } d) return;
        var cyan = Color.FromArgb(255, 0, 229, 255);
        var style = new Microsoft.Graphics.Canvas.Geometry.CanvasStrokeStyle { DashStyle = Microsoft.Graphics.Canvas.Geometry.CanvasDashStyle.Dash };
        var a = _view.ToScreen(d.Start);
        var b = _view.ToScreen(d.Current);
        switch (_selectTool)
        {
            case SelectTool.Rectangle or SelectTool.Ellipse:
                var corners = new[] { d.Start, new Vector2(d.Current.X, d.Start.Y), d.Current, new Vector2(d.Start.X, d.Current.Y) }.Select(_view.ToScreen).ToArray();
                if (_selectTool == SelectTool.Rectangle)
                {
                    for (var i = 0; i < 4; i++) ds.DrawLine(corners[i], corners[(i + 1) % 4], cyan, 1.5f, style);
                }
                else
                {
                    var c = (d.Start + d.Current) / 2;
                    var r = Vector2.Abs(d.Current - d.Start) / 2;
                    var prev = _view.ToScreen(c + new Vector2(r.X, 0));
                    for (var i = 1; i <= 96; i++)
                    {
                        var t = i * MathF.Tau / 96;
                        var p = _view.ToScreen(c + new Vector2(MathF.Cos(t) * r.X, MathF.Sin(t) * r.Y));
                        ds.DrawLine(prev, p, cyan, 1.5f, style);
                        prev = p;
                    }
                }
                break;
            case SelectTool.Freehand:
                for (var i = 1; i < d.Path.Count; i++) ds.DrawLine(_view.ToScreen(d.Path[i - 1]), _view.ToScreen(d.Path[i]), cyan, 1.5f);
                if (d.Path.Count > 2) ds.DrawLine(_view.ToScreen(d.Path[^1]), _view.ToScreen(d.Path[0]), cyan, 1f, style);
                break;
            default:
                ds.DrawCircle(a, 8, cyan, 2);
                break;
        }
        _ = b;
    }
}
