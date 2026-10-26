using System.Numerics;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Geometry;
using Microsoft.UI;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Trazo.Core.Geometry;
using Trazo.Core.Imaging;
using Trazo.Rendering;
using Windows.UI;
using static Trazo.Core.Localization.Tr;

namespace Trazo.App.Views;

/// <summary>Modos y asas de Transformar (TR-01), Licuar (LQ-01) y Guías de dibujo (DG-01).</summary>
public sealed partial class StudioView
{
    private static readonly Color Accent = Color.FromArgb(255, 0, 229, 255);

    // filas de botones de igual ancho que caben en la hoja inferior
    private static Grid EqualRow(IReadOnlyList<FrameworkElement> items)
    {
        var g = new Grid { ColumnSpacing = 6 };
        for (var i = 0; i < items.Count; i++)
        {
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            items[i].HorizontalAlignment = HorizontalAlignment.Stretch;
            Grid.SetColumn(items[i], i);
            g.Children.Add(items[i]);
        }
        return g;
    }

    private static T CompactButton<T>(T b) where T : ButtonBase
    {
        b.Padding = new Thickness(4, 5, 4, 6);
        b.FontSize = 13;
        return b;
    }

    private Grid ChoiceRow<T>(IEnumerable<(T Value, string Label)> options, Func<T> get, Action<T> set)
    {
        var buttons = new List<ToggleButton>();
        foreach (var (value, label) in options)
        {
            var b = CompactButton(new ToggleButton { Content = label, IsChecked = EqualityComparer<T>.Default.Equals(value, get()) });
            b.Click += (_, _) =>
            {
                set(value);
                foreach (var o in buttons) o.IsChecked = ReferenceEquals(o, b);
                Canvas.Invalidate();
            };
            buttons.Add(b);
        }
        return EqualRow(buttons);
    }

    private Grid SliderRow(string label, double min, double max, double value, Func<double, string> format, Action<double> set)
    {
        var text = new TextBlock { VerticalAlignment = VerticalAlignment.Center, FontSize = 13, Width = 130, Text = $"{label} {format(value)}" };
        var slider = new Slider { Minimum = min, Maximum = max, Value = value, VerticalAlignment = VerticalAlignment.Center };
        slider.ValueChanged += (_, e) =>
        {
            text.Text = $"{label} {format(e.NewValue)}";
            set(e.NewValue);
            Canvas.Invalidate();
        };
        var g = new Grid { ColumnSpacing = 10 };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.Children.Add(text);
        Grid.SetColumn(slider, 1);
        g.Children.Add(slider);
        return g;
    }

    private float HitRadius(PointerDeviceType type) => type == PointerDeviceType.Touch ? 26 : 14;

    // =============== Transformar: modos y asas ===============

    private sealed record TransformDrag(uint Pointer, TransformHandle? Handle, int Point);
    private TransformDrag? _transformDrag;

    private void BuildTransformSheet()
    {
        SheetBody.Children.Clear();
        SheetBody.Children.Add(ChoiceRow(
            [(TransformMode.Freeform, T("Libre")), (TransformMode.Uniform, T("Uniforme")), (TransformMode.Distort, T("Distorsionar")), (TransformMode.Warp, T("Deformar"))],
            () => _transform?.Mode ?? TransformMode.Freeform, m => _transform?.SetMode(m)));
        Button Action(string text, Action a)
        {
            var b = CompactButton(new Button { Content = text });
            b.Click += (_, _) => { a(); Canvas.Invalidate(); };
            return b;
        }
        SheetBody.Children.Add(EqualRow(
        [
            Action(T("Voltear H"), () => _transform?.Flip(true)),
            Action(T("Voltear V"), () => _transform?.Flip(false)),
            Action(T("Rotar 90°"), () => _transform?.Rotate(90)),
            Action(T("Ajustar"), () => _transform?.FitToCanvas()),
            Action(T("Restablecer"), () => _transform?.Reset()),
        ]));
        // interruptores sin cabecera, en una sola fila
        ToggleSwitch Switch(string label, bool on, Action<bool> set)
        {
            var t = new ToggleSwitch { IsOn = on, OnContent = label, OffContent = label, MinWidth = 0 };
            t.Toggled += (_, _) => { set(t.IsOn); _settings.Save(); Canvas.Invalidate(); };
            return t;
        }
        SheetBody.Children.Add(EqualRow(
        [
            Switch(T("Magnético"), _settings.MagneticTransform, v =>
            {
                _settings.MagneticTransform = v;
                if (_transform is not null) _transform.State.Magnetic = v;
            }),
            Switch(T("Interpolación suave"), _settings.SmoothTransform, v =>
            {
                _settings.SmoothTransform = v;
                if (_transform is not null) _transform.Smooth = v;
            }),
        ]));
        SheetBody.Children.Add(new TextBlock
        {
            Text = T("Arrastra para mover; dos dedos escalan y giran. En Distorsionar y Deformar, arrastra los puntos. Intro aplica."),
            Opacity = 0.6, FontSize = 12, TextWrapping = TextWrapping.Wrap,
        });
        OpenSheet(T("Transformar"));
    }

    /// <summary>Empieza a arrastrar un asa o un punto si el puntero cae encima; devuelve false si es un gesto normal.</summary>
    private bool TryBeginTransformHandle(Contact c)
    {
        if (_transform is null || _contacts.Count != 1) return false;
        var s = _transform.State;
        var r = HitRadius(c.Type);
        var screen = c.Position;
        if (s.Mode is TransformMode.Distort or TransformMode.Warp)
        {
            var pts = s.HandlePoints();
            var best = -1;
            var bestD = r;
            for (var i = 0; i < pts.Length; i++)
            {
                var d = Vector2.Distance(_view.ToScreen(pts[i]), screen);
                if (d < bestD) { best = i; bestD = d; }
            }
            if (best < 0) return false;
            _transformDrag = new TransformDrag(c.Id, null, best);
            return true;
        }
        var handles = s.AffineHandles(RotateHandleOffset);
        for (var i = handles.Length - 1; i >= 0; i--)
        {
            if (Vector2.Distance(_view.ToScreen(handles[i]), screen) >= r) continue;
            var h = (TransformHandle)i;
            s.BeginHandle(h, _view.ToDocument(screen));
            _transformDrag = new TransformDrag(c.Id, h, -1);
            return true;
        }
        return false;
    }

    private float RotateHandleOffset => 40 / _view.Scale;

    private void UpdateTransformHandle(Vector2 screen)
    {
        if (_transform is null || _transformDrag is not { } drag) return;
        var d = _view.ToDocument(screen);
        if (drag.Handle is not null) _transform.State.DragHandle(d);
        else _transform.State.MovePoint(drag.Point, d);
        Canvas.Invalidate();
    }

    private void EndTransformHandle()
    {
        _transform?.State.EndHandle();
        _transformDrag = null;
    }

    private void DrawTransformOverlay(CanvasDrawingSession ds)
    {
        if (_transform is null) return;
        var s = _transform.State;
        var dash = new CanvasStrokeStyle { DashStyle = CanvasDashStyle.Dash };
        void Handle(Vector2 p, bool square = false)
        {
            if (square) ds.FillRectangle(p.X - 5, p.Y - 5, 10, 10, Colors.White);
            else ds.FillCircle(p, 6, Colors.White);
            if (square) ds.DrawRectangle(p.X - 5, p.Y - 5, 10, 10, Accent, 1.5f);
            else ds.DrawCircle(p, 6, Accent, 1.5f);
        }
        // contorno de la forma: los bordes de la malla (curvos en Deformar)
        const int n = 16;
        var outline = new List<Vector2>();
        for (var i = 0; i < n; i++) outline.Add(s.Map(i / (float)n, 0));
        for (var i = 0; i < n; i++) outline.Add(s.Map(1, i / (float)n));
        for (var i = n; i > 0; i--) outline.Add(s.Map(i / (float)n, 1));
        for (var i = n; i > 0; i--) outline.Add(s.Map(0, i / (float)n));
        var screen = outline.Select(_view.ToScreen).ToArray();
        var halo = Color.FromArgb(140, 0, 0, 0);
        for (var i = 0; i < screen.Length; i++) ds.DrawLine(screen[i], screen[(i + 1) % screen.Length], halo, 3.5f);
        for (var i = 0; i < screen.Length; i++) ds.DrawLine(screen[i], screen[(i + 1) % screen.Length], Accent, 1.5f, dash);

        switch (s.Mode)
        {
            case TransformMode.Distort:
                foreach (var p in s.HandlePoints()) Handle(_view.ToScreen(p));
                break;
            case TransformMode.Warp:
                // líneas interiores de la malla y los 16 puntos de control
                for (var k = 1; k < 4; k++)
                {
                    var t = k / 4f;
                    for (var i = 0; i < n; i++)
                    {
                        foreach (var (w, c) in new[] { (3f, Color.FromArgb(120, 0, 0, 0)), (1f, Colors.White) })
                        {
                            ds.DrawLine(_view.ToScreen(s.Map(i / (float)n, t)), _view.ToScreen(s.Map((i + 1) / (float)n, t)), c, w);
                            ds.DrawLine(_view.ToScreen(s.Map(t, i / (float)n)), _view.ToScreen(s.Map(t, (i + 1) / (float)n)), c, w);
                        }
                    }
                }
                foreach (var p in s.HandlePoints()) Handle(_view.ToScreen(p));
                break;
            default:
                var h = s.AffineHandles(RotateHandleOffset).Select(_view.ToScreen).ToArray();
                if (!s.IsAffine)
                {
                    // la caja de las asas rodea la forma deformada
                    for (var i = 0; i < 4; i++) ds.DrawLine(h[i], h[(i + 1) % 4], Color.FromArgb(120, 255, 255, 255), 1, dash);
                }
                ds.DrawLine(h[(int)TransformHandle.Top], h[(int)TransformHandle.Rotate], Accent, 1.5f);
                for (var i = 0; i < 4; i++) Handle(h[i]);
                for (var i = 4; i < 8; i++) Handle(h[i], square: true);
                ds.FillCircle(h[8], 7, Accent);
                ds.DrawCircle(h[8], 7, Colors.White, 1.5f);
                break;
        }
    }

    // =============== Licuar ===============

    private LiquifySession? _liquify;
    private uint? _liquifyPointer;
    private Vector2 _liquifyPos;
    private float _liquifyPressure = 1;
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _liquifyTimer;

    private void StartLiquify()
    {
        if (_doc is null || _liquify is not null) return;
        if (_selectMode) ExitSelectMode();
        if (_transform is not null) ApplyTransform();
        if (_adjust is not null) CloseSheet();
        if (!_doc.ActiveLayer.Visible) { Window.ShowMessage(T("La capa activa está oculta")); return; }
        CancelInput();
        _liquify = LiquifySession.Start(_doc, _doc.ActiveLayer);
        _liquify.Mode = Enum.TryParse<LiquifyMode>(_settings.LiquifyMode, out var m) ? m : LiquifyMode.Push;
        _liquify.Size = _settings.LiquifySize;
        _liquify.Strength = _settings.LiquifyStrength;
        BuildLiquifySheet();
        OpenSheet(T("Licuar"));
    }

    private void BuildLiquifySheet()
    {
        if (_liquify is null) return;
        var l = _liquify;
        SheetBody.Children.Clear();
        SheetBody.Children.Add(ChoiceRow(
            [(LiquifyMode.Push, T("Empujar")), (LiquifyMode.TwirlRight, T("Girar ↻")), (LiquifyMode.TwirlLeft, T("Girar ↺")),
             (LiquifyMode.Pinch, T("Pellizcar")), (LiquifyMode.Expand, T("Expandir")), (LiquifyMode.Reconstruct, T("Reconstruir"))],
            () => l.Mode, v => { l.Mode = v; _settings.LiquifyMode = v.ToString(); _settings.Save(); }));
        SheetBody.Children.Add(SliderRow(T("Tamaño"), 10, 1000, l.Size, v => F("{0:0} px", v), v => { l.Size = (float)v; _settings.LiquifySize = (float)v; }));
        SheetBody.Children.Add(SliderRow(T("Presión"), 5, 100, l.Strength * 100, v => F("{0:0} %", v), v => { l.Strength = (float)v / 100; _settings.LiquifyStrength = (float)v / 100; }));
        SheetBody.Children.Add(SliderRow(T("Cantidad"), 0, 100, l.Amount * 100, v => F("{0:0} %", v), v => l.Amount = (float)v / 100));
        var reset = CompactButton(new Button { Content = T("Restablecer"), HorizontalAlignment = HorizontalAlignment.Right });
        reset.Click += (_, _) => { l.Reset(); BuildLiquifySheet(); Canvas.Invalidate(); };
        SheetBody.Children.Add(reset);
    }

    private void ApplyLiquify()
    {
        if (_liquify is null) return;
        _settings.Save();
        _liquify.Apply(_ops!);
        EndLiquify();
    }

    private void EndLiquify()
    {
        _liquifyTimer?.Stop();
        _liquifyPointer = null;
        _liquify?.Dispose();
        _liquify = null;
        Sheet.Visibility = Visibility.Collapsed;
        Canvas.Invalidate();
    }

    private bool TryBeginLiquify(PointerPoint pt, PointerDeviceType type, Contact c)
    {
        if (_liquify is null) return false;
        if (type == PointerDeviceType.Touch)
        {
            // un segundo dedo interrumpe el trazo y pasa a navegar
            if (Touches.Count() >= 2)
            {
                EndLiquifyStroke();
                return false;
            }
            if (PenOnly) return false;
        }
        if (type == PointerDeviceType.Mouse && (!pt.Properties.IsLeftButtonPressed || _spaceDown)) return false;
        _liquifyPointer = c.Id;
        _liquifyPos = _view.ToDocument(c.Position);
        _liquifyPressure = type == PointerDeviceType.Pen ? Math.Max(0.05f, pt.Properties.Pressure) : 1;
        _liquify.BeginStroke(_liquifyPos, _liquifyPressure);
        if (_liquifyTimer is null)
        {
            _liquifyTimer = DispatcherQueue.CreateTimer();
            _liquifyTimer.Interval = TimeSpan.FromMilliseconds(50);
            _liquifyTimer.Tick += (_, _) =>
            {
                // mantener quieto el pincel sigue girando, pellizcando o reconstruyendo
                if (_liquify is null || _liquifyPointer is null) return;
                _liquify.DabAt(_liquifyPos, _liquifyPressure);
                Canvas.Invalidate();
            };
        }
        if (_liquify.Mode != LiquifyMode.Push) _liquifyTimer.Start();
        Canvas.Invalidate();
        return true;
    }

    private void UpdateLiquify(PointerPoint pt, PointerDeviceType type, Vector2 screen)
    {
        if (_liquify is null) return;
        _liquifyPos = _view.ToDocument(screen);
        if (type == PointerDeviceType.Pen) _liquifyPressure = Math.Max(0.05f, pt.Properties.Pressure);
        _liquify.ContinueStroke(_liquifyPos, _liquifyPressure);
        _hover = screen;
        Canvas.Invalidate();
    }

    private void EndLiquifyStroke()
    {
        _liquifyTimer?.Stop();
        _liquifyPointer = null;
        _liquify?.EndStroke();
    }

    private void DrawLiquifyCursor(CanvasDrawingSession ds)
    {
        if (_liquify is null || _hover is not { } p) return;
        var r = _liquify.Size / 2 * _view.Scale;
        ds.DrawCircle(p, r + 0.75f, Color.FromArgb(140, 0, 0, 0), 1);
        ds.DrawCircle(p, r, Color.FromArgb(230, 255, 255, 255), 1.5f);
    }

    // =============== Guías de dibujo ===============

    private bool _guideEdit;
    private (uint Pointer, int Index, Vector2 Start, bool Added)? _guideDrag;

    private static readonly (string Hex, string Name)[] GuideColors = [("00E5FF", T("Cian")), ("FF4A1C", T("Rojo anaranjado")), ("000000", T("Negro")), ("FFFFFF", T("Blanco"))];

    private void ShowGuideSheet()
    {
        if (_doc is null) return;
        if (_selectMode) ExitSelectMode();
        if (_transform is not null) ApplyTransform();
        if (_adjust is not null) CloseSheet();
        if (_liquify is not null) ApplyLiquify();
        _guideEdit = true;
        BuildGuideSheet();
        OpenSheet(T("Guía de dibujo"));
    }

    private void SetGuide(DrawingGuide guide)
    {
        if (_doc is null) return;
        _doc.Guide = guide.Kind == GuideKind.None ? null : guide;
        _doc.IsDirty = true;
        _autosave.Stop();
        _autosave.Start();
        Canvas.Invalidate();
    }

    private DrawingGuide CurrentGuide => _doc?.Guide ?? new DrawingGuide();

    private void BuildGuideSheet()
    {
        SheetBody.Children.Clear();
        var g = CurrentGuide;
        var kinds = ChoiceRow(
            [(GuideKind.None, T("Ninguna")), (GuideKind.Grid, T("Cuadrícula 2D")), (GuideKind.Isometric, T("Isométrica")), (GuideKind.Perspective, T("Perspectiva"))],
            () => CurrentGuide.Kind,
            k =>
            {
                SetGuide(CurrentGuide with { Kind = k });
                DispatcherQueue.TryEnqueue(BuildGuideSheet);
            });
        SheetBody.Children.Add(kinds);
        if (g.Kind == GuideKind.None)
        {
            SheetBody.Children.Add(new TextBlock { Text = T("La guía de simetría está en Acciones › Lienzo."), Opacity = 0.6, FontSize = 12 });
            return;
        }
        if (g.Kind is GuideKind.Grid or GuideKind.Isometric)
            SheetBody.Children.Add(SliderRow(T("Tamaño"), 10, 1000, g.Size, v => F("{0:0} px", v), v => SetGuide(CurrentGuide with { Size = (float)Math.Round(v) })));
        SheetBody.Children.Add(SliderRow(T("Opacidad"), 5, 100, g.Opacity * 100, v => F("{0:0} %", v), v => SetGuide(CurrentGuide with { Opacity = (float)v / 100 })));
        SheetBody.Children.Add(SliderRow(T("Grosor"), 0.5, 6, g.Thickness, v => F("{0:0.0} px", v), v => SetGuide(CurrentGuide with { Thickness = (float)v })));
        SheetBody.Children.Add(ChoiceRow(GuideColors.Select(c => (c.Hex, c.Name)), () => CurrentGuide.Color, hex => SetGuide(CurrentGuide with { Color = hex })));
        var assist = new ToggleSwitch { IsOn = g.Assist, OnContent = T("Asistente de dibujo"), OffContent = T("Asistente de dibujo") };
        assist.Toggled += (_, _) => SetGuide(CurrentGuide with { Assist = assist.IsOn });
        SheetBody.Children.Add(assist);
        if (g.Kind == GuideKind.Perspective)
        {
            var clear = CompactButton(new Button { Content = T("Quitar los puntos de fuga"), HorizontalAlignment = HorizontalAlignment.Right });
            clear.Click += (_, _) => SetGuide(CurrentGuide with { VanishingPoints = [] });
            SheetBody.Children.Add(clear);
            SheetBody.Children.Add(new TextBlock
            {
                Text = T("Toca el lienzo para añadir un punto de fuga (hasta 3). Arrástralo para moverlo; tócalo sin moverlo para quitarlo."),
                Opacity = 0.6, FontSize = 12, TextWrapping = TextWrapping.Wrap,
            });
        }
    }

    private void EndGuideEdit()
    {
        _guideEdit = false;
        _guideDrag = null;
        Sheet.Visibility = Visibility.Collapsed;
        Canvas.Invalidate();
    }

    /// <summary>Con la perspectiva en edición: tocar añade, arrastrar mueve y tocar sin mover quita un punto de fuga.</summary>
    private bool TryBeginGuidePoint(PointerPoint pt, PointerDeviceType type, Contact c)
    {
        if (!_guideEdit || CurrentGuide.Kind != GuideKind.Perspective) return false;
        if (type == PointerDeviceType.Touch && Touches.Count() >= 2)
        {
            _guideDrag = null;
            return false;
        }
        if (type == PointerDeviceType.Mouse && (!pt.Properties.IsLeftButtonPressed || _spaceDown)) return false;
        var g = CurrentGuide;
        var vps = g.VanishingPoints.ToList();
        var r = HitRadius(type);
        var hit = vps.FindIndex(v => Vector2.Distance(_view.ToScreen(v), c.Position) < r);
        if (hit < 0)
        {
            if (vps.Count >= 3) return false;
            vps.Add(_view.ToDocument(c.Position));
            SetGuide(g with { VanishingPoints = vps });
            _guideDrag = (c.Id, vps.Count - 1, c.Position, true);
        }
        else _guideDrag = (c.Id, hit, c.Position, false);
        return true;
    }

    private void UpdateGuidePoint(Vector2 screen)
    {
        if (_guideDrag is not { } d) return;
        var vps = CurrentGuide.VanishingPoints.ToList();
        if (d.Index >= vps.Count) return;
        vps[d.Index] = _view.ToDocument(screen);
        SetGuide(CurrentGuide with { VanishingPoints = vps });
    }

    private void EndGuidePoint(Vector2 screen)
    {
        if (_guideDrag is not { } d) return;
        _guideDrag = null;
        // un toque sin arrastrar sobre un punto existente lo quita
        if (!d.Added && Vector2.Distance(screen, d.Start) < 6)
        {
            var vps = CurrentGuide.VanishingPoints.ToList();
            if (d.Index < vps.Count) vps.RemoveAt(d.Index);
            SetGuide(CurrentGuide with { VanishingPoints = vps });
        }
    }

    private void DrawGuide(CanvasDrawingSession ds, PaintDocument doc)
    {
        if (doc.Guide is not { } g) return;
        var rgb = Convert.ToInt32(g.Color, 16);
        var color = Color.FromArgb((byte)(255 * g.Opacity), (byte)(rgb >> 16), (byte)(rgb >> 8 & 255), (byte)(rgb & 255));
        foreach (var (a, b) in g.Lines(doc.Width, doc.Height))
            ds.DrawLine(_view.ToScreen(a), _view.ToScreen(b), color, g.Thickness);
        if (g.Kind == GuideKind.Perspective && (_guideEdit || g.VanishingPoints.Count > 0))
            foreach (var vp in g.VanishingPoints)
            {
                var p = _view.ToScreen(vp);
                ds.FillCircle(p, _guideEdit ? 9 : 4, Colors.White);
                ds.DrawCircle(p, _guideEdit ? 9 : 4, Accent, 2);
            }
    }

    /// <summary>Asistente para el trazo que empieza, si la guía lo tiene activado.</summary>
    private GuideAssist? NewAssist() =>
        _doc?.Guide is { Assist: true, IsVisible: true } g ? new GuideAssist(g, 6 / _view.Scale) : null;
}
