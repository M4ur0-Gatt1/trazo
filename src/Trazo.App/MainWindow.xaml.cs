using Microsoft.Graphics.Canvas;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Trazo.App.Painting;
using Trazo.App.Services;
using Trazo.Core.IO;
using Windows.Storage.Pickers;

namespace Trazo.App;

public sealed partial class MainWindow : Window
{
    private readonly DispatcherTimer _toastTimer = new() { Interval = TimeSpan.FromSeconds(2.2) };

    public MainWindow()
    {
        InitializeComponent();
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(TitleBar);
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "Trazo.ico"));
        AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Standard;
        if (AppWindow.Presenter is OverlappedPresenter p) p.Maximize();

        _toastTimer.Tick += (_, _) => { Toast.IsOpen = false; _toastTimer.Stop(); };
        Gallery.OpenRequested += path => _ = OpenFileAsync(path);
        Gallery.CreateRequested += (w, h, name) => CreateDocument(w, h, name);
        Gallery.ImportRequested += () => _ = ImportAsync();
        Studio.CloseRequested += () => _ = CloseDocumentAsync();
        AppWindow.Closing += OnClosing;
        _ = Gallery.RefreshAsync();
    }

    public nint Handle => WinRT.Interop.WindowNative.GetWindowHandle(this);
    public static CanvasDevice Device => CanvasDevice.GetSharedDevice();

    public void ShowMessage(string text, InfoBarSeverity severity = InfoBarSeverity.Informational)
    {
        Toast.Message = text;
        Toast.Severity = severity;
        Toast.IsOpen = true;
        _toastTimer.Stop();
        _toastTimer.Start();
    }

    public FileOpenPicker OpenPicker(params string[] extensions)
    {
        var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.PicturesLibrary, ViewMode = PickerViewMode.Thumbnail };
        foreach (var e in extensions) picker.FileTypeFilter.Add(e);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, Handle);
        return picker;
    }

    public FileSavePicker SavePicker(string suggestedName, string label, params string[] extensions)
    {
        var picker = new FileSavePicker { SuggestedStartLocation = PickerLocationId.PicturesLibrary, SuggestedFileName = suggestedName };
        picker.FileTypeChoices.Add(label, extensions.ToList());
        WinRT.Interop.InitializeWithWindow.Initialize(picker, Handle);
        return picker;
    }

    private void ShowStudio(PaintDocument doc)
    {
        Gallery.Visibility = Visibility.Collapsed;
        Studio.Visibility = Visibility.Visible;
        Studio.Open(doc);
        TitleText.Text = $"{doc.Name} — Trazo";
    }

    private void CreateDocument(int width, int height, string name)
    {
        var doc = PaintDocument.CreateBlank(Device, width, height, name);
        doc.FilePath = ProjectStore.NewPath(name);
        doc.IsDirty = true;
        ShowStudio(doc);
        _ = Studio.SaveAsync();
    }

    public async Task OpenFileAsync(string path)
    {
        try
        {
            ProjectData data;
            await using (var fs = File.OpenRead(path)) data = await Task.Run(() => TrazoFile.Read(fs));
            var doc = await PaintDocument.FromProjectDataAsync(Device, data, path);
            // los archivos del formato web antiguo se guardan como .trazo v2 en la carpeta de proyectos
            if (!path.StartsWith(ProjectStore.Folder, StringComparison.OrdinalIgnoreCase)) doc.FilePath = ProjectStore.NewPath(doc.Name);
            ShowStudio(doc);
        }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException or FormatException)
        {
            Log.Error($"No se pudo abrir {path}", e);
            ShowMessage("No se pudo abrir el archivo: " + e.Message, InfoBarSeverity.Error);
        }
    }

    private async Task ImportAsync()
    {
        var file = await OpenPicker(".trazo", ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".tif", ".tiff", ".webp").PickSingleFileAsync();
        if (file is null) return;
        if (file.FileType.Equals(".trazo", StringComparison.OrdinalIgnoreCase))
        {
            await OpenFileAsync(file.Path);
            return;
        }
        try
        {
            using var stream = await file.OpenReadAsync();
            using var bmp = await CanvasBitmap.LoadAsync(Device, stream, 96);
            var (w, h) = Core.Documents.CanvasLimits.Fit(bmp.SizeInPixels.Width, bmp.SizeInPixels.Height);
            var doc = PaintDocument.CreateBlank(Device, w, h, Path.GetFileNameWithoutExtension(file.Name));
            doc.Layers[0].Name = "Imagen";
            using (var ds = doc.Layers[0].Surface.CreateDrawingSession())
                ds.DrawImage(bmp, new Windows.Foundation.Rect(0, 0, w, h), bmp.Bounds, 1, CanvasImageInterpolation.HighQualityCubic);
            doc.FilePath = ProjectStore.NewPath(doc.Name);
            doc.IsDirty = true;
            ShowStudio(doc);
            await Studio.SaveAsync();
        }
        catch (Exception e)
        {
            Log.Error("Importación fallida", e);
            ShowMessage("No se pudo importar la imagen", InfoBarSeverity.Error);
        }
    }

    /// <summary>Autoprueba sin interacción (Trazo.exe --selftest carpeta). Sale con código 0 si todo va bien.</summary>
    public async Task RunSelfTestAsync(string folder)
    {
        Directory.CreateDirectory(folder);
        var doc = PaintDocument.CreateBlank(Device, 1600, 1000, "Autoprueba");
        doc.FilePath = Path.Combine(folder, "autoprueba" + TrazoFile.Extension);
        ShowStudio(doc);
        await Task.Delay(1000);
        var ok = await Studio.RunSelfTestAsync(folder);
        await Task.Delay(5000); // deja la ventana visible para capturarla
        Environment.Exit(ok ? 0 : 1);
    }

    private async Task CloseDocumentAsync()
    {
        await Studio.CloseAsync();
        Studio.Visibility = Visibility.Collapsed;
        Gallery.Visibility = Visibility.Visible;
        TitleText.Text = "Trazo";
        await Gallery.RefreshAsync();
    }

    private bool _savedBeforeClose;

    // Al cerrar con cambios sin guardar, se cancela el cierre, se guarda y se vuelve a cerrar
    private async void OnClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (_savedBeforeClose || !Studio.HasUnsavedChanges) return;
        args.Cancel = true;
        await Studio.SaveAsync();
        _savedBeforeClose = true;
        Close();
    }
}
