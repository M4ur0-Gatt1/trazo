using System.Numerics;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Effects;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Trazo.Rendering;
using Trazo.App.Services;
using Trazo.Core.Brushes;
using Trazo.Core.Colors;
using Trazo.Core.Geometry;
using Trazo.Core.IO;
using Trazo.Core.Strokes;
using Windows.Foundation;
using Windows.UI;
using static Trazo.Core.Localization.Tr;

namespace Trazo.App.Views;

/// <summary>Estudio de pintura: lienzo, herramientas y paneles.</summary>
public sealed partial class StudioView : UserControl
{
    private readonly Settings _settings = Settings.Current;
    private readonly BrushLibrary _brushes = new();
    private readonly ViewTransform _view = new();
    private readonly DispatcherTimer _autosave = new() { Interval = TimeSpan.FromSeconds(3) };
    private readonly SemaphoreSlim _saveLock = new(1, 1);
    private TipCache? _tips;
    private readonly CompositionCache _composition = new();
    private GrainCache? _grains;
    private PaintDocument? _doc;
    private LayerOperations? _ops;
    private BrushEngine? _engine;
    private AdjustmentSession? _adjust;
    private TransformSession? _transform;
    private CanvasBitmap? _checker;
    private bool _fitPending;
    private bool _updatingSliders = true; // evita eventos de los sliders mientras se construye el XAML

    public event Action? CloseRequested;

    public StudioView()
    {
        InitializeComponent();
        _updatingSliders = false;
        _settings.ApplyBrushOverrides(_brushes);
        _autosave.Tick += async (_, _) => { _autosave.Stop(); await SaveAsync(); };
        BuildAdjustMenu();
        Loaded += (_, _) => ApplyLayoutPreferences();
        SizeChanged += (_, e) =>
        {
            // mantener centrado el contenido al girar la tableta
            if (e.PreviousSize.Width > 0) _view.Offset += new Vector2((float)(e.NewSize.Width - e.PreviousSize.Width) / 2, (float)(e.NewSize.Height - e.PreviousSize.Height) / 2);
            Canvas.Invalidate();
        };
        ColorButton.AddHandler(PointerPressedEvent, new Microsoft.UI.Xaml.Input.PointerEventHandler(ColorButton_PointerPressed), true);
        ColorButton.AddHandler(PointerMovedEvent, new Microsoft.UI.Xaml.Input.PointerEventHandler(ColorButton_PointerMoved), true);
        ColorButton.AddHandler(PointerReleasedEvent, new Microsoft.UI.Xaml.Input.PointerEventHandler(ColorButton_PointerReleased), true);
    }

    private static MainWindow Window => App.Window!;
    public bool HasUnsavedChanges => _doc?.IsDirty == true;

    // ================= Estado de herramientas =================

    private ToolKind Tool
    {
        get => Enum.TryParse<ToolKind>(_settings.Tool, out var t) ? t : ToolKind.Brush;
        set
        {
            _settings.Tool = value.ToString();
            _settings.Save();
            RefreshToolUi();
        }
    }

    private ToolSettings ToolState(ToolKind t) => t switch
    {
        ToolKind.Smudge => _settings.SmudgeTool,
        ToolKind.Eraser => _settings.EraserTool,
        _ => _settings.BrushTool,
    };

    private BrushPreset BrushFor(ToolKind t) =>
        _brushes.TryGet(ToolState(t).Brush, out var b) ? b : _brushes["roundbrush"];

    private Color CurrentColor
    {
        get => Rgba.TryParseHex(_settings.Color, out var c) ? PaintDocument.ToColor(c) : Colors.Black;
        set
        {
            var hex = PaintDocument.ToRgba(value).ToHex();
            if (hex == _settings.Color) return;
            _previousColor = _settings.Color;
            _settings.Color = hex;
            _settings.Save();
            ColorSwatch.Fill = new SolidColorBrush(value);
        }
    }

    private string _previousColor = "#ffffff";

    private void PushRecentColor(Color c)
    {
        var hex = PaintDocument.ToRgba(c).ToHex();
        _settings.RecentColors.Remove(hex);
        _settings.RecentColors.Insert(0, hex);
        if (_settings.RecentColors.Count > 12) _settings.RecentColors.RemoveRange(12, _settings.RecentColors.Count - 12);
        _settings.Save();
    }

    private void RefreshToolUi()
    {
        var t = Tool;
        BrushButton.IsChecked = t == ToolKind.Brush;
        SmudgeButton.IsChecked = t == ToolKind.Smudge;
        EraserButton.IsChecked = t == ToolKind.Eraser;
        ColorSwatch.Fill = new SolidColorBrush(CurrentColor);
        RefreshSliders();
    }

    // Tamaño en escala cuadrática para tener precisión en pinceles pequeños
    private void RefreshSliders()
    {
        _updatingSliders = true;
        var st = ToolState(Tool);
        var b = BrushFor(Tool);
        var t = MathF.Sqrt(Math.Clamp((st.Size - b.MinSize) / (b.MaxSize - b.MinSize), 0, 1));
        SizeSlider.Value = t * 1000;
        OpacitySlider.Value = Math.Round(st.Opacity * 100);
        SizeLabel.Text = st.Size < 10 ? $"{st.Size:0.0}" : $"{st.Size:0}";
        OpacityLabel.Text = F("{0:0} %", st.Opacity * 100);
        _updatingSliders = false;
    }

    private void SetSize(float px)
    {
        var b = BrushFor(Tool);
        ToolState(Tool).Size = Math.Clamp(px, b.MinSize, b.MaxSize);
        _settings.Save();
        RefreshSliders();
        Canvas.Invalidate();
    }

    private void SetOpacity(float v)
    {
        ToolState(Tool).Opacity = Math.Clamp(v, 0.01f, 1);
        _settings.Save();
        RefreshSliders();
    }

    private void SizeSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (_updatingSliders) return;
        var b = BrushFor(Tool);
        var t = (float)e.NewValue / 1000;
        ToolState(Tool).Size = b.MinSize + (b.MaxSize - b.MinSize) * t * t;
        _settings.Save();
        var s = ToolState(Tool).Size;
        SizeLabel.Text = s < 10 ? $"{s:0.0}" : $"{s:0}";
        _cursorPreviewUntil = DateTime.UtcNow.AddSeconds(1);
        Canvas.Invalidate();
    }

    private void OpacitySlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (_updatingSliders) return;
        ToolState(Tool).Opacity = (float)e.NewValue / 100;
        OpacityLabel.Text = F("{0:0} %", e.NewValue);
        _settings.Save();
    }

    private DateTime _cursorPreviewUntil;

    private void ApplyLayoutPreferences()
    {
        SideBar.HorizontalAlignment = _settings.LeftHanded ? HorizontalAlignment.Right : HorizontalAlignment.Left;
        SideBar.CornerRadius = _settings.LeftHanded ? new CornerRadius(16, 0, 0, 16) : new CornerRadius(0, 16, 16, 0);
    }

    // ================= Documento =================

    public void Open(PaintDocument doc)
    {
        _doc?.Dispose();
        _doc = doc;
        WatchDevice(doc.Device);
        App.Journal.Begin(doc.FilePath, DateTimeOffset.Now);
        _lastSaved = doc.FilePath is { } existing && File.Exists(existing) && !doc.IsDirty ? File.GetLastWriteTime(existing) : null;
        _tips ??= new TipCache(doc.Device);
        _grains ??= new GrainCache(doc.Device);
        _ops = new LayerOperations(doc);
        _ops.Message += m => Window.ShowMessage(m);
        _engine = new BrushEngine(doc, _ops, _tips, _grains);
        doc.History.Changed += (_, _) => OnDocumentChanged();
        doc.Layers.CollectionChanged += (_, _) => OnDocumentChanged(markDirty: false);
        StartTimelapse(doc);
        RefreshToolUi();
        RefreshHistoryButtons();
        // el ajuste a pantalla espera a que el lienzo tenga tamaño (ver Canvas_Draw)
        _fitPending = true;
        Canvas.Invalidate();
        DispatcherQueue.TryEnqueue(() => Canvas.Focus(FocusState.Programmatic));
    }

    private void OnDocumentChanged(bool markDirty = true)
    {
        if (_doc is null) return;
        if (markDirty) _doc.IsDirty = true;
        RefreshHistoryButtons();
        RefreshLayersPanel();
        _autosave.Stop();
        _autosave.Start();
        Canvas.Invalidate();
    }

    private void RefreshHistoryButtons()
    {
        UndoButton.IsEnabled = _doc?.History.CanUndo == true;
        RedoButton.IsEnabled = _doc?.History.CanRedo == true;
    }

    public async Task SaveAsync()
    {
        var doc = _doc;
        if (doc?.FilePath is null || !doc.IsDirty) return;
        await _saveLock.WaitAsync();
        try
        {
            doc.IsDirty = false;
            var data = await doc.ToProjectDataAsync();
            await ProjectSaver.SaveAsync(doc.FilePath, data);
            _lastSaved = DateTimeOffset.Now;
            App.Journal.MarkSaved(_lastSaved.Value, doc.FilePath);
        }
        catch (SaveFailedException e)
        {
            // el archivo anterior sigue intacto: se reintentará en el próximo autoguardado
            doc.IsDirty = true;
            Log.Error($"Guardado fallido en la etapa {e.Stage}", e);
            Window.ShowMessage(e.Message, InfoBarSeverity.Error);
        }
        catch (Exception e)
        {
            doc.IsDirty = true;
            Log.Error("Error al preparar el guardado", e);
            Window.ShowMessage(T("No se pudo guardar: ") + e.Message + T(" El archivo anterior sigue intacto."), InfoBarSeverity.Error);
        }
        finally
        {
            _saveLock.Release();
        }
    }

    public async Task CloseAsync()
    {
        CancelInput();
        if (_transform is not null) ApplyTransform();
        if (_adjust is not null) CloseSheet();
        if (_liquify is not null) ApplyLiquify();
        if (_guideEdit) EndGuideEdit();
        _autosave.Stop();
        await SaveAsync();
        // el último cambio también entra en el time-lapse
        _timelapse?.Flush();
        StopTimelapse();
        _doc?.Dispose();
        _doc = null;
        _composition.Dispose();
        App.Journal.End();
        Canvas.Invalidate();
    }

    private void Gallery_Click(object sender, RoutedEventArgs e) => CloseRequested?.Invoke();

    private void Undo()
    {
        if (_doc is null || _transform is not null || _adjust is not null || _liquify is not null) return;
        CancelInput();
        var a = _doc.History.Undo();
        Window.ShowMessage(a is null ? T("Nada que deshacer") : T("Deshacer: ") + a.Label);
    }

    private void Redo()
    {
        if (_doc is null || _transform is not null || _adjust is not null || _liquify is not null) return;
        var a = _doc.History.Redo();
        Window.ShowMessage(a is null ? T("Nada que rehacer") : T("Rehacer: ") + a.Label);
    }

    private void Undo_Click(object sender, RoutedEventArgs e) => Undo();
    private void Redo_Click(object sender, RoutedEventArgs e) => Redo();

    // ================= Vista =================

    private float TopInset => TopBar.Visibility == Visibility.Visible ? 52 : 0;
    private Vector2 ViewSize => new((float)Canvas.ActualWidth, (float)Canvas.ActualHeight);

    private void FitToScreen()
    {
        if (_doc is null) return;
        _view.Fit(new Vector2(_doc.Width, _doc.Height), ViewSize, 28, TopInset);
        Canvas.Invalidate();
    }

    private void ShowActualSize()
    {
        var c = _view.ToDocument(ViewSize / 2);
        _view.Scale = 1;
        _view.Pin(c, ViewSize / 2);
        Canvas.Invalidate();
    }

    private void ToggleFullscreen()
    {
        var hide = TopBar.Visibility == Visibility.Visible;
        TopBar.Visibility = SideBar.Visibility = hide ? Visibility.Collapsed : Visibility.Visible;
    }

    // ================= Renderizado =================

    private void Canvas_Draw(CanvasControl sender, CanvasDrawEventArgs args)
    {
        var doc = _doc;
        if (doc is null) return;
        if (_fitPending && sender.ActualWidth > 0 && sender.ActualHeight > 0)
        {
            _fitPending = false;
            _view.Fit(new Vector2(doc.Width, doc.Height), ViewSize, 28, TopInset);
        }
        var ds = args.DrawingSession;
        var owned = new List<IDisposable>();
        try
        {
            // la clave es la capa o su máscara (MaskKey), según dónde se esté pintando
            var overrides = new Dictionary<object, ICanvasImage>();
            if (_engine?.Preview(owned) is { } stroke && _engine.PreviewKey is { } key) overrides[key] = stroke;
            if (_adjust is not null) overrides[_adjust.TargetKey] = _adjust.Preview(owned);
            if (_transform is not null) overrides[_transform.TargetKey] = _transform.Preview(owned);
            if (_liquify is not null) overrides[_liquify.TargetKey] = _liquify.Preview(owned);

            ds.Transform = _view.Matrix;
            // sombra del lienzo
            using (var shadowSrc = new CanvasCommandList(sender))
            {
                using (var cds = shadowSrc.CreateDrawingSession()) cds.FillRectangle(doc.Bounds, Colors.Black);
                using var shadow = new ShadowEffect { Source = shadowSrc, BlurAmount = ViewEffects.ShadowBlur(_view.Scale), ShadowColor = Color.FromArgb(120, 0, 0, 0) };
                ds.DrawImage(shadow);
            }
            if (!doc.BackgroundVisible) DrawChecker(ds, doc, owned);

            var interp = _view.Scale >= 2.5f ? CanvasImageInterpolation.NearestNeighbor
                : _view.Scale < 0.5f ? CanvasImageInterpolation.HighQualityCubic
                : CanvasImageInterpolation.Linear;
            // PF-01: la parte que no cambia se compone una vez y se reutiliza en cada fotograma
            ds.DrawImage(_composition.Compose(doc, overrides, owned), Vector2.Zero, doc.Bounds, 1, interp);
            DrawSelectionOverlay(ds, doc, owned);
            ds.Transform = Matrix3x2.Identity;
            DrawSelectionDrag(ds);

            DrawGuide(ds, doc);
            DrawSymmetryGuides(ds, doc);
            DrawTransformOverlay(ds);
            DrawLiquifyCursor(ds);
            DrawShapeLabel(ds);
            DrawEyedropper(ds);
            DrawBrushCursor(ds);
        }
        catch (Exception e) when (sender.Device.IsDeviceLost(e.HResult))
        {
            Log.Error("Se perdió el dispositivo gráfico", e);
            sender.Device.RaiseDeviceLost();
        }
        catch (Exception e)
        {
            // Una excepción que sale de Draw deja abierta la sesión de dibujo del control y el lienzo
            // ya no vuelve a redibujarse. Se registra (una vez por tipo de error) y se pierde solo este fotograma.
            if (_drawErrors.Add(e.GetType().FullName + e.Message)) Log.Error("Error al dibujar un fotograma", e);
        }
        finally
        {
            foreach (var o in owned) o.Dispose();
        }
    }

    private readonly HashSet<string> _drawErrors = [];

    private void DrawChecker(CanvasDrawingSession ds, PaintDocument doc, List<IDisposable> owned)
    {
        if (_checker is null)
        {
            var px = new Color[16 * 16];
            for (var y = 0; y < 16; y++)
            for (var x = 0; x < 16; x++)
                px[y * 16 + x] = (x < 8) ^ (y < 8) ? Color.FromArgb(255, 220, 220, 220) : Colors.White;
            _checker = CanvasBitmap.CreateFromColors(doc.Device, px, 16, 16);
        }
        var tile = new BorderEffect { Source = _checker, ExtendX = CanvasEdgeBehavior.Wrap, ExtendY = CanvasEdgeBehavior.Wrap };
        var scaled = new ScaleEffect { Source = tile, Scale = new Vector2(1 / _view.Scale), InterpolationMode = CanvasImageInterpolation.NearestNeighbor };
        owned.Add(tile);
        owned.Add(scaled);
        ds.DrawImage(scaled, Vector2.Zero, doc.Bounds);
    }

    private void DrawSymmetryGuides(CanvasDrawingSession ds, PaintDocument doc)
    {
        var mode = _settings.Symmetry;
        if (mode == SymmetryMode.None) return;
        var style = new Microsoft.Graphics.Canvas.Geometry.CanvasStrokeStyle { DashStyle = Microsoft.Graphics.Canvas.Geometry.CanvasDashStyle.Dash };
        var color = Color.FromArgb(200, 0, 229, 255);
        void Line(float ax, float ay, float bx, float by) =>
            ds.DrawLine(_view.ToScreen(new Vector2(ax, ay)), _view.ToScreen(new Vector2(bx, by)), color, 1.2f, style);
        float cx = doc.Width / 2f, cy = doc.Height / 2f;
        if (mode is SymmetryMode.Vertical or SymmetryMode.Quadrant) Line(cx, 0, cx, doc.Height);
        if (mode is SymmetryMode.Horizontal or SymmetryMode.Quadrant) Line(0, cy, doc.Width, cy);
        if (mode == SymmetryMode.Radial)
        {
            var r = MathF.Sqrt(cx * cx + cy * cy);
            for (var i = 0; i < 4; i++)
            {
                var t = i * MathF.PI / 4;
                Line(cx - MathF.Cos(t) * r, cy - MathF.Sin(t) * r, cx + MathF.Cos(t) * r, cy + MathF.Sin(t) * r);
            }
        }
    }

    private void DrawShapeLabel(CanvasDrawingSession ds)
    {
        if (_engine?.Shape is not { } shape) return;
        var text = shape.DisplayName + T(" creada");
        using var fmt = new Microsoft.Graphics.Canvas.Text.CanvasTextFormat { FontSize = 13, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, FontFamily = CanvasFonts.Ui };
        using var layout = new Microsoft.Graphics.Canvas.Text.CanvasTextLayout(ds, text, fmt, 400, 30);
        var w = (float)layout.LayoutBounds.Width + 28;
        var x = (ViewSize.X - w) / 2;
        ds.FillRoundedRectangle(x, 64, w, 30, 15, 15, Color.FromArgb(230, 20, 20, 22));
        ds.DrawTextLayout(layout, x + 14, 70, Colors.White);
    }

    private void DrawBrushCursor(CanvasDrawingSession ds)
    {
        var show = _hover is { } && _settings.BrushCursor && _transform is null && _liquify is null && _mode is InputMode.Idle or InputMode.Draw;
        var preview = DateTime.UtcNow < _cursorPreviewUntil;
        if (!show && !preview) return;
        var p = _hover ?? ViewSize / 2;
        var r = Math.Max(2, ToolState(Tool).Size * _view.Scale / 2);
        ds.DrawCircle(p, r + 0.75f, Color.FromArgb(140, 0, 0, 0), 1);
        ds.DrawCircle(p, r, Color.FromArgb(220, 255, 255, 255), 1);
        if (preview && !show) DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () => Canvas.Invalidate());
    }
}
