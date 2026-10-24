using System.Numerics;
using Microsoft.Graphics.Canvas;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Trazo.App.Painting;
using Trazo.Core.Strokes;
using Windows.System;
using DispatcherQueueTimer = Microsoft.UI.Dispatching.DispatcherQueueTimer;
using Windows.UI;
using Windows.UI.Core;

namespace Trazo.App.Views;

public sealed partial class StudioView
{
    private enum InputMode
    {
        Idle,
        Draw,
        Gesture,
        Eyedrop,
        Pan,
    }

    private sealed class Contact
    {
        public uint Id;
        public PointerDeviceType Type;
        public Vector2 Start;
        public Vector2 Position;
        public long T0;
        public float Moved;
    }

    private sealed class GestureState
    {
        public long T0;
        public int MaxTouches;
        public float Moved;
        public float StartScale;
        public bool RotationOn;
        public uint[] Ids = [];
        public float D0, A0, Scale0, Rot0;
        public Vector2 DocAnchor;
    }

    private readonly Dictionary<uint, Contact> _contacts = [];
    private InputMode _mode = InputMode.Idle;
    private uint _strokeId;
    private PointerDeviceType _strokeType;
    private long _strokeT0;
    private ToolKind? _toolBeforeOverride;
    private DispatcherQueueTimer? _holdTimer, _shapeTimer;
    private Vector2 _shapeAnchor;
    private GestureState? _gesture;
    private (uint Id, Vector2 Pos, Color Old, Color? Current, bool Touch)? _eyedrop;
    private (uint Id, Vector2 Start, Vector2 Offset)? _pan;
    private Vector2? _hover;
    private bool _penSeen, _eyedropOnce, _fillOnce, _spaceDown;

    private bool PenOnly => _settings.PenOnly == true;
    private static long Now => Environment.TickCount64;
    private IEnumerable<Contact> Touches => _contacts.Values.Where(c => c.Type == PointerDeviceType.Touch);

    private InputPoint ToInput(Microsoft.UI.Input.PointerPoint p, PointerDeviceType type)
    {
        var d = _view.ToDocument(new Vector2((float)p.Position.X, (float)p.Position.Y));
        float pressure = 1, tilt = 0;
        if (type == PointerDeviceType.Pen)
        {
            pressure = PressureCurve.Apply(p.Properties.Pressure, _settings.PressureGamma);
            var tx = p.Properties.XTilt;
            var ty = p.Properties.YTilt;
            if (tx != 0 || ty != 0) tilt = Math.Min(1, MathF.Sqrt(tx * tx + ty * ty) / 70);
        }
        return new InputPoint(d.X, d.Y, pressure, tilt);
    }

    private static Vector2 Pos(Microsoft.UI.Input.PointerPoint p) => new((float)p.Position.X, (float)p.Position.Y);

    // ================= Punteros =================

    private void Canvas_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (_doc is null) return;
        var pt = e.GetCurrentPoint(Canvas);
        var type = e.Pointer.PointerDeviceType;
        Canvas.CapturePointer(e.Pointer);
        Canvas.Focus(FocusState.Pointer);
        e.Handled = true;
        var c = new Contact { Id = e.Pointer.PointerId, Type = type, Start = Pos(pt), Position = Pos(pt), T0 = Now };
        _contacts[c.Id] = c;
        _hover = null;

        if (_transform is not null)
        {
            _transform.Anchor(_contacts.Values.Select(k => (k.Id, _view.ToDocument(k.Position))).ToList());
            return;
        }

        switch (type)
        {
            case PointerDeviceType.Pen:
                if (!_penSeen)
                {
                    _penSeen = true;
                    if (_settings.PenOnly is null)
                    {
                        _settings.PenOnly = true;
                        _settings.Save();
                        Window.ShowMessage("Lápiz detectado: el lápiz dibuja y el dedo navega. Puedes cambiarlo en Acciones › Preferencias.");
                    }
                }
                if (_mode == InputMode.Gesture) EndGesture();
                if (_mode == InputMode.Draw && _strokeType == PointerDeviceType.Touch) EndDraw(cancel: true);
                if (_mode != InputMode.Idle) return;
                if (_eyedropOnce || pt.Properties.IsBarrelButtonPressed) { StartEyedrop(c); return; }
                if (_fillOnce) { DoFill(c.Position); return; }
                StartDraw(pt, type, pt.Properties.IsEraser || pt.Properties.IsInverted ? ToolKind.Eraser : null);
                return;

            case PointerDeviceType.Mouse:
                if (_mode != InputMode.Idle) return;
                var props = pt.Properties;
                if (props.IsMiddleButtonPressed || (props.IsLeftButtonPressed && _spaceDown))
                {
                    _pan = (c.Id, c.Position, _view.Offset);
                    _mode = InputMode.Pan;
                    return;
                }
                if (props.IsRightButtonPressed || (props.IsLeftButtonPressed && (_eyedropOnce || IsKeyDown(VirtualKey.Menu)))) { StartEyedrop(c); return; }
                if (props.IsLeftButtonPressed)
                {
                    if (_fillOnce) { DoFill(c.Position); return; }
                    StartDraw(pt, type, null);
                }
                return;
        }

        // ---- Táctil ----
        var touches = Touches.Count();
        if (_mode == InputMode.Draw && _strokeType == PointerDeviceType.Pen) return; // rechazo de palma
        if (_mode == InputMode.Eyedrop)
        {
            if (touches >= 2) EndEyedrop(apply: false);
            else return;
        }
        if (_mode == InputMode.Draw && _strokeType == PointerDeviceType.Touch)
        {
            // un segundo dedo al poco de empezar convierte el trazo en gesto
            if (Now - _strokeT0 < 280) EndDraw(cancel: true);
            else return;
        }
        if (_mode == InputMode.Gesture)
        {
            AnchorGesture();
            return;
        }
        if (touches == 1)
        {
            if (_eyedropOnce) { StartEyedrop(c); return; }
            if (_fillOnce) { DoFill(c.Position); return; }
            if (PenOnly || _adjust is not null) { StartGesture(); return; }
            StartDraw(pt, type, null);
            return;
        }
        StartGesture();
    }

    private void Canvas_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        var pt = e.GetCurrentPoint(Canvas);
        if (!_contacts.TryGetValue(e.Pointer.PointerId, out var c))
        {
            if (e.Pointer.PointerDeviceType != PointerDeviceType.Touch && _doc is not null)
            {
                _hover = Pos(pt);
                if (_settings.BrushCursor) Canvas.Invalidate();
            }
            return;
        }
        c.Position = Pos(pt);
        c.Moved = Math.Max(c.Moved, Vector2.Distance(c.Position, c.Start));
        e.Handled = true;

        if (_transform is not null)
        {
            _transform.Move(_contacts.ToDictionary(k => k.Key, k => _view.ToDocument(k.Value.Position)));
            Canvas.Invalidate();
            return;
        }

        switch (_mode)
        {
            case InputMode.Draw when c.Id == _strokeId && _engine?.IsActive == true:
                // puntos intermedios: el lápiz informa a más frecuencia que la pantalla
                var pts = e.GetIntermediatePoints(Canvas);
                for (var i = pts.Count - 1; i >= 0; i--) _engine.Add(ToInput(pts[i], c.Type));
                if (Vector2.Distance(c.Position, _shapeAnchor) > 4)
                {
                    _shapeAnchor = c.Position;
                    if (_engine.Shape is null) ArmShapeTimer();
                }
                if (c.Moved > 10) _holdTimer?.Stop();
                if (c.Type != PointerDeviceType.Touch) _hover = c.Position;
                Canvas.Invalidate();
                break;
            case InputMode.Gesture when c.Type == PointerDeviceType.Touch:
                _gesture!.Moved = Math.Max(_gesture.Moved, c.Moved);
                MoveGesture();
                break;
            case InputMode.Eyedrop when _eyedrop?.Id == c.Id:
                SampleEyedrop(c.Position);
                break;
            case InputMode.Pan when _pan?.Id == c.Id:
                _view.Offset = _pan.Value.Offset + c.Position - _pan.Value.Start;
                Canvas.Invalidate();
                break;
        }
    }

    private void Canvas_PointerReleased(object sender, PointerRoutedEventArgs e) => Release(e, cancelled: false);
    private void Canvas_PointerCanceled(object sender, PointerRoutedEventArgs e) => Release(e, cancelled: true);

    private void Canvas_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (_contacts.ContainsKey(e.Pointer.PointerId)) return;
        _hover = null;
        Canvas.Invalidate();
    }

    private void Release(PointerRoutedEventArgs e, bool cancelled)
    {
        if (!_contacts.Remove(e.Pointer.PointerId, out var c)) return;
        Canvas.ReleasePointerCapture(e.Pointer);
        if (_transform is not null)
        {
            _transform.Anchor(_contacts.Values.Select(k => (k.Id, _view.ToDocument(k.Position))).ToList());
            return;
        }
        switch (_mode)
        {
            case InputMode.Draw when c.Id == _strokeId:
                EndDraw(cancelled);
                break;
            case InputMode.Eyedrop when _eyedrop?.Id == c.Id:
                EndEyedrop(!cancelled);
                break;
            case InputMode.Pan when _pan?.Id == c.Id:
                _pan = null;
                _mode = InputMode.Idle;
                break;
            case InputMode.Gesture:
                if (!Touches.Any()) EndGesture();
                else AnchorGesture();
                break;
        }
    }

    private void CancelInput()
    {
        if (_mode == InputMode.Draw) EndDraw(cancel: true);
        if (_mode == InputMode.Eyedrop) EndEyedrop(apply: false);
        _gesture = null;
        _pan = null;
        _contacts.Clear();
        _mode = InputMode.Idle;
    }

    // ================= Dibujo =================

    private void StartDraw(Microsoft.UI.Input.PointerPoint pt, PointerDeviceType type, ToolKind? overrideTool)
    {
        if (_doc is null || _engine is null || _adjust is not null) return;
        if (overrideTool is { } o && o != Tool)
        {
            _toolBeforeOverride = Tool;
            _settings.Tool = o.ToString();
            RefreshToolUi();
        }
        var tool = Tool;
        var st = ToolState(tool);
        var ok = _engine.Begin(ToInput(pt, type), tool, BrushFor(tool), CurrentColor, st.Size, st.Opacity, _settings.Symmetry, _view.Scale);
        if (!ok)
        {
            RestoreTool();
            Window.ShowMessage("La capa activa está oculta");
            return;
        }
        _mode = InputMode.Draw;
        _strokeId = pt.PointerId;
        _strokeType = type;
        _strokeT0 = Now;
        _shapeAnchor = Pos(pt);
        ArmShapeTimer();
        if (type == PointerDeviceType.Touch)
        {
            if (_holdTimer is null)
            {
                _holdTimer = DispatcherQueue.CreateTimer();
                _holdTimer.Interval = TimeSpan.FromMilliseconds(450);
                _holdTimer.IsRepeating = false;
                _holdTimer.Tick += HoldTick;
            }
            _holdTimer.Start();
        }
        Canvas.Invalidate();
    }

    // tocar y mantener sin moverse = cuentagotas
    private void HoldTick(DispatcherQueueTimer sender, object args)
    {
        if (_mode != InputMode.Draw || !_contacts.TryGetValue(_strokeId, out var c) || c.Moved >= 10) return;
        _engine?.Cancel();
        _mode = InputMode.Idle;
        RestoreTool();
        StartEyedrop(c);
    }

    private void ArmShapeTimer()
    {
        _shapeTimer ??= CreateShapeTimer();
        _shapeTimer.Stop();
        if (_settings.QuickShape) _shapeTimer.Start();
    }

    private DispatcherQueueTimer CreateShapeTimer()
    {
        var t = DispatcherQueue.CreateTimer();
        t.Interval = TimeSpan.FromMilliseconds(600);
        t.IsRepeating = false;
        t.Tick += (_, _) =>
        {
            if (_mode == InputMode.Draw && Now - _strokeT0 > 500 && _engine?.TrySnapShape(_view.Scale) == true)
            {
                _holdTimer?.Stop();
                Canvas.Invalidate();
            }
        };
        return t;
    }

    private void EndDraw(bool cancel)
    {
        _holdTimer?.Stop();
        _shapeTimer?.Stop();
        if (_engine?.IsActive == true)
        {
            if (cancel) _engine.Cancel();
            else
            {
                _engine.End();
                if (Tool == ToolKind.Brush) PushRecentColor(CurrentColor);
            }
        }
        _mode = InputMode.Idle;
        RestoreTool();
        Canvas.Invalidate();
    }

    private void RestoreTool()
    {
        if (_toolBeforeOverride is not { } t) return;
        _toolBeforeOverride = null;
        _settings.Tool = t.ToString();
        RefreshToolUi();
    }

    private void DoFill(Vector2 screen)
    {
        _fillOnce = false;
        var d = _view.ToDocument(screen);
        var color = CurrentColor;
        if (_engine?.Fill(d.X, d.Y, color, _settings.FillTolerance) == true) PushRecentColor(color);
        else if (_doc?.ActiveLayer.Visible == false) Window.ShowMessage("La capa activa está oculta");
        Canvas.Invalidate();
    }

    // ================= Cuentagotas =================

    private void StartEyedrop(Contact c)
    {
        _mode = InputMode.Eyedrop;
        _eyedrop = (c.Id, c.Position, CurrentColor, null, c.Type == PointerDeviceType.Touch);
        SampleEyedrop(c.Position);
    }

    private void SampleEyedrop(Vector2 screen)
    {
        if (_eyedrop is not { } e || _doc is null) return;
        var d = _view.ToDocument(screen);
        var color = _doc.SampleColor(d.X, d.Y, null) ?? e.Current;
        _eyedrop = e with { Pos = screen, Current = color };
        Canvas.Invalidate();
    }

    private void EndEyedrop(bool apply)
    {
        if (apply && _eyedrop?.Current is { } c) CurrentColor = c;
        _eyedrop = null;
        _eyedropOnce = false;
        EyedropperButton.IsChecked = false;
        _mode = InputMode.Idle;
        Canvas.Invalidate();
    }

    private void DrawEyedropper(CanvasDrawingSession ds)
    {
        if (_eyedrop is not { } e) return;
        var center = e.Pos - new Vector2(0, e.Touch ? 90 : 0);
        const float r = 46;
        using var top = Microsoft.Graphics.Canvas.Geometry.CanvasGeometry.CreatePath(Arc(ds, center, r, MathF.PI, 0));
        using var bottom = Microsoft.Graphics.Canvas.Geometry.CanvasGeometry.CreatePath(Arc(ds, center, r, 0, MathF.PI));
        ds.DrawGeometry(top, e.Current ?? e.Old, 16);
        ds.DrawGeometry(bottom, e.Old, 16);
        ds.DrawCircle(center, r + 8, Colors.White, 1.5f);
        ds.DrawCircle(center, r - 8, Colors.White, 1.5f);
    }

    private static Microsoft.Graphics.Canvas.Geometry.CanvasPathBuilder Arc(ICanvasResourceCreator rc, Vector2 c, float r, float start, float end)
    {
        var pb = new Microsoft.Graphics.Canvas.Geometry.CanvasPathBuilder(rc);
        pb.BeginFigure(c + new Vector2(MathF.Cos(start), MathF.Sin(start)) * r);
        pb.AddArc(c, r, r, start, end - start + (end < start ? MathF.Tau : 0));
        pb.EndFigure(Microsoft.Graphics.Canvas.Geometry.CanvasFigureLoop.Open);
        return pb;
    }

    // ================= Gestos =================

    private void StartGesture()
    {
        _gesture ??= new GestureState { T0 = Touches.Min(t => t.T0), StartScale = _view.Scale };
        _mode = InputMode.Gesture;
        AnchorGesture();
    }

    private void AnchorGesture()
    {
        var g = _gesture!;
        var ts = Touches.Take(2).ToArray();
        g.MaxTouches = Math.Max(g.MaxTouches, Touches.Count());
        if (ts.Length == 0) return;
        g.Ids = ts.Select(t => t.Id).ToArray();
        var center = ts.Length == 2 ? (ts[0].Position + ts[1].Position) / 2 : ts[0].Position;
        g.D0 = ts.Length == 2 ? Vector2.Distance(ts[0].Position, ts[1].Position) : 0;
        g.A0 = ts.Length == 2 ? MathF.Atan2(ts[1].Position.Y - ts[0].Position.Y, ts[1].Position.X - ts[0].Position.X) : 0;
        g.Scale0 = _view.Scale;
        g.Rot0 = _view.Rotation;
        g.DocAnchor = _view.ToDocument(center);
    }

    private void MoveGesture()
    {
        var g = _gesture!;
        var ts = g.Ids.Select(id => _contacts.GetValueOrDefault(id)).Where(c => c is not null).ToArray();
        if (ts.Length == 0) return;
        var center = ts.Length == 2 ? (ts[0]!.Position + ts[1]!.Position) / 2 : ts[0]!.Position;
        if (ts.Length == 2 && g.D0 > 0)
        {
            var d = Vector2.Distance(ts[0]!.Position, ts[1]!.Position);
            _view.Scale = Math.Clamp(g.Scale0 * d / g.D0, Core.Geometry.ViewTransform.MinScale, Core.Geometry.ViewTransform.MaxScale);
            if (_settings.RotateGesture)
            {
                var a = MathF.Atan2(ts[1]!.Position.Y - ts[0]!.Position.Y, ts[1]!.Position.X - ts[0]!.Position.X) - g.A0;
                a = MathF.Atan2(MathF.Sin(a), MathF.Cos(a));
                if (!g.RotationOn && MathF.Abs(a) > 0.2f)
                {
                    // al activar la rotación se reancla el ángulo para evitar un salto
                    g.RotationOn = true;
                    g.A0 += a;
                    a = 0;
                }
                if (g.RotationOn) _view.Rotation = g.Rot0 + a;
            }
        }
        _view.Pin(g.DocAnchor, center);
        Canvas.Invalidate();
    }

    private void EndGesture()
    {
        var g = _gesture;
        _gesture = null;
        _mode = InputMode.Idle;
        if (g is null) return;
        var dur = Now - g.T0;
        if (dur < 320 && g.Moved < 18)
        {
            switch (g.MaxTouches)
            {
                case 2: Undo(); break;
                case 3: Redo(); break;
                case >= 4: ToggleFullscreen(); break;
            }
            return;
        }
        if (dur < 450 && _view.Scale < g.StartScale * 0.62f)
        {
            FitToScreen(); // pellizco rápido
            return;
        }
        if (_view.SnapRotation(ViewSize / 2)) Canvas.Invalidate();
    }

    // ================= Rueda =================

    private void Canvas_PointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        if (_doc is null) return;
        e.Handled = true;
        var pt = e.GetCurrentPoint(Canvas);
        var delta = pt.Properties.MouseWheelDelta;
        var pos = Pos(pt);
        var mods = e.KeyModifiers;
        if (_transform is not null && !mods.HasFlag(VirtualKeyModifiers.Control))
        {
            var d = _view.ToDocument(pos);
            if (mods.HasFlag(VirtualKeyModifiers.Menu)) _transform.RotateAt(d, delta > 0 ? -5 : 5);
            else _transform.ScaleAt(d, delta > 0 ? 1.08f : 1 / 1.08f);
        }
        else if (pt.Properties.IsHorizontalMouseWheel) _view.Offset -= new Vector2(delta, 0);
        else if (mods.HasFlag(VirtualKeyModifiers.Menu)) _view.RotateAt(pos, (delta > 0 ? -1 : 1) * MathF.PI / 36);
        else if (mods.HasFlag(VirtualKeyModifiers.Shift)) _view.Offset += new Vector2(delta, 0);
        else _view.ZoomAt(pos, MathF.Pow(1.15f, delta / 120f));
        Canvas.Invalidate();
    }

    // ================= Teclado =================

    private static bool IsKeyDown(VirtualKey key) =>
        InputKeyboardSource.GetKeyStateForCurrentThread(key).HasFlag(CoreVirtualKeyStates.Down);

    private void OnPreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (_doc is null || e.OriginalSource is TextBox or NumberBox or AutoSuggestBox) return;
        var ctrl = IsKeyDown(VirtualKey.Control);
        var shift = IsKeyDown(VirtualKey.Shift);
        var handled = true;
        if (ctrl)
        {
            switch (e.Key)
            {
                case VirtualKey.Z when shift: Redo(); break;
                case VirtualKey.Z: Undo(); break;
                case VirtualKey.Y: Redo(); break;
                case VirtualKey.S: _doc.IsDirty = true; _ = SaveAndNotifyAsync(); break;
                case VirtualKey.Number0: FitToScreen(); break;
                case VirtualKey.Number1: ShowActualSize(); break;
                case VirtualKey.J: _ops?.Duplicate(_doc.ActiveIndex); break;
                case VirtualKey.E: _ops?.MergeDown(_doc.ActiveIndex); break;
                case (VirtualKey)187: case VirtualKey.Add: _view.ZoomAt(ViewSize / 2, 1.25f); Canvas.Invalidate(); break;
                case (VirtualKey)189: case VirtualKey.Subtract: _view.ZoomAt(ViewSize / 2, 0.8f); Canvas.Invalidate(); break;
                default: handled = false; break;
            }
            e.Handled = handled;
            return;
        }
        if (_transform is not null)
        {
            if (e.Key == VirtualKey.Enter) ApplyTransform();
            else if (e.Key == VirtualKey.Escape) CancelTransform();
            else handled = false;
            e.Handled = handled;
            return;
        }
        switch (e.Key)
        {
            case VirtualKey.Space: _spaceDown = true; break;
            case VirtualKey.B: Tool = ToolKind.Brush; break;
            case VirtualKey.E: Tool = ToolKind.Eraser; break;
            case VirtualKey.S: Tool = ToolKind.Smudge; break;
            case VirtualKey.I: _eyedropOnce = true; EyedropperButton.IsChecked = true; break;
            case VirtualKey.G: _fillOnce = true; Window.ShowMessage("Toca el lienzo para rellenar"); break;
            case VirtualKey.V: StartTransform(); break;
            case VirtualKey.L: ShowLayersPanel(); break;
            case VirtualKey.C: ShowColorPanel(); break;
            case (VirtualKey)219: SetSize(ToolState(Tool).Size / 1.15f); break; // [
            case (VirtualKey)221: SetSize(ToolState(Tool).Size * 1.15f); break; // ]
            case VirtualKey.Number0: FitToScreen(); break;
            case VirtualKey.R: _view.RotateAt(ViewSize / 2, -_view.Rotation); Canvas.Invalidate(); break;
            case VirtualKey.X:
                var prev = _previousColor;
                if (Core.Colors.Rgba.TryParseHex(prev, out var pc)) CurrentColor = PaintDocument.ToColor(pc);
                break;
            case VirtualKey.Tab: ToggleFullscreen(); break;
            case VirtualKey.Delete: case VirtualKey.Back: _ops?.Clear(_doc.ActiveIndex); break;
            case >= VirtualKey.Number1 and <= VirtualKey.Number9: SetOpacity((e.Key - VirtualKey.Number0) / 10f); break;
            default: handled = false; break;
        }
        e.Handled = handled;
    }

    private void OnKeyUp(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Space) _spaceDown = false;
    }

    private async Task SaveAndNotifyAsync()
    {
        await SaveAsync();
        Window.ShowMessage("Guardado");
    }
}
