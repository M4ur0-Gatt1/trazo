using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Shapes;
using System.Runtime.InteropServices.WindowsRuntime;
using Trazo.App.Services;
using Trazo.Core.Colors;
using Trazo.Rendering;
using Windows.UI;
using XCanvas = Microsoft.UI.Xaml.Controls.Canvas;
using static Trazo.Core.Localization.Tr;

namespace Trazo.App.Views;

/// <summary>CO-02: panel de color con disco, armonía, valor y paletas (docs/specs/CO-02-color-panel.md).</summary>
public sealed partial class StudioView
{
    private const double DiscSize = 240;

    /// <summary>Las paletas anteriores a CO-02 pasan al conjunto nuevo la primera vez.</summary>
    private void MigratePalettes()
    {
        if (_settings.Palettes.Count > 0) return;
        var def = _settings.DefaultPaletteId;
        PaletteSet.Migrate(_settings.Palettes, _settings.Palette, DefaultPalette, ref def);
        _settings.DefaultPaletteId = def;
        _settings.Palette = [];
        _settings.Save();
    }

    private void ShowColorPanel(int? tab = null)
    {
        MigratePalettes();
        // cada control se suscribe aquí y se actualiza cuando el color cambia desde otro
        var listeners = new List<(object Owner, Action<Color> Update)>();
        void SetColor(Color c, object? source)
        {
            CurrentColor = c;
            ColorSwatch.Fill = new SolidColorBrush(c);
            foreach (var (owner, update) in listeners)
                if (!ReferenceEquals(owner, source)) update(c);
        }

        var tabs = new[] { T("Disco"), T("Armonía"), T("Valor"), T("Paletas") };
        var selector = new SelectorBar();
        foreach (var t in tabs) selector.Items.Add(new SelectorBarItem { Text = t });
        var host = new ContentControl { HorizontalContentAlignment = HorizontalAlignment.Stretch };
        var pages = new FrameworkElement[]
        {
            DiscPage(listeners, SetColor),
            HarmonyPage(listeners, SetColor),
            ValuePage(listeners, SetColor),
            PalettesPage(SetColor),
        };
        selector.SelectionChanged += (_, _) =>
        {
            var i = selector.Items.IndexOf(selector.SelectedItem);
            if (i < 0) return;
            host.Content = pages[i];
            _settings.ColorTab = i;
            _settings.Save();
        };
        selector.SelectedItem = selector.Items[Math.Clamp(tab ?? _settings.ColorTab, 0, tabs.Length - 1)];

        var content = new StackPanel { Spacing = 8, Width = 340 };
        content.Children.Add(selector);
        content.Children.Add(host);
        if (_settings.RecentColors.Count > 0)
        {
            content.Children.Add(new TextBlock { Text = T("Historial"), Opacity = 0.6, FontSize = 12 });
            content.Children.Add(SwatchGrid(_settings.RecentColors, c => SetColor(c, null)));
        }
        _colorFlyout = new Flyout { Content = new ScrollViewer { MaxHeight = 760, Content = content }, Placement = FlyoutPlacementMode.BottomEdgeAlignedRight };
        _colorFlyout.ShowAt(ColorButton);
    }

    private Flyout? _colorFlyout;

    /// <summary>Cuadrícula de muestras; con <paramref name="onRemove"/>, el clic derecho quita el color.</summary>
    private static VariableSizedWrapGrid SwatchGrid(IEnumerable<string> colors, Action<Color> pick, Action<int>? onRemove = null)
    {
        var panel = new VariableSizedWrapGrid { Orientation = Orientation.Horizontal, MaximumRowsOrColumns = 10, ItemWidth = 32, ItemHeight = 32 };
        var i = 0;
        foreach (var hex in colors)
        {
            var index = i++;
            if (!Rgba.TryParseHex(hex, out var rgba)) continue;
            var color = PaintDocument.ToColor(rgba);
            var b = new Button
            {
                Width = 28, Height = 28, Padding = new Thickness(0), CornerRadius = new CornerRadius(6),
                Background = new SolidColorBrush(color), BorderThickness = new Thickness(1),
            };
            b.Click += (_, _) => pick(color);
            if (onRemove is not null)
            {
                ToolTipService.SetToolTip(b, T("Clic derecho para quitar"));
                b.RightTapped += (_, _) => onRemove(index);
            }
            panel.Children.Add(b);
        }
        return panel;
    }

    // ---------- Disco ----------

    private FrameworkElement DiscPage(List<(object, Action<Color>)> listeners, Action<Color, object?> set)
    {
        var picker = new ColorPicker
        {
            Color = CurrentColor, ColorSpectrumShape = ColorSpectrumShape.Ring, IsAlphaEnabled = false,
            IsMoreButtonVisible = false, IsColorChannelTextInputVisible = false, IsHexInputVisible = false,
        };
        var updating = false;
        picker.ColorChanged += (_, e) =>
        {
            if (!updating) set(e.NewColor, picker);
        };
        listeners.Add((picker, c =>
        {
            updating = true;
            picker.Color = c;
            updating = false;
        }));
        return picker;
    }

    // ---------- Armonía ----------

    private FrameworkElement HarmonyPage(List<(object, Action<Color>)> listeners, Action<Color, object?> set)
    {
        var hsv = ColorMath.RgbToHsv(PaintDocument.ToRgba(CurrentColor));
        var disc = new Image { Width = DiscSize, Height = DiscSize };
        var overlay = new XCanvas { Width = DiscSize, Height = DiscSize, Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent) };
        var grid = new Grid { Width = DiscSize, Height = DiscSize, HorizontalAlignment = HorizontalAlignment.Center };
        grid.Children.Add(disc);
        grid.Children.Add(overlay);
        var swatches = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Center };
        var modes = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
        foreach (var m in Enum.GetValues<HarmonyMode>()) modes.Items.Add(ColorHarmony.Name(m));
        modes.SelectedIndex = (int)_settings.Harmony;
        var brightness = new Slider { Header = T("Brillo"), Minimum = 0, Maximum = 100, Value = hsv.V * 100 };
        var renderedV = -1.0;

        void Render()
        {
            if (Math.Abs(renderedV - hsv.V) > 0.004)
            {
                disc.Source = HueDisc(hsv.V);
                renderedV = hsv.V;
            }
            overlay.Children.Clear();
            swatches.Children.Clear();
            var colors = ColorHarmony.Colors(hsv, _settings.Harmony);
            for (var i = 0; i < colors.Length; i++)
            {
                var c = colors[i];
                var angle = c.H * Math.PI / 180;
                var r = c.S * DiscSize / 2;
                var x = DiscSize / 2 + Math.Cos(angle) * r;
                var y = DiscSize / 2 - Math.Sin(angle) * r;
                var size = i == 0 ? 26 : 18;
                var dot = new Ellipse
                {
                    Width = size, Height = size, StrokeThickness = i == 0 ? 3 : 2,
                    Stroke = new SolidColorBrush(Microsoft.UI.Colors.White), Fill = new SolidColorBrush(PaintDocument.ToColor(c.ToRgba())),
                };
                XCanvas.SetLeft(dot, x - size / 2.0);
                XCanvas.SetTop(dot, y - size / 2.0);
                overlay.Children.Add(dot);
                var pick = PaintDocument.ToColor(c.ToRgba());
                var b = new Button
                {
                    Width = i == 0 ? 52 : 40, Height = 40, Padding = new Thickness(0), CornerRadius = new CornerRadius(8),
                    Background = new SolidColorBrush(pick), BorderThickness = new Thickness(i == 0 ? 2 : 1),
                };
                var linked = c;
                b.Click += (_, _) =>
                {
                    hsv = linked;
                    set(pick, null);
                    Render();
                };
                swatches.Children.Add(b);
            }
        }

        void PickAt(Windows.Foundation.Point p)
        {
            var dx = p.X - DiscSize / 2;
            var dy = DiscSize / 2 - p.Y;
            var h = (Math.Atan2(dy, dx) * 180 / Math.PI + 360) % 360;
            var s = Math.Clamp(Math.Sqrt(dx * dx + dy * dy) / (DiscSize / 2), 0, 1);
            hsv = new Hsv(h, s, hsv.V);
            set(PaintDocument.ToColor(hsv.ToRgba()), grid);
            Render();
        }
        var dragging = false;
        overlay.PointerPressed += (_, e) =>
        {
            dragging = true;
            overlay.CapturePointer(e.Pointer);
            PickAt(e.GetCurrentPoint(overlay).Position);
        };
        overlay.PointerMoved += (_, e) => { if (dragging) PickAt(e.GetCurrentPoint(overlay).Position); };
        overlay.PointerReleased += (_, e) =>
        {
            dragging = false;
            overlay.ReleasePointerCapture(e.Pointer);
        };
        brightness.ValueChanged += (_, e) =>
        {
            if (Math.Abs(hsv.V * 100 - e.NewValue) < 0.01) return;
            hsv = hsv with { V = e.NewValue / 100 };
            set(PaintDocument.ToColor(hsv.ToRgba()), grid);
            Render();
        };
        modes.SelectionChanged += (_, _) =>
        {
            if (modes.SelectedIndex < 0) return;
            _settings.Harmony = (HarmonyMode)modes.SelectedIndex;
            _settings.Save();
            Render();
        };
        listeners.Add((grid, c =>
        {
            var n = ColorMath.RgbToHsv(PaintDocument.ToRgba(c));
            // un gris no tiene tono: se conserva el anterior para que el punto no salte
            hsv = n.S < 0.001 ? hsv with { S = n.S, V = n.V } : n;
            brightness.Value = hsv.V * 100;
            Render();
        }));
        Render();
        return new StackPanel { Spacing = 10, Children = { modes, grid, brightness, swatches } };
    }

    /// <summary>Disco de tono (ángulo) y saturación (radio) con el brillo dado.</summary>
    private static WriteableBitmap HueDisc(double v)
    {
        const int n = (int)DiscSize;
        var bmp = new WriteableBitmap(n, n);
        var px = new byte[n * n * 4];
        var r0 = n / 2.0;
        for (var y = 0; y < n; y++)
            for (var x = 0; x < n; x++)
            {
                double dx = x + 0.5 - r0, dy = r0 - (y + 0.5);
                var d = Math.Sqrt(dx * dx + dy * dy) / r0;
                var i = (y * n + x) * 4;
                if (d > 1)
                {
                    // fuera del disco: transparente, con borde suavizado
                    if (d > 1 + 1.5 / r0) continue;
                }
                var c = new Hsv((Math.Atan2(dy, dx) * 180 / Math.PI + 360) % 360, Math.Min(1, d), v).ToRgba();
                var a = d <= 1 ? 1.0 : Math.Max(0, 1 - (d - 1) * r0 / 1.5);
                // BGRA premultiplicado
                px[i] = (byte)(c.B * a);
                px[i + 1] = (byte)(c.G * a);
                px[i + 2] = (byte)(c.R * a);
                px[i + 3] = (byte)(255 * a);
            }
        using (var s = bmp.PixelBuffer.AsStream()) s.Write(px);
        bmp.Invalidate();
        return bmp;
    }

    // ---------- Valor ----------

    private FrameworkElement ValuePage(List<(object, Action<Color>)> listeners, Action<Color, object?> set)
    {
        var panel = new StackPanel { Spacing = 2 };
        var updating = false;
        var rgba = PaintDocument.ToRgba(CurrentColor);
        var hsv = ColorMath.RgbToHsv(rgba);
        var hex = new TextBox { Header = T("Hexadecimal"), Text = rgba.ToHex(), MaxLength = 7 };
        var rows = new List<(Slider Slider, Func<double> Get)>();

        void Push(Rgba c, Hsv h)
        {
            rgba = c;
            hsv = h;
            set(PaintDocument.ToColor(c), panel);
            Refresh();
        }
        void Refresh()
        {
            updating = true;
            foreach (var (slider, get) in rows) slider.Value = get();
            if (!ReferenceEquals(Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(XamlRoot), hex)) hex.Text = rgba.ToHex();
            updating = false;
        }
        void Row(string label, double max, Func<double> get, Action<double> apply)
        {
            var s = new Slider { Header = label, Minimum = 0, Maximum = max, StepFrequency = 1, Value = get() };
            s.ValueChanged += (_, e) =>
            {
                if (updating) return;
                apply(e.NewValue);
            };
            rows.Add((s, get));
            panel.Children.Add(s);
        }
        Row(T("Tono"), 360, () => Math.Round(hsv.H), v => Push((hsv with { H = v }).ToRgba(), hsv with { H = v }));
        Row(T("Saturación"), 100, () => Math.Round(hsv.S * 100), v => Push((hsv with { S = v / 100 }).ToRgba(), hsv with { S = v / 100 }));
        Row(T("Brillo"), 100, () => Math.Round(hsv.V * 100), v => Push((hsv with { V = v / 100 }).ToRgba(), hsv with { V = v / 100 }));
        Row(T("Rojo"), 255, () => rgba.R, v => { var c = rgba with { R = (byte)v }; Push(c, Keep(ColorMath.RgbToHsv(c))); });
        Row(T("Verde"), 255, () => rgba.G, v => { var c = rgba with { G = (byte)v }; Push(c, Keep(ColorMath.RgbToHsv(c))); });
        Row(T("Azul"), 255, () => rgba.B, v => { var c = rgba with { B = (byte)v }; Push(c, Keep(ColorMath.RgbToHsv(c))); });
        // un gris no tiene tono: se conserva el que había para que el deslizador no salte a 0
        Hsv Keep(Hsv n) => n.S < 0.001 ? n with { H = hsv.H } : n;
        hex.TextChanged += (_, _) =>
        {
            if (updating) return;
            var text = hex.Text.StartsWith('#') ? hex.Text : "#" + hex.Text;
            if (text.Length == 7 && Rgba.TryParseHex(text, out var c)) Push(c, Keep(ColorMath.RgbToHsv(c)));
        };
        panel.Children.Add(hex);
        listeners.Add((panel, c =>
        {
            rgba = PaintDocument.ToRgba(c) with { A = 255 };
            hsv = Keep(ColorMath.RgbToHsv(rgba));
            Refresh();
        }));
        return panel;
    }

    // ---------- Paletas ----------

    private FrameworkElement PalettesPage(Action<Color, object?> set)
    {
        var root = new StackPanel { Spacing = 8 };
        var current = PaletteSet.Default(_settings.Palettes, _settings.DefaultPaletteId);

        void Save()
        {
            _settings.Save();
            Build();
        }

        void Build()
        {
            root.Children.Clear();
            var combo = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
            foreach (var p in _settings.Palettes)
                combo.Items.Add(p.Id == _settings.DefaultPaletteId ? $"★ {p.Name}" : p.Name);
            combo.SelectedIndex = current is null ? -1 : _settings.Palettes.IndexOf(current);
            combo.SelectionChanged += (_, _) =>
            {
                if (combo.SelectedIndex < 0) return;
                current = _settings.Palettes[combo.SelectedIndex];
                Build();
            };
            root.Children.Add(combo);
            if (current is not null)
            {
                var pal = current;
                root.Children.Add(pal.Colors.Count == 0
                    ? new TextBlock { Text = T("Paleta vacía: añade el color actual."), Opacity = 0.6, FontSize = 12 }
                    : SwatchGrid(pal.Colors, c => set(c, null), i =>
                    {
                        pal.Colors.RemoveAt(i);
                        Save();
                    }));
            }
            Button B(string text, Func<Task> action, bool enabled = true)
            {
                var b = new Button { Content = text, IsEnabled = enabled, Padding = new Thickness(10, 4, 10, 5), HorizontalAlignment = HorizontalAlignment.Stretch, Margin = new Thickness(0, 0, 6, 6) };
                b.Click += async (_, _) => await action();
                return b;
            }
            var has = current is not null;
            root.Children.Add(new VariableSizedWrapGrid
            {
                Orientation = Orientation.Horizontal, ItemHeight = 40, ItemWidth = 170, MaximumRowsOrColumns = 2,
                Children =
                {
                    B(T("Añadir color"), () =>
                    {
                        if (current is null || current.Colors.Count >= PaletteFiles.MaxColors) return Task.CompletedTask;
                        current.Colors.Add(PaintDocument.ToRgba(CurrentColor).ToHex());
                        Save();
                        return Task.CompletedTask;
                    }, has),
                    B(T("Nueva"), async () =>
                    {
                        if (await AskName(T("Nueva paleta"), T("Paleta")) is not { } name) return;
                        current = new PaletteData { Name = name };
                        _settings.Palettes.Add(current);
                        Save();
                    }),
                    B(T("Renombrar"), async () =>
                    {
                        if (current is null || await AskName(T("Renombrar paleta"), current.Name) is not { } name) return;
                        current.Name = name;
                        Save();
                    }, has),
                    B(T("Predeterminada"), () =>
                    {
                        _settings.DefaultPaletteId = current?.Id;
                        Save();
                        return Task.CompletedTask;
                    }, has && current!.Id != _settings.DefaultPaletteId),
                    B(T("Importar…"), ImportPalettesAsync),
                    B(T("Exportar…"), () => ExportPaletteAsync(current!), has && current!.Colors.Count > 0),
                    B(T("Eliminar"), () =>
                    {
                        if (current is null) return Task.CompletedTask;
                        _settings.Palettes.Remove(current);
                        current = PaletteSet.Default(_settings.Palettes, _settings.DefaultPaletteId);
                        Save();
                        return Task.CompletedTask;
                    }, has),
                },
            });
        }

        async Task ImportPalettesAsync()
        {
            var file = await Window.OpenPicker(".swatches", ".ase").PickSingleFileAsync();
            if (file is null) return;
            var added = await ImportPaletteFileAsync(file.Path);
            if (added.Count > 0) current = added[0];
            _colorFlyout?.Hide();
            ShowColorPanel(3);
        }

        Build();
        return root;
    }

    /// <summary>Importa un .swatches o un .ase; devuelve las paletas añadidas (vacío si falla, con aviso).</summary>
    internal async Task<List<PaletteData>> ImportPaletteFileAsync(string path)
    {
        MigratePalettes();
        var fallback = PaletteFiles.CleanName(System.IO.Path.GetFileNameWithoutExtension(path), T("Paleta"));
        try
        {
            var bytes = await File.ReadAllBytesAsync(path);
            var import = path.EndsWith(".ase", StringComparison.OrdinalIgnoreCase)
                ? PaletteFiles.ReadAse(bytes, fallback)
                : new PaletteImport([PaletteFiles.ReadSwatches(new MemoryStream(bytes), fallback)], false);
            var added = import.Palettes.Select(PaletteSet.From).ToList();
            _settings.Palettes.AddRange(added);
            _settings.Save();
            Window.ShowMessage(import.Approximated
                ? F("{0} paletas importadas. Algunos colores CMYK o Lab se convirtieron de forma aproximada.", added.Count)
                : F("{0} paletas importadas.", added.Count));
            return added;
        }
        catch (InvalidDataException e)
        {
            Window.ShowMessage(e.Message, InfoBarSeverity.Error);
            return [];
        }
        catch (IOException e)
        {
            Log.Error("Importar paleta", e);
            Window.ShowMessage(T("No se pudo leer el archivo."), InfoBarSeverity.Error);
            return [];
        }
    }

    private async Task ExportPaletteAsync(PaletteData data)
    {
        var picker = new Windows.Storage.Pickers.FileSavePicker { SuggestedFileName = data.Name };
        picker.FileTypeChoices.Add(T("Paleta de Procreate"), [".swatches"]);
        picker.FileTypeChoices.Add(T("Muestras de Adobe"), [".ase"]);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, Window.Handle);
        var file = await picker.PickSaveFileAsync();
        if (file is null) return;
        await using (var fs = File.Create(file.Path))
        {
            var palette = PaletteSet.ToPalette(data);
            if (file.FileType.Equals(".ase", StringComparison.OrdinalIgnoreCase)) PaletteFiles.WriteAse(palette, fs);
            else PaletteFiles.WriteSwatches(palette, fs);
        }
        Window.ShowMessage(T("Exportado: ") + file.Name);
    }

    private async Task<string?> AskName(string title, string initial)
    {
        var box = new TextBox { Text = initial, MaxLength = PaletteFiles.MaxName };
        _colorFlyout?.Hide();
        var ok = await Dialogs.ShowAsync(XamlRoot, title, box, T("Aceptar")) == ContentDialogResult.Primary && box.Text.Trim().Length > 0;
        ShowColorPanel(3);
        return ok ? PaletteFiles.CleanName(box.Text, initial) : null;
    }
}
