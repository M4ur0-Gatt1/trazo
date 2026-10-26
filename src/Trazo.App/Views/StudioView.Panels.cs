using System.Numerics;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Text;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Trazo.Core.Geometry;
using Trazo.Rendering;
using Trazo.Core.Brushes;
using Trazo.Core.Colors;
using Trazo.Core.Documents;
using Trazo.Core.IO;
using Trazo.Core.Strokes;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.UI;

namespace Trazo.App.Views;

public sealed partial class StudioView
{
    private float Dpi => 96 * (float)(XamlRoot?.RasterizationScale ?? 1);

    // ================= Herramientas =================

    private void Tool_Click(object sender, RoutedEventArgs e)
    {
        var button = (ToggleButton)sender;
        var tool = Enum.Parse<ToolKind>((string)button.Tag);
        if (Tool == tool) ShowBrushLibrary(button, tool);
        Tool = tool;
    }

    private void Eyedropper_Click(object sender, RoutedEventArgs e) => _eyedropOnce = EyedropperButton.IsChecked == true;

    // ================= Biblioteca de pinceles =================

    private readonly Dictionary<(string, bool), CanvasImageSource> _previews = [];

    private ImageSource BrushPreview(BrushPreset b, bool selected)
    {
        if (_previews.TryGetValue((b.Id, selected), out var src)) return src;
        src = new CanvasImageSource(_doc!.Device, 200, 44, Dpi);
        using (var ds = src.CreateDrawingSession(Colors.Transparent))
            BrushEngine.DrawPreview(ds, _tips!, _grains!, b, 200, 44, selected ? Colors.White : Color.FromArgb(255, 200, 200, 206));
        _previews[(b.Id, selected)] = src;
        return src;
    }

    /// <param name="showCurves">Abre los ajustes del pincel por las curvas de presión.</param>
    private void ShowBrushLibrary(FrameworkElement anchor, ToolKind tool, bool showCurves = false)
    {
        FrameworkElement? curveAnchor = null;
        if (_doc is null) return;
        var flyout = new Flyout { Placement = FlyoutPlacementMode.BottomEdgeAlignedRight };
        var current = BrushFor(tool);
        var category = current.Category;
        var cats = new ListView { Width = 140, SelectionMode = ListViewSelectionMode.Single };
        var list = new StackPanel { Spacing = 4 };
        var settingsPanel = new StackPanel { Spacing = 2 };

        void RenderList()
        {
            list.Children.Clear();
            foreach (var b in _brushes.Categories.First(c => c.Name == category).Brushes)
            {
                var selected = b.Id == ToolState(tool).Brush;
                var item = new Button
                {
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    HorizontalContentAlignment = HorizontalAlignment.Left,
                    Padding = new Thickness(10, 6, 10, 6),
                    Background = selected ? (Brush)Application.Current.Resources["AccentBrushTrazo"] : new SolidColorBrush(Colors.Transparent),
                    BorderThickness = new Thickness(0),
                    Content = new StackPanel
                    {
                        Children =
                        {
                            new TextBlock { Text = b.Name, FontSize = 13, FontWeight = FontWeights.SemiBold },
                            new Image { Source = BrushPreview(b, selected), Width = 200, Height = 44 },
                        },
                    },
                };
                item.Click += (_, _) =>
                {
                    var st = ToolState(tool);
                    st.Brush = b.Id;
                    st.Size = b.Size;
                    _settings.Save();
                    RefreshToolUi();
                    RenderList();
                    RenderSettings();
                };
                list.Children.Add(item);
            }
        }

        void RenderSettings()
        {
            var b = BrushFor(tool);
            settingsPanel.Children.Clear();
            settingsPanel.Children.Add(new TextBlock { Text = $"Ajustes de «{b.Name}»", Opacity = 0.7, FontSize = 12, Margin = new Thickness(0, 6, 0, 2) });
            void Row(string label, double min, double max, double value, Action<float> set)
            {
                var s = new Slider { Header = label, Minimum = min, Maximum = max, StepFrequency = (max - min) / 100, Value = value };
                s.ValueChanged += (_, e) =>
                {
                    set((float)e.NewValue);
                    _settings.BrushOverrides[b.Id] = Services.BrushOverride.From(b);
                    _settings.Save();
                    _previews.Remove((b.Id, true));
                    _previews.Remove((b.Id, false));
                };
                settingsPanel.Children.Add(s);
            }
            Row("StreamLine", 0, 1, b.StreamLine, v => b.StreamLine = v);
            Row("Espaciado", 0.01, 2, b.Spacing, v => b.Spacing = v);
            Row("Flujo", 0.02, 1, b.Flow, v => b.Flow = v);
            Row("Tamaño a presión mínima", 0, 1, b.MinSizePressure, v => b.MinSizePressure = v);
            Row("Flujo por presión", 0, 1, b.FlowPressure, v => b.FlowPressure = v);
            Row("Opacidad por presión", 0, 1, b.OpacityPressure, v => b.OpacityPressure = v);

            // ---- Curvas de presión (BR-06) ----
            settingsPanel.Children.Add(new TextBlock { Text = "Curvas de presión", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 10, 0, 0) });
            var curveHost = new ContentControl { HorizontalContentAlignment = HorizontalAlignment.Left };
            var curveKind = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch, ItemsSource = new[] { "Tamaño", "Flujo", "Opacidad" }, SelectedIndex = 0 };
            void ShowCurve()
            {
                curveHost.Content = curveKind.SelectedIndex switch
                {
                    1 => CurveEditor(() => b.FlowCurve, c => { b.FlowCurve = c; SaveBrush(); }),
                    2 => CurveEditor(() => b.OpacityCurve, c => { b.OpacityCurve = c; SaveBrush(); }),
                    _ => CurveEditor(() => b.SizeCurve, c => { b.SizeCurve = c; SaveBrush(); }),
                };
            }
            curveKind.SelectionChanged += (_, _) => ShowCurve();
            settingsPanel.Children.Add(curveKind);
            settingsPanel.Children.Add(curveHost);
            curveAnchor = curveKind;
            ShowCurve();

            // ---- Mezcla húmeda (BR-04) ----
            settingsPanel.Children.Add(new TextBlock { Text = "Mezcla húmeda", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 10, 0, 0) });
            Row("Dilución", 0, 1, b.Wet.Dilution, v => b.Wet = b.Wet with { Dilution = v });
            Row("Carga", 0, 1, b.Wet.Charge, v => b.Wet = b.Wet with { Charge = v });
            Row("Ataque", 0, 1, b.Wet.Attack, v => b.Wet = b.Wet with { Attack = v });
            Row("Arrastre", 0, 1, b.Wet.Pull, v => b.Wet = b.Wet with { Pull = v });
            Row("Variación de tamaño", 0, 1, b.Jitter, v => b.Jitter = v);
            Row("Dispersión", 0, 2, b.Scatter, v => b.Scatter = v);

            // ---- Grano (BR-03) ----
            settingsPanel.Children.Add(new TextBlock { Text = "Grano", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 10, 0, 0) });
            var mode = new ComboBox { Header = "Comportamiento", HorizontalAlignment = HorizontalAlignment.Stretch, ItemsSource = new[] { "Sin grano", "En movimiento", "Texturizado" }, SelectedIndex = (int)b.Grain.Mode };
            mode.SelectionChanged += (_, _) => { b.Grain = b.Grain with { Mode = (GrainMode)mode.SelectedIndex }; SaveBrush(); };
            settingsPanel.Children.Add(mode);
            var kinds = new[] { (GrainKind.Paper, "Papel"), (GrainKind.Canvas, "Lienzo"), (GrainKind.Charcoal, "Carboncillo"), (GrainKind.Noise, "Ruido"), (GrainKind.Sponge, "Esponja") };
            var kind = new ComboBox { Header = "Textura", HorizontalAlignment = HorizontalAlignment.Stretch, ItemsSource = kinds.Select(k => k.Item2).ToList(), SelectedIndex = Array.FindIndex(kinds, k => k.Item1 == b.Grain.Kind) };
            kind.SelectionChanged += (_, _) => { b.Grain = b.Grain with { Kind = kinds[kind.SelectedIndex].Item1 }; SaveBrush(); };
            settingsPanel.Children.Add(kind);
            Row("Profundidad del grano", 0, 1, b.Grain.Depth, v => b.Grain = b.Grain with { Depth = v });
            Row("Escala del grano", 0.1, 4, b.Grain.Scale, v => b.Grain = b.Grain with { Scale = v });
            Row("Movimiento (en movimiento)", 0, 1, b.Grain.Movement, v => b.Grain = b.Grain with { Movement = v });
            Row("Rotación (en movimiento)", -1, 1, b.Grain.Rotation, v => b.Grain = b.Grain with { Rotation = v });
            Row("Brillo del grano", -1, 1, b.Grain.Brightness, v => b.Grain = b.Grain with { Brightness = v });
            Row("Contraste del grano", 0, 2, b.Grain.Contrast, v => b.Grain = b.Grain with { Contrast = v });

            // ---- Afinado (BR-05): longitudes en diámetros del pincel ----
            settingsPanel.Children.Add(new TextBlock { Text = "Afinado con lápiz", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 10, 0, 0) });
            Row("Inicio (diámetros)", 0, 10, b.PressureTaper.Start, v => b.PressureTaper = b.PressureTaper with { Start = v });
            Row("Final (diámetros)", 0, 10, b.PressureTaper.End, v => b.PressureTaper = b.PressureTaper with { End = v });
            Row("Tamaño en la punta", 0, 1, b.PressureTaper.TipSize, v => b.PressureTaper = b.PressureTaper with { TipSize = v });
            Row("Opacidad en la punta", 0, 1, b.PressureTaper.TipOpacity, v => b.PressureTaper = b.PressureTaper with { TipOpacity = v });
            Row("Forma (fina ↔ roma)", 0, 1, b.PressureTaper.Shape, v => b.PressureTaper = b.PressureTaper with { Shape = v });
            settingsPanel.Children.Add(new TextBlock { Text = "Afinado con dedo o ratón", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 10, 0, 0) });
            Row("Inicio (diámetros) ", 0, 10, b.TouchTaper.Start, v => b.TouchTaper = b.TouchTaper with { Start = v });
            Row("Final (diámetros) ", 0, 10, b.TouchTaper.End, v => b.TouchTaper = b.TouchTaper with { End = v });
            Row("Tamaño en la punta ", 0, 1, b.TouchTaper.TipSize, v => b.TouchTaper = b.TouchTaper with { TipSize = v });
            Row("Opacidad en la punta ", 0, 1, b.TouchTaper.TipOpacity, v => b.TouchTaper = b.TouchTaper with { TipOpacity = v });

            void SaveBrush()
            {
                _settings.BrushOverrides[b.Id] = Services.BrushOverride.From(b);
                _settings.Save();
                _previews.Remove((b.Id, true));
                _previews.Remove((b.Id, false));
            }
            var reset = new Button { Content = "Restablecer pincel", Margin = new Thickness(0, 6, 0, 0) };
            reset.Click += (_, _) =>
            {
                b.CopySettingsFrom(BrushLibrary.Default(b.Id));
                _settings.BrushOverrides.Remove(b.Id);
                _settings.Save();
                _previews.Remove((b.Id, true));
                _previews.Remove((b.Id, false));
                RenderSettings();
                RenderList();
            };
            settingsPanel.Children.Add(reset);
        }

        foreach (var c in _brushes.Categories) cats.Items.Add(c.Name);
        cats.SelectedItem = category;
        cats.SelectionChanged += (_, _) =>
        {
            if (cats.SelectedItem is string s) { category = s; RenderList(); }
        };
        RenderList();
        RenderSettings();

        var titles = new Dictionary<ToolKind, string> { [ToolKind.Brush] = "Pintar", [ToolKind.Smudge] = "Difuminar", [ToolKind.Eraser] = "Borrar" };
        var body = new Grid { ColumnSpacing = 8, Height = 380 };
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        body.Children.Add(cats);
        var scroll = new ScrollViewer { Content = list };
        Grid.SetColumn(scroll, 1);
        body.Children.Add(scroll);
        flyout.Content = new StackPanel
        {
            Width = 420,
            Spacing = 8,
            Children =
            {
                new TextBlock { Text = "Biblioteca de pinceles · " + titles[tool], FontWeight = FontWeights.SemiBold, FontSize = 15 },
                body,
                new Expander { Header = "Ajustes del pincel", HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch,
                    IsExpanded = showCurves, Content = new ScrollViewer { MaxHeight = 320, Content = settingsPanel } },
            },
        };
        flyout.ShowAt(anchor);
        if (showCurves)
            flyout.Opened += (_, _) => DispatcherQueue.TryEnqueue(() => curveAnchor?.StartBringIntoView(new BringIntoViewOptions { VerticalAlignmentRatio = 0 }));
    }

    // ================= Capas =================

    private Flyout? _layersFlyout;
    private StackPanel? _layersList;
    private readonly Dictionary<Layer, (CanvasImageSource Image, int Revision)> _thumbs = [];

    private void Layers_Click(object sender, RoutedEventArgs e) => ShowLayersPanel();

    private void ShowLayersPanel()
    {
        if (_doc is null) return;
        _layersList = new StackPanel { Spacing = 4 };
        var add = new Button { Content = new FontIcon { Glyph = "", FontSize = 14 }, Padding = new Thickness(10, 6, 10, 6) };
        ToolTipService.SetToolTip(add, "Nueva capa");
        add.Click += (_, _) => _ops?.Add();
        var header = new Grid { Margin = new Thickness(0, 0, 0, 6) };
        header.Children.Add(new TextBlock { Text = "Capas", FontWeight = FontWeights.SemiBold, FontSize = 16, VerticalAlignment = VerticalAlignment.Center });
        add.HorizontalAlignment = HorizontalAlignment.Right;
        header.Children.Add(add);
        _layersFlyout = new Flyout
        {
            Placement = FlyoutPlacementMode.BottomEdgeAlignedRight,
            Content = new StackPanel { Width = 330, Children = { header, new ScrollViewer { MaxHeight = 560, Content = _layersList } } },
        };
        _layersFlyout.Closed += (_, _) => { _layersFlyout = null; _layersList = null; };
        RefreshLayersPanel();
        _layersFlyout.ShowAt(LayersButton);
    }

    private ImageSource Thumbnail(Layer layer)
    {
        if (_thumbs.TryGetValue(layer, out var t) && t.Revision == layer.Revision) return t.Image;
        var doc = _doc!;
        var k = Math.Min(64f / doc.Width, 48f / doc.Height);
        var img = t.Image ?? new CanvasImageSource(doc.Device, 64, 48, Dpi);
        using (var ds = img.CreateDrawingSession(Colors.Transparent))
            ds.DrawImage(layer.Surface, new Windows.Foundation.Rect((64 - doc.Width * k) / 2, (48 - doc.Height * k) / 2, doc.Width * k, doc.Height * k), doc.Bounds, 1, CanvasImageInterpolation.HighQualityCubic);
        _thumbs[layer] = (img, layer.Revision);
        return img;
    }

    private readonly Dictionary<Layer, (CanvasImageSource Image, int Revision)> _maskThumbs = [];

    private ImageSource MaskThumbnail(Layer layer)
    {
        if (_maskThumbs.TryGetValue(layer, out var t) && t.Revision == layer.Revision) return t.Image;
        var doc = _doc!;
        var k = Math.Min(40f / doc.Width, 48f / doc.Height);
        var img = t.Image ?? new CanvasImageSource(doc.Device, 40, 48, Dpi);
        using (var ds = img.CreateDrawingSession(Colors.Black))
            if (layer.Mask is { } m)
                ds.DrawImage(m, new Windows.Foundation.Rect((40 - doc.Width * k) / 2, (48 - doc.Height * k) / 2, doc.Width * k, doc.Height * k), doc.Bounds, 1, CanvasImageInterpolation.HighQualityCubic);
        _maskThumbs[layer] = (img, layer.Revision);
        return img;
    }

    private void RefreshLayersPanel()
    {
        if (_layersList is null || _doc is null) return;
        var doc = _doc;
        _layersList.Children.Clear();
        var accent = (Brush)Application.Current.Resources["AccentBrushTrazo"];
        var normal = new SolidColorBrush(Color.FromArgb(255, 44, 45, 49));
        var groupBg = new SolidColorBrush(Color.FromArgb(255, 34, 35, 39));
        for (var i = doc.Layers.Count - 1; i >= 0; i--)
        {
            var layer = doc.Layers[i];
            var group = doc.Group(layer.GroupId);
            // la cabecera del grupo va encima de su miembro más alto
            if (group is not null && (i == doc.Layers.Count - 1 || doc.Layers[i + 1].GroupId != group.Id))
                _layersList.Children.Add(GroupRow(group, groupBg));
            if (group is { Collapsed: true }) continue;
            _layersList.Children.Add(LayerRow(i, group is not null, accent, normal));
        }

        // fila del color de fondo
        var bgRow = new Grid { Padding = new Thickness(6), CornerRadius = new CornerRadius(10), Background = normal, ColumnSpacing = 10 };
        bgRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        bgRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        bgRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var swatch = new Button { Width = 64, Height = 48, Background = new SolidColorBrush(doc.Background), CornerRadius = new CornerRadius(6) };
        ToolTipService.SetToolTip(swatch, "Cambiar el color de fondo");
        swatch.Click += (_, _) => PickBackground(swatch);
        bgRow.Children.Add(swatch);
        var bgName = new TextBlock { Text = "Color de fondo", VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeights.SemiBold };
        Grid.SetColumn(bgName, 1);
        bgRow.Children.Add(bgName);
        var bgVisible = new CheckBox { IsChecked = doc.BackgroundVisible, MinWidth = 0 };
        bgVisible.Click += (_, _) => _ops?.SetBackground(doc.Background, bgVisible.IsChecked == true);
        Grid.SetColumn(bgVisible, 2);
        bgRow.Children.Add(bgVisible);
        _layersList.Children.Add(bgRow);
        _layersList.Children.Add(new TextBlock { Text = $"{doc.Layers.Count} de {doc.MaxLayers} capas", FontSize = 11, Opacity = 0.5, Margin = new Thickness(4, 6, 0, 0) });
    }

    private Grid LayerRow(int index, bool inGroup, Brush accent, Brush normal)
    {
        var doc = _doc!;
        var layer = doc.Layers[index];
        var active = index == doc.ActiveIndex;
        var row = new Grid
        {
            Padding = new Thickness(6), CornerRadius = new CornerRadius(10), Background = active ? accent : normal, ColumnSpacing = 8, MinHeight = 60,
            Margin = new Thickness(inGroup ? 18 : 0, 0, 0, 0),
        };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var editingMask = layer.HasMask && layer.EditingMask;
        var selectedBorder = new SolidColorBrush(Colors.White);
        var thumb = new Border
        {
            Width = 64, Height = 48, CornerRadius = new CornerRadius(6),
            Background = new SolidColorBrush(Color.FromArgb(255, 210, 210, 210)),
            Child = new Image { Source = Thumbnail(layer) },
            Margin = new Thickness(layer.Clip && index > 0 ? 14 : 0, 0, 0, 0),
            Opacity = layer.Visible ? 1 : 0.45,
            BorderThickness = new Thickness(layer.HasMask && active && !editingMask ? 2 : 0),
            BorderBrush = selectedBorder,
        };
        row.Children.Add(thumb);
        Border? maskThumb = null;
        if (layer.HasMask)
        {
            maskThumb = new Border
            {
                Width = 40, Height = 48, CornerRadius = new CornerRadius(6), Child = new Image { Source = MaskThumbnail(layer) },
                BorderThickness = new Thickness(editingMask && active ? 2 : 0), BorderBrush = selectedBorder,
                Opacity = layer.Visible ? 1 : 0.45,
            };
            ToolTipService.SetToolTip(maskThumb, "Máscara de capa: tócala para pintar en ella");
            Grid.SetColumn(maskThumb, 1);
            row.Children.Add(maskThumb);
        }
        var info = new List<string>();
        if (editingMask) info.Add("editando máscara");
        if (layer.AlphaLock) info.Add("α bloqueado");
        if (layer.Clip && index > 0) info.Add("recorte");
        if (layer.Opacity < 1) info.Add($"{layer.Opacity * 100:0} %");
        var name = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Opacity = layer.Visible ? 1 : 0.55 };
        name.Children.Add(new TextBlock { Text = layer.Name, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
        if (info.Count > 0) name.Children.Add(new TextBlock { Text = string.Join(" · ", info), FontSize = 11, Opacity = 0.75, TextTrimming = TextTrimming.CharacterEllipsis });
        Grid.SetColumn(name, 2);
        row.Children.Add(name);
        var blend = new Button { Content = layer.BlendShort, Width = 38, Height = 34, Padding = new Thickness(0), FontWeight = FontWeights.Bold };
        ToolTipService.SetToolTip(blend, "Opacidad y modo de fusión");
        blend.Click += (_, _) => ShowBlendFlyout(blend, layer);
        Grid.SetColumn(blend, 3);
        row.Children.Add(blend);
        var visible = new CheckBox { IsChecked = layer.Visible, MinWidth = 0, Padding = new Thickness(0), VerticalAlignment = VerticalAlignment.Center };
        ToolTipService.SetToolTip(visible, "Visible");
        visible.Click += (_, _) => _ops?.Set(layer, "visible", visible.IsChecked == true, l => l.Visible, (l, v) => l.Visible = v, "Visibilidad");
        Grid.SetColumn(visible, 4);
        row.Children.Add(visible);
        row.Tapped += (_, e) =>
        {
            if (e.OriginalSource is FrameworkElement fe && (IsInside(fe, blend) || IsInside(fe, visible))) return;
            var onMask = maskThumb is not null && e.OriginalSource is FrameworkElement m && IsInside(m, maskThumb);
            var onThumb = e.OriginalSource is FrameworkElement t && IsInside(t, thumb);
            var i = doc.Layers.IndexOf(layer);
            if (onMask || (onThumb && layer.HasMask))
            {
                // tocar una miniatura elige dónde se pinta: la capa o su máscara
                doc.ActiveIndex = i;
                layer.EditingMask = onMask;
                RefreshLayersPanel();
                Canvas.Invalidate();
            }
            else if (i == doc.ActiveIndex) ShowLayerMenu(row, layer);
            else
            {
                doc.ActiveIndex = i;
                RefreshLayersPanel();
            }
        };
        return row;
    }

    private Grid GroupRow(LayerGroup group, Brush background)
    {
        var row = new Grid { Padding = new Thickness(4, 6, 6, 6), CornerRadius = new CornerRadius(10), Background = background, ColumnSpacing = 8, MinHeight = 44 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var chevron = new Button
        {
            Content = new FontIcon { Glyph = group.Collapsed ? "" : "", FontSize = 12 },
            Width = 34, Height = 34, Padding = new Thickness(0), Background = new SolidColorBrush(Colors.Transparent), BorderThickness = new Thickness(0),
        };
        ToolTipService.SetToolTip(chevron, group.Collapsed ? "Expandir grupo" : "Contraer grupo");
        chevron.Click += (_, _) =>
        {
            group.Collapsed = !group.Collapsed;
            RefreshLayersPanel();
        };
        row.Children.Add(chevron);
        var count = _doc!.Layers.Count(l => l.GroupId == group.Id);
        var info = new List<string> { count == 1 ? "1 capa" : $"{count} capas" };
        if (group.Opacity < 1) info.Add($"{group.Opacity * 100:0} %");
        var name = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Opacity = group.Visible ? 1 : 0.55, Orientation = Orientation.Horizontal, Spacing = 8 };
        name.Children.Add(new FontIcon { Glyph = "", FontSize = 16 });
        var text = new StackPanel();
        text.Children.Add(new TextBlock { Text = group.Name, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
        text.Children.Add(new TextBlock { Text = string.Join(" · ", info), FontSize = 11, Opacity = 0.75 });
        name.Children.Add(text);
        Grid.SetColumn(name, 1);
        row.Children.Add(name);
        var blend = new Button { Content = group.Blend.ShortName(), Width = 38, Height = 34, Padding = new Thickness(0), FontWeight = FontWeights.Bold };
        ToolTipService.SetToolTip(blend, "Opacidad y modo de fusión del grupo");
        blend.Click += (_, _) => ShowGroupBlendFlyout(blend, group);
        Grid.SetColumn(blend, 2);
        row.Children.Add(blend);
        var visible = new CheckBox { IsChecked = group.Visible, MinWidth = 0, Padding = new Thickness(0), VerticalAlignment = VerticalAlignment.Center };
        ToolTipService.SetToolTip(visible, "Visible");
        visible.Click += (_, _) => _ops?.SetGroup(group, visible.IsChecked == true, g => g.Visible, (g, v) => g.Visible = v, "Visibilidad del grupo");
        Grid.SetColumn(visible, 3);
        row.Children.Add(visible);
        row.Tapped += (_, e) =>
        {
            if (e.OriginalSource is FrameworkElement fe && (IsInside(fe, blend) || IsInside(fe, visible) || IsInside(fe, chevron))) return;
            ShowGroupMenu(row, group);
        };
        return row;
    }

    private void ShowGroupBlendFlyout(FrameworkElement anchor, LayerGroup group)
    {
        var opacity = new Slider { Header = "Opacidad del grupo", Minimum = 0, Maximum = 100, Value = group.Opacity * 100 };
        opacity.ValueChanged += (_, e) =>
        {
            _ops?.SetGroup(group, (float)e.NewValue / 100, g => g.Opacity, (g, v) => g.Opacity = v, "Opacidad del grupo");
            Canvas.Invalidate();
        };
        var modes = new ListView { SelectionMode = ListViewSelectionMode.Single, MaxHeight = 360 };
        foreach (var m in BlendModes.All) modes.Items.Add(new ListViewItem { Content = m.DisplayName(), Tag = m });
        modes.SelectedIndex = (int)group.Blend;
        modes.SelectionChanged += (_, _) =>
        {
            if (modes.SelectedItem is ListViewItem { Tag: BlendMode m })
                _ops?.SetGroup(group, m, g => g.Blend, (g, v) => g.Blend = v, "Fusión del grupo");
            Canvas.Invalidate();
        };
        new Flyout { Content = new StackPanel { Width = 260, Spacing = 8, Children = { opacity, modes } } }.ShowAt(anchor);
    }

    private void ShowGroupMenu(FrameworkElement anchor, LayerGroup group)
    {
        var menu = new MenuFlyout();
        void Item(string text, string glyph, Action action)
        {
            var mi = new MenuFlyoutItem { Text = text, Icon = new FontIcon { Glyph = glyph } };
            mi.Click += (_, _) => { action(); RefreshLayersPanel(); Canvas.Invalidate(); };
            menu.Items.Add(mi);
        }
        Item("Cambiar nombre…", "", async () =>
        {
            var box = new TextBox { Text = group.Name };
            if (await Dialogs.ShowAsync(XamlRoot, "Nombre del grupo", box, "Aceptar") == ContentDialogResult.Primary && box.Text.Trim().Length > 0)
                _ops!.SetGroup(group, box.Text.Trim(), g => g.Name, (g, v) => g.Name = v, "Renombrar grupo");
            RefreshLayersPanel();
        });
        Item("Combinar grupo", "", () => _ops!.MergeGroup(group.Id));
        Item("Desagrupar", "", () => _ops!.Ungroup(group.Id));
        menu.Items.Add(new MenuFlyoutSeparator());
        Item("Eliminar grupo y sus capas", "", () => _ops!.DeleteGroup(group.Id));
        menu.ShowAt(anchor);
    }

    private static bool IsInside(DependencyObject? child, DependencyObject parent)
    {
        for (var e = child; e is not null; e = VisualTreeHelper.GetParent(e))
            if (e == parent) return true;
        return false;
    }

    private void PickBackground(FrameworkElement anchor)
    {
        var picker = new ColorPicker { Color = _doc!.Background, IsAlphaEnabled = false, ColorSpectrumShape = ColorSpectrumShape.Ring, IsMoreButtonVisible = false };
        var apply = new Button { Content = "Aplicar", Style = (Style)Application.Current.Resources["AccentButtonStyle"], HorizontalAlignment = HorizontalAlignment.Right };
        var fly = new Flyout { Content = new StackPanel { Spacing = 8, Children = { picker, apply } } };
        apply.Click += (_, _) =>
        {
            _ops?.SetBackground(picker.Color, _doc.BackgroundVisible);
            fly.Hide();
        };
        fly.ShowAt(anchor);
    }

    private void ShowBlendFlyout(FrameworkElement anchor, Layer layer)
    {
        var opacity = new Slider { Header = "Opacidad", Minimum = 0, Maximum = 100, Value = layer.Opacity * 100 };
        opacity.ValueChanged += (_, e) =>
        {
            _ops?.Set(layer, "opacity", (float)e.NewValue / 100, l => l.Opacity, (l, v) => l.Opacity = v, "Opacidad de capa");
            Canvas.Invalidate();
        };
        var modes = new ListView { SelectionMode = ListViewSelectionMode.Single, MaxHeight = 360 };
        foreach (var m in BlendModes.All) modes.Items.Add(new ListViewItem { Content = m.DisplayName(), Tag = m });
        modes.SelectedIndex = (int)layer.Blend;
        modes.SelectionChanged += (_, _) =>
        {
            if (modes.SelectedItem is ListViewItem { Tag: BlendMode m })
                _ops?.Set(layer, "blend", m, l => l.Blend, (l, v) => l.Blend = v, "Modo de fusión");
            Canvas.Invalidate();
        };
        new Flyout { Content = new StackPanel { Width = 260, Spacing = 8, Children = { opacity, modes } } }.ShowAt(anchor);
    }

    private void ShowLayerMenu(FrameworkElement anchor, Layer layer)
    {
        var doc = _doc!;
        int Index() => doc.Layers.IndexOf(layer);
        var menu = new MenuFlyout();
        void Item(string text, string glyph, Action action, bool enabled = true)
        {
            var mi = new MenuFlyoutItem { Text = text, Icon = new FontIcon { Glyph = glyph }, IsEnabled = enabled };
            mi.Click += (_, _) => { action(); RefreshLayersPanel(); Canvas.Invalidate(); };
            menu.Items.Add(mi);
        }
        void Toggle(string text, bool value, Action<bool> action)
        {
            var mi = new ToggleMenuFlyoutItem { Text = text, IsChecked = value };
            mi.Click += (_, _) => { action(mi.IsChecked); RefreshLayersPanel(); Canvas.Invalidate(); };
            menu.Items.Add(mi);
        }
        Item("Cambiar nombre…", "", async () =>
        {
            var box = new TextBox { Text = layer.Name };
            if (await Dialogs.ShowAsync(XamlRoot, "Nombre de la capa", box, "Aceptar") == ContentDialogResult.Primary && box.Text.Trim().Length > 0)
                _ops!.Set(layer, "name", box.Text.Trim(), l => l.Name, (l, v) => l.Name = v, "Renombrar capa");
            RefreshLayersPanel();
        });
        Item("Rellenar con el color actual", "", () => _ops!.Fill(Index(), CurrentColor));
        Item("Borrar contenido", "", () => _ops!.Clear(Index()));
        menu.Items.Add(new MenuFlyoutSeparator());
        Toggle("Bloqueo alfa", layer.AlphaLock, v => _ops!.Set(layer, "alphaLock", v, l => l.AlphaLock, (l, x) => l.AlphaLock = x, "Bloqueo alfa"));
        Toggle("Máscara de recorte", layer.Clip, v => _ops!.Set(layer, "clip", v, l => l.Clip, (l, x) => l.Clip = x, "Máscara de recorte"));
        menu.Items.Add(new MenuFlyoutSeparator());
        if (!layer.HasMask) Item("Máscara de capa", "\uE7B8", () => _ops!.AddMask(layer));
        else
        {
            Toggle("Pintar en la máscara", layer.EditingMask, v => layer.EditingMask = v);
            Item("Invertir máscara", "\uE746", () => _ops!.InvertMask(layer));
            Item("Aplicar máscara", "\uE73E", () => _ops!.ApplyMask(layer));
            Item("Eliminar máscara", "\uE74D", () => _ops!.DeleteMask(layer));
        }
        Item("Seleccionar contenido", "\uE8B3", () => _ops!.SelectLayerContent(layer));
        if (layer.GroupId is null)
        {
            Item("Nuevo grupo con esta capa", "\uE8B7", () => _ops!.CreateGroup(Index()));
            Item("Agrupar con la de abajo", "\uE8B7", () => _ops!.GroupWithBelow(Index()), Index() > 0);
        }
        menu.Items.Add(new MenuFlyoutSeparator());
        Item("Invertir", "", () => _ops!.Invert(Index()));
        Item("Transformar", "", StartTransform);
        Item("Duplicar", "", () => _ops!.Duplicate(Index()));
        Item("Combinar hacia abajo", "", () => _ops!.MergeDown(Index()), Index() > 0);
        Item("Subir", "", () => _ops!.Move(Index(), Index() + 1), Index() < doc.Layers.Count - 1);
        Item("Bajar", "", () => _ops!.Move(Index(), Index() - 1), Index() > 0);
        Item("Exportar capa como PNG…", "", async () => await ExportBitmapAsync(layer.Surface, layer.Name, CanvasBitmapFileFormat.Png));
        menu.Items.Add(new MenuFlyoutSeparator());
        Item("Eliminar", "", () => _ops!.Remove(Index()));
        menu.ShowAt(anchor);
    }

    // ================= Color =================

    private static readonly string[] DefaultPalette =
    [
        "#000000", "#3a3a3c", "#7c7c80", "#bdbdc2", "#ffffff", "#5b3a29", "#8d5a3b", "#c68b59", "#e8c39e", "#f6e2c9",
        "#7a0d1e", "#d7263d", "#ff6b6b", "#ff9f43", "#feca57", "#1dd1a1", "#10ac84", "#0b6e4f", "#48dbfb", "#0abde3",
        "#2e86de", "#1f3a93", "#5f27cd", "#9b59b6", "#f368e0", "#ff9ff3", "#576574", "#222f3e", "#c8d6e5", "#f4f1de",
    ];

    private bool _colorDragged;
    private Vector2 _colorDragStart;
    private bool _colorPressed;

    private void Color_Click(object sender, RoutedEventArgs e)
    {
        if (_colorDragged) { _colorDragged = false; return; }
        ShowColorPanel();
    }

    private void ShowColorPanel()
    {
        var picker = new ColorPicker
        {
            Color = CurrentColor,
            ColorSpectrumShape = ColorSpectrumShape.Ring,
            IsAlphaEnabled = false,
            IsMoreButtonVisible = true,
            IsColorChannelTextInputVisible = true,
            IsHexInputVisible = true,
        };
        picker.ColorChanged += (_, e) =>
        {
            CurrentColor = e.NewColor;
            ColorSwatch.Fill = new SolidColorBrush(e.NewColor);
        };

        WrapGrid Swatches(IEnumerable<string> colors, Action<string>? onRemove = null)
        {
            var panel = new VariableSizedWrapGrid { Orientation = Orientation.Horizontal, MaximumRowsOrColumns = 10, ItemWidth = 30, ItemHeight = 30 };
            foreach (var hex in colors)
            {
                if (!Rgba.TryParseHex(hex, out var rgba)) continue;
                var b = new Button { Width = 26, Height = 26, Padding = new Thickness(0), CornerRadius = new CornerRadius(6), Background = new SolidColorBrush(PaintDocument.ToColor(rgba)), BorderThickness = new Thickness(1) };
                b.Click += (_, _) => picker.Color = PaintDocument.ToColor(rgba);
                if (onRemove is not null)
                {
                    ToolTipService.SetToolTip(b, "Clic derecho para quitar");
                    b.RightTapped += (_, _) => onRemove(hex);
                }
                panel.Children.Add(b);
            }
            return new WrapGrid(panel);
        }

        var custom = new StackPanel();
        void RenderCustom()
        {
            custom.Children.Clear();
            custom.Children.Add(Swatches(_settings.Palette, hex => { _settings.Palette.Remove(hex); _settings.Save(); RenderCustom(); }).Panel);
            var add = new Button { Content = "Añadir color actual", Margin = new Thickness(0, 4, 0, 0) };
            add.Click += (_, _) =>
            {
                var hex = PaintDocument.ToRgba(CurrentColor).ToHex();
                if (!_settings.Palette.Contains(hex)) _settings.Palette.Add(hex);
                _settings.Save();
                RenderCustom();
            };
            custom.Children.Add(add);
        }
        RenderCustom();

        var content = new StackPanel { Spacing = 8, Width = 330 };
        content.Children.Add(picker);
        if (_settings.RecentColors.Count > 0)
        {
            content.Children.Add(new TextBlock { Text = "Historial", Opacity = 0.6, FontSize = 12 });
            content.Children.Add(Swatches(_settings.RecentColors).Panel);
        }
        content.Children.Add(new TextBlock { Text = "Paleta", Opacity = 0.6, FontSize = 12 });
        content.Children.Add(Swatches(DefaultPalette).Panel);
        content.Children.Add(new TextBlock { Text = "Mi paleta", Opacity = 0.6, FontSize = 12 });
        content.Children.Add(custom);
        new Flyout { Content = new ScrollViewer { MaxHeight = 720, Content = content }, Placement = FlyoutPlacementMode.BottomEdgeAlignedRight }.ShowAt(ColorButton);
    }

    private sealed record WrapGrid(VariableSizedWrapGrid Panel);

    // Arrastrar el círculo de color al lienzo = relleno (ColorDrop)
    private void ColorButton_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        var p = e.GetCurrentPoint(Stage).Position;
        _colorDragStart = new Vector2((float)p.X, (float)p.Y);
        _colorPressed = true;
        _colorDragged = false;
        ColorButton.CapturePointer(e.Pointer);
    }

    private void ColorButton_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_colorPressed) return;
        var p = e.GetCurrentPoint(Stage).Position;
        var v = new Vector2((float)p.X, (float)p.Y);
        if (!_colorDragged && Vector2.Distance(v, _colorDragStart) > 14)
        {
            _colorDragged = true;
            DropDot.Fill = new SolidColorBrush(CurrentColor);
            DropDot.Visibility = Visibility.Visible;
        }
        if (_colorDragged)
        {
            Microsoft.UI.Xaml.Controls.Canvas.SetLeft(DropDot, v.X - 22);
            Microsoft.UI.Xaml.Controls.Canvas.SetTop(DropDot, v.Y - 22);
        }
    }

    private void ColorButton_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (!_colorPressed) return;
        _colorPressed = false;
        ColorButton.ReleasePointerCapture(e.Pointer);
        DropDot.Visibility = Visibility.Collapsed;
        if (!_colorDragged) return;
        var p = e.GetCurrentPoint(Canvas).Position;
        if (p.X >= 0 && p.Y >= TopInset && p.X < Canvas.ActualWidth && p.Y < Canvas.ActualHeight)
            DoFill(new Vector2((float)p.X, (float)p.Y));
    }

    // ================= Ajustes =================

    private void BuildAdjustMenu()
    {
        var liquify = new MenuFlyoutItem { Text = "Licuar" };
        liquify.Click += (_, _) => StartLiquify();
        AdjustMenu.Items.Add(liquify);
        AdjustMenu.Items.Add(new MenuFlyoutSeparator());
        foreach (var a in Adjustment.All)
        {
            var mi = new MenuFlyoutItem { Text = a.Name };
            mi.Click += (_, _) => StartAdjustment(a);
            AdjustMenu.Items.Add(mi);
        }
    }

    private void StartAdjustment(Adjustment a)
    {
        if (_doc is null || _transform is not null) return;
        if (!_doc.ActiveLayer.Visible) { Window.ShowMessage("La capa activa está oculta"); return; }
        CancelInput();
        if (_selectMode) ExitSelectMode();
        if (_liquify is not null) ApplyLiquify();
        if (_guideEdit) EndGuideEdit();
        _adjust = new AdjustmentSession(_doc, _doc.ActiveLayer, a);
        SheetBody.Children.Clear();
        foreach (var p in a.Params)
        {
            var s = new Slider { Header = p.Label, Minimum = p.Min, Maximum = p.Max, Value = p.Default, StepFrequency = (p.Max - p.Min) / 400 };
            s.ThumbToolTipValueConverter = new UnitConverter(p.Unit);
            s.ValueChanged += (_, e) =>
            {
                if (_adjust is null) return;
                _adjust.Values[p.Key] = e.NewValue;
                Canvas.Invalidate();
            };
            SheetBody.Children.Add(s);
        }
        OpenSheet(a.Name);
    }

    private sealed class UnitConverter(string unit) : Microsoft.UI.Xaml.Data.IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language) => $"{(double)value:0.##}{unit}";
        public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotSupportedException();
    }

    private void OpenSheet(string title)
    {
        SheetTitle.Text = title;
        Sheet.Visibility = Visibility.Visible;
        Canvas.Invalidate();
    }

    private void CloseSheet()
    {
        _adjust = null;
        Sheet.Visibility = Visibility.Collapsed;
        Canvas.Invalidate();
    }

    private void SheetApply_Click(object sender, RoutedEventArgs e)
    {
        if (_selectMode) ExitSelectMode();
        else if (_liquify is not null) ApplyLiquify();
        else if (_guideEdit) EndGuideEdit();
        else if (_transform is not null) ApplyTransform();
        else if (_adjust is not null)
        {
            _adjust.Apply(_ops!);
            CloseSheet();
        }
    }

    private void SheetCancel_Click(object sender, RoutedEventArgs e)
    {
        if (_selectMode)
        {
            _ops?.Deselect();
            ExitSelectMode();
        }
        else if (_liquify is not null) EndLiquify();
        else if (_guideEdit) EndGuideEdit();
        else if (_transform is not null) CancelTransform();
        else CloseSheet();
    }

    // ================= Transformar =================

    private void Transform_Click(object sender, RoutedEventArgs e)
    {
        if (_transform is not null) ApplyTransform();
        else StartTransform();
    }

    private void StartTransform() => StartTransform(TransformMode.Freeform);

    private void StartTransform(TransformMode mode)
    {
        if (_selectMode) ExitSelectMode();
        if (_liquify is not null) ApplyLiquify();
        if (_guideEdit) EndGuideEdit();
        if (_doc is null || _transform is not null || _adjust is not null) { TransformButton.IsChecked = _transform is not null; return; }
        if (!_doc.ActiveLayer.Visible) { Window.ShowMessage("La capa activa está oculta"); TransformButton.IsChecked = false; return; }
        CancelInput();
        _transform = TransformSession.Start(_doc, _doc.ActiveLayer);
        if (_transform is null)
        {
            Window.ShowMessage("La capa está vacía");
            TransformButton.IsChecked = false;
            return;
        }
        _transform.Smooth = _settings.SmoothTransform;
        _transform.State.Magnetic = _settings.MagneticTransform;
        _transform.SetMode(mode);
        TransformButton.IsChecked = true;
        BuildTransformSheet();
        OpenSheet("Transformar");
    }

    private void ApplyTransform()
    {
        if (_transform is null) return;
        _transform.Apply(_ops!);
        EndTransform();
    }

    private void CancelTransform()
    {
        if (_transform is null) return;
        EndTransform();
    }

    private void EndTransform()
    {
        _transform?.Dispose();
        _transform = null;
        _contacts.Clear();
        TransformButton.IsChecked = false;
        Sheet.Visibility = Visibility.Collapsed;
        Canvas.Invalidate();
    }

    // ================= Acciones =================

    private void ActionsMenu_Opening(object sender, object e)
    {
        var menu = ActionsMenu;
        menu.Items.Clear();
        if (_doc is null) return;
        var doc = _doc;

        MenuFlyoutSubItem Sub(string text, string glyph) => new() { Text = text, Icon = new FontIcon { Glyph = glyph } };
        MenuFlyoutItem Item(string text, Func<Task> action, string? glyph = null)
        {
            var mi = new MenuFlyoutItem { Text = text };
            if (glyph is not null) mi.Icon = new FontIcon { Glyph = glyph };
            mi.Click += async (_, _) =>
            {
                try { await action(); }
                catch (Exception ex)
                {
                    Services.Log.Error(text, ex);
                    Window.ShowMessage("Error: " + ex.Message, InfoBarSeverity.Error);
                }
                Canvas.Invalidate();
            };
            return mi;
        }
        Func<Task> Sync(Action a) => () => { a(); return Task.CompletedTask; };
        ToggleMenuFlyoutItem Toggle(string text, bool value, Action<bool> set)
        {
            var t = new ToggleMenuFlyoutItem { Text = text, IsChecked = value };
            t.Click += (_, _) => { set(t.IsChecked); _settings.Save(); ApplyLayoutPreferences(); Canvas.Invalidate(); };
            return t;
        }

        var add = Sub("Añadir", "");
        add.Items.Add(Item("Insertar una foto…", InsertPhotoAsync, ""));
        add.Items.Add(Item("Pegar imagen", PasteImageAsync, ""));
        add.Items.Add(Item("Añadir texto…", AddTextAsync, ""));
        add.Items.Add(Item("Nueva capa", Sync(() => _ops!.Add()), ""));
        menu.Items.Add(add);

        var canvas = Sub("Lienzo", "");
        canvas.Items.Add(Item("Voltear horizontalmente", Sync(() => _ops!.FlipCanvas(true))));
        canvas.Items.Add(Item("Voltear verticalmente", Sync(() => _ops!.FlipCanvas(false))));
        canvas.Items.Add(Item("Ajustar a la pantalla", Sync(FitToScreen)));
        canvas.Items.Add(Item("Tamaño real (100 %)", Sync(ShowActualSize)));
        canvas.Items.Add(new MenuFlyoutSeparator());
        var sym = Sub("Guía de simetría", "");
        foreach (var (mode, label) in new[] { (SymmetryMode.None, "Ninguna"), (SymmetryMode.Vertical, "Vertical"), (SymmetryMode.Horizontal, "Horizontal"), (SymmetryMode.Quadrant, "Cuadrante"), (SymmetryMode.Radial, "Radial") })
        {
            var r = new RadioMenuFlyoutItem { Text = label, GroupName = "sym", IsChecked = _settings.Symmetry == mode };
            r.Click += (_, _) => { _settings.Symmetry = mode; _settings.Save(); Canvas.Invalidate(); };
            sym.Items.Add(r);
        }
        canvas.Items.Add(sym);
        canvas.Items.Add(Item("Guía de dibujo…", Sync(ShowGuideSheet)));
        canvas.Items.Add(Item("Cambiar nombre…", RenameDocumentAsync));
        canvas.Items.Add(Item("Información del lienzo", async () =>
        {
            var text = $"Dimensiones: {doc.Width} × {doc.Height} px\nCapas: {doc.Layers.Count} de {doc.MaxLayers}\nPasos de historial: {doc.History.UndoCount}\nMemoria del historial: {doc.History.Bytes / 1024 / 1024} MB\nArchivo: {doc.FilePath}";
            await Dialogs.ShowAsync(XamlRoot, doc.Name, new TextBlock { Text = text, IsTextSelectionEnabled = true, TextWrapping = TextWrapping.Wrap }, "Cerrar", "");
        }));
        menu.Items.Add(canvas);

        var share = Sub("Compartir", "");
        share.Items.Add(Item("PNG…", async () => { using var f = doc.Flatten(); await ExportBitmapAsync(f, doc.Name, CanvasBitmapFileFormat.Png); }));
        share.Items.Add(Item("PNG transparente…", async () => { using var f = doc.Flatten(includeBackground: false); await ExportBitmapAsync(f, doc.Name, CanvasBitmapFileFormat.Png); }));
        share.Items.Add(Item("JPEG…", ExportJpegAsync));
        share.Items.Add(Item("Capas como PNG…", ExportLayersAsync));
        share.Items.Add(Item("PSD (Photoshop)…", ExportPsdAsync));
        share.Items.Add(Item("Proyecto .trazo…", ExportProjectAsync));
        menu.Items.Add(share);

        var prefs = Sub("Preferencias", "");
        prefs.Items.Add(Toggle("Solo lápiz (el dedo navega)", PenOnly, v => _settings.PenOnly = v));
        prefs.Items.Add(Toggle("Interfaz para zurdos", _settings.LeftHanded, v => _settings.LeftHanded = v));
        prefs.Items.Add(Toggle("Rotar con dos dedos", _settings.RotateGesture, v => _settings.RotateGesture = v));
        prefs.Items.Add(Toggle("QuickShape", _settings.QuickShape, v => _settings.QuickShape = v));
        prefs.Items.Add(Toggle("Cursor del pincel", _settings.BrushCursor, v => _settings.BrushCursor = v));
        prefs.Items.Add(new MenuFlyoutSeparator());
        prefs.Items.Add(Item("Curva de presión y relleno…", PressureDialogAsync));
        menu.Items.Add(prefs);

        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(Item("Gestos y atajos", HelpAsync, ""));
        menu.Items.Add(Item("Acerca de Trazo", AboutAsync, ""));
    }

    private async Task InsertPhotoAsync()
    {
        var file = await Window.OpenPicker(".png", ".jpg", ".jpeg", ".bmp", ".gif", ".tif", ".tiff", ".webp").PickSingleFileAsync();
        if (file is null) return;
        using var stream = await file.OpenReadAsync();
        using var bmp = await CanvasBitmap.LoadAsync(_doc!.Device, stream, 96);
        if (_ops!.AddImage(bmp, Path.GetFileNameWithoutExtension(file.Name)) is not null) StartTransform();
    }

    private async Task PasteImageAsync()
    {
        var content = Clipboard.GetContent();
        if (!content.Contains(StandardDataFormats.Bitmap))
        {
            Window.ShowMessage("No hay ninguna imagen en el portapapeles");
            return;
        }
        var reference = await content.GetBitmapAsync();
        using var stream = await reference.OpenReadAsync();
        using var bmp = await CanvasBitmap.LoadAsync(_doc!.Device, stream, 96);
        if (_ops!.AddImage(bmp, "Pegado") is not null) StartTransform();
    }

    private async Task AddTextAsync()
    {
        var box = new TextBox { AcceptsReturn = true, Height = 120, TextWrapping = TextWrapping.Wrap, PlaceholderText = "Escribe el texto" };
        if (await Dialogs.ShowAsync(XamlRoot, "Añadir texto", box, "Añadir") != ContentDialogResult.Primary || box.Text.Trim().Length == 0) return;
        var doc = _doc!;
        var layer = doc.CreateLayer(box.Text.Trim().Split('\r', '\n')[0] is var first && first.Length > 24 ? first[..24] : first);
        using (var ds = layer.Surface.CreateDrawingSession())
        using (var fmt = new CanvasTextFormat { FontFamily = CanvasFonts.Display, FontSize = Math.Min(doc.Width, doc.Height) / 10f, FontWeight = FontWeights.SemiBold, HorizontalAlignment = CanvasHorizontalAlignment.Center, VerticalAlignment = CanvasVerticalAlignment.Center })
            ds.DrawText(box.Text.Replace("\r", "\n"), new Windows.Foundation.Rect(0, 0, doc.Width, doc.Height), CurrentColor, fmt);
        if (_ops!.Add(layer) is not null) StartTransform();
    }

    private async Task RenameDocumentAsync()
    {
        var box = new TextBox { Text = _doc!.Name };
        if (await Dialogs.ShowAsync(XamlRoot, "Nombre del lienzo", box, "Aceptar") != ContentDialogResult.Primary || box.Text.Trim().Length == 0) return;
        var doc = _doc;
        doc.Name = box.Text.Trim();
        if (doc.FilePath is { } old && File.Exists(old))
        {
            var target = Services.ProjectStore.NewPath(doc.Name);
            File.Move(old, target);
            doc.FilePath = target;
        }
        doc.IsDirty = true;
        await SaveAsync();
    }

    private async Task ExportBitmapAsync(CanvasBitmap bitmap, string name, CanvasBitmapFileFormat format)
    {
        var (label, ext) = format == CanvasBitmapFileFormat.Jpeg ? ("Imagen JPEG", ".jpg") : ("Imagen PNG", ".png");
        var file = await Window.SavePicker(name, label, ext).PickSaveFileAsync();
        if (file is null) return;
        await WriteBitmapAsync(file, bitmap, format);
        Window.ShowMessage("Exportado: " + file.Name);
    }

    private static async Task WriteBitmapAsync(StorageFile file, CanvasBitmap bitmap, CanvasBitmapFileFormat format)
    {
        using var stream = await file.OpenAsync(FileAccessMode.ReadWrite);
        stream.Size = 0;
        await bitmap.SaveAsync(stream, format, 0.92f);
    }

    private async Task ExportJpegAsync()
    {
        var doc = _doc!;
        using var flat = doc.Flatten(includeBackground: false);
        using var opaque = new CanvasRenderTarget(doc.Device, doc.Width, doc.Height, 96);
        using (var ds = opaque.CreateDrawingSession())
        {
            ds.Clear(doc.BackgroundVisible ? doc.Background : Colors.White);
            ds.DrawImage(flat);
        }
        await ExportBitmapAsync(opaque, doc.Name, CanvasBitmapFileFormat.Jpeg);
    }

    private async Task ExportLayersAsync()
    {
        var picker = new Windows.Storage.Pickers.FolderPicker();
        picker.FileTypeFilter.Add("*");
        WinRT.Interop.InitializeWithWindow.Initialize(picker, Window.Handle);
        var folder = await picker.PickSingleFolderAsync();
        if (folder is null) return;
        var doc = _doc!;
        for (var i = 0; i < doc.Layers.Count; i++)
        {
            var l = doc.Layers[i];
            var safe = string.Concat(l.Name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
            var file = await folder.CreateFileAsync($"{doc.Name} - {i + 1:D2} - {safe}.png", CreationCollisionOption.GenerateUniqueName);
            await WriteBitmapAsync(file, l.Surface, CanvasBitmapFileFormat.Png);
        }
        Window.ShowMessage($"{doc.Layers.Count} capas exportadas");
    }

    /// <summary>EX-01: capas, grupos, máscaras y modos en un PSD que abren Photoshop, Procreate, Krita y Photopea.</summary>
    private async Task ExportPsdAsync()
    {
        var doc = _doc!;
        var file = await Window.SavePicker(doc.Name, "Documento de Photoshop", PsdInterop.Extension).PickSaveFileAsync();
        if (file is null) return;
        try
        {
            var psd = PsdInterop.ToPsd(doc);
            await Task.Run(() =>
            {
                using var stream = File.Create(file.Path);
                Psd.Write(stream, psd);
            });
            Window.ShowMessage("Exportado: " + file.Name);
        }
        catch (Exception e)
        {
            Services.Log.Error("Exportación PSD fallida", e);
            Window.ShowMessage("No se pudo exportar el PSD: " + e.Message, InfoBarSeverity.Error);
        }
    }

    private async Task ExportProjectAsync()
    {
        var doc = _doc!;
        var file = await Window.SavePicker(doc.Name, "Proyecto de Trazo", TrazoFile.Extension).PickSaveFileAsync();
        if (file is null) return;
        var data = await doc.ToProjectDataAsync();
        await ProjectSaver.SaveAsync(file.Path, data);
        Window.ShowMessage("Exportado: " + file.Name);
    }

    private async Task PressureDialogAsync()
    {
        var gamma = new Slider { Header = "Curva de presión (menor = más sensible)", Minimum = 0.3, Maximum = 2.5, StepFrequency = 0.05, Value = _settings.PressureGamma };
        var tol = new Slider { Header = "Umbral de relleno", Minimum = 0, Maximum = 255, Value = _settings.FillTolerance };
        if (await Dialogs.ShowAsync(XamlRoot, "Preferencias del lápiz", new StackPanel { Width = 360, Spacing = 12, Children = { gamma, tol } }, "Guardar") != ContentDialogResult.Primary) return;
        _settings.PressureGamma = (float)gamma.Value;
        _settings.FillTolerance = (int)tol.Value;
        _settings.Save();
    }

    private async Task HelpAsync()
    {
        string[][] rows =
        [
            ["Tocar con dos dedos", "Deshacer"], ["Tocar con tres dedos", "Rehacer"], ["Tocar con cuatro dedos", "Ocultar la interfaz"],
            ["Pellizcar / girar con dos dedos", "Zoom, desplazar y rotar"], ["Pellizco rápido", "Ajustar a la pantalla"],
            ["Tocar y mantener (dedo)", "Cuentagotas"], ["Mantener al final del trazo", "QuickShape"],
            ["Arrastrar el círculo de color", "Relleno"], ["Botón lateral del lápiz", "Cuentagotas"], ["Extremo borrador del lápiz", "Borrador"],
            ["B · E · S", "Pintar · Borrar · Difuminar"], ["[ y ]", "Tamaño del pincel"], ["1 … 9", "Opacidad"],
            ["I · G · V · X", "Cuentagotas · Relleno · Transformar · Color anterior"], ["L · C", "Capas · Color"],
            ["Ctrl+Z · Ctrl+Y", "Deshacer · Rehacer"], ["Ctrl+J · Ctrl+E", "Duplicar · Combinar abajo"],
            ["Espacio + arrastrar · rueda", "Desplazar · Zoom"], ["Alt + rueda · R", "Rotar · Quitar rotación"],
            ["Ctrl+0 · Ctrl+1", "Ajustar · 100 %"], ["Tab", "Pantalla completa"], ["Ctrl+S", "Guardar"],
        ];
        var grid = new Grid { ColumnSpacing = 16, RowSpacing = 6 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        for (var i = 0; i < rows.Length; i++)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var a = new TextBlock { Text = rows[i][0], FontWeight = FontWeights.SemiBold };
            var b = new TextBlock { Text = rows[i][1], Opacity = 0.75 };
            Grid.SetRow(a, i);
            Grid.SetRow(b, i);
            Grid.SetColumn(b, 1);
            grid.Children.Add(a);
            grid.Children.Add(b);
        }
        await Dialogs.ShowAsync(XamlRoot, "Gestos y atajos", new ScrollViewer { MaxHeight = 520, Content = grid }, "Cerrar", "");
    }
}
