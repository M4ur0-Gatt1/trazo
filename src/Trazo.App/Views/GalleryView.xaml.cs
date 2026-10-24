using System.Diagnostics;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Imaging;
using Trazo.App.Services;
using Trazo.Core.Documents;
using Windows.Storage.Streams;

namespace Trazo.App.Views;

public sealed class GalleryItem(ProjectEntry entry, BitmapImage? thumbnail)
{
    public ProjectEntry Entry { get; } = entry;
    public string Name => Entry.Name;
    public string Subtitle => Entry.Modified.ToString("g");
    public BitmapImage? Thumbnail { get; } = thumbnail;
}

public sealed partial class GalleryView : UserControl
{
    public event Action<string>? OpenRequested;
    public event Action<int, int, string>? CreateRequested;
    public event Action? ImportRequested;

    public GalleryView()
    {
        InitializeComponent();
        foreach (var p in CanvasLimits.Presets)
        {
            var item = new MenuFlyoutItem { Text = $"{p.Name}  ·  {p.Width} × {p.Height}" };
            item.Click += (_, _) => CreateRequested?.Invoke(p.Width, p.Height, "Sin título");
            NewMenu.Items.Add(item);
        }
        var screen = new MenuFlyoutItem { Text = "Tamaño de pantalla" };
        screen.Click += (_, _) =>
        {
            var area = Microsoft.UI.Windowing.DisplayArea.Primary.OuterBounds;
            var (w, h) = CanvasLimits.Fit(area.Width, area.Height);
            CreateRequested?.Invoke(w, h, "Sin título");
        };
        NewMenu.Items.Insert(0, screen);
        NewMenu.Items.Add(new MenuFlyoutSeparator());
        var custom = new MenuFlyoutItem { Text = "Tamaño personalizado…" };
        custom.Click += async (_, _) => await CustomSizeAsync();
        NewMenu.Items.Add(custom);
    }

    public async Task RefreshAsync()
    {
        var entries = await ProjectStore.ListAsync();
        var items = new List<GalleryItem>();
        foreach (var e in entries)
        {
            BitmapImage? img = null;
            if (e.Thumbnail is { Length: > 0 } bytes)
            {
                img = new BitmapImage();
                using var stream = new InMemoryRandomAccessStream();
                await stream.WriteAsync(bytes.AsBuffer());
                stream.Seek(0);
                await img.SetSourceAsync(stream);
            }
            items.Add(new GalleryItem(e, img));
        }
        Grid.ItemsSource = items;
        Empty.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Grid_ItemClick(object sender, ItemClickEventArgs e) =>
        OpenRequested?.Invoke(((GalleryItem)e.ClickedItem).Entry.Path);

    private void Import_Click(object sender, RoutedEventArgs e) => ImportRequested?.Invoke();

    private void OpenFolder_Click(object sender, RoutedEventArgs e) =>
        Process.Start(new ProcessStartInfo { FileName = ProjectStore.Folder, UseShellExecute = true });

    private void Item_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not GalleryItem item) return;
        var menu = new MenuFlyout();
        void Add(string text, Func<Task> action)
        {
            var mi = new MenuFlyoutItem { Text = text };
            mi.Click += async (_, _) => await action();
            menu.Items.Add(mi);
        }
        Add("Abrir", () => { OpenRequested?.Invoke(item.Entry.Path); return Task.CompletedTask; });
        Add("Cambiar nombre…", () => RenameAsync(item));
        Add("Duplicar", async () =>
        {
            File.Copy(item.Entry.Path, ProjectStore.NewPath(item.Name + " copia"));
            await RefreshAsync();
        });
        Add("Mostrar en el Explorador", () =>
        {
            Process.Start("explorer.exe", $"/select,\"{item.Entry.Path}\"");
            return Task.CompletedTask;
        });
        menu.Items.Add(new MenuFlyoutSeparator());
        Add("Eliminar", () => DeleteAsync(item));
        menu.ShowAt((FrameworkElement)sender, e.GetPosition((FrameworkElement)sender));
    }

    private async Task RenameAsync(GalleryItem item)
    {
        var box = new TextBox { Text = item.Name };
        if (await Dialogs.ShowAsync(XamlRoot, "Cambiar nombre", box, "Aceptar") != ContentDialogResult.Primary) return;
        var target = ProjectStore.NewPath(box.Text);
        File.Move(item.Entry.Path, target);
        await RefreshAsync();
    }

    private async Task DeleteAsync(GalleryItem item)
    {
        var text = new TextBlock { Text = $"«{item.Name}» se enviará a la papelera de reciclaje.", TextWrapping = TextWrapping.Wrap };
        if (await Dialogs.ShowAsync(XamlRoot, "Eliminar obra", text, "Eliminar") != ContentDialogResult.Primary) return;
        // StorageDeleteOption.Default envía el archivo a la papelera de reciclaje
        var file = await Windows.Storage.StorageFile.GetFileFromPathAsync(item.Entry.Path);
        await file.DeleteAsync(Windows.Storage.StorageDeleteOption.Default);
        await RefreshAsync();
    }

    private async Task CustomSizeAsync()
    {
        var name = new TextBox { Header = "Nombre", Text = "Sin título" };
        var w = new NumberBox { Header = "Ancho (px)", Value = 2048, Minimum = CanvasLimits.MinSide, Maximum = CanvasLimits.MaxSide, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline };
        var h = new NumberBox { Header = "Alto (px)", Value = 1536, Minimum = CanvasLimits.MinSide, Maximum = CanvasLimits.MaxSide, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline };
        var hint = new TextBlock { Text = $"Máximo {CanvasLimits.MaxSide} px por lado y {CanvasLimits.MaxArea / 1024 / 1024} megapíxeles.", Opacity = 0.6, FontSize = 12 };
        var panel = new StackPanel { Spacing = 12, Children = { name, w, h, hint } };
        if (await Dialogs.ShowAsync(XamlRoot, "Lienzo personalizado", panel, "Crear") != ContentDialogResult.Primary) return;
        if (double.IsNaN(w.Value) || double.IsNaN(h.Value)) return;
        var (fw, fh) = CanvasLimits.Fit(w.Value, h.Value);
        CreateRequested?.Invoke(fw, fh, string.IsNullOrWhiteSpace(name.Text) ? "Sin título" : name.Text.Trim());
    }
}

public static class Dialogs
{
    public static async Task<ContentDialogResult> ShowAsync(XamlRoot root, string title, object content, string primary, string close = "Cancelar")
    {
        var dlg = new ContentDialog
        {
            XamlRoot = root,
            Title = title,
            Content = content,
            PrimaryButtonText = primary,
            CloseButtonText = close,
            DefaultButton = ContentDialogButton.Primary,
            RequestedTheme = ElementTheme.Dark,
        };
        return await dlg.ShowAsync();
    }
}
