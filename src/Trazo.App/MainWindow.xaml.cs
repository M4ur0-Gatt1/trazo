using Microsoft.Graphics.Canvas;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Trazo.Rendering;
using Trazo.App.Services;
using Trazo.Core.IO;
using Windows.Storage.Pickers;
using static Trazo.Core.Localization.Tr;

namespace Trazo.App;

public sealed partial class MainWindow : Window
{
    private readonly DispatcherTimer _toastTimer = new();

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
    /// <summary>El estudio, para la recuperación tras fallos desde App (RC-01).</summary>
    public Views.StudioView StudioPage => Studio;

    public void ShowMessage(string text, InfoBarSeverity severity = InfoBarSeverity.Informational)
    {
        Toast.Message = text;
        Toast.Severity = severity;
        Toast.IsOpen = true;
        _toastTimer.Stop();
        // un aviso o un error tiene que dar tiempo a leerlo (antes desaparecían todos a los 2,2 s; FA-01)
        _toastTimer.Interval = TimeSpan.FromSeconds(severity is InfoBarSeverity.Warning or InfoBarSeverity.Error ? 8 : 2.2);
        _toastTimer.Start();
    }

    /// <summary>
    /// Trazo se está ejecutando como administrador (por ejemplo, abierto desde el instalador). Windows no deja usar los
    /// selectores de archivos en una app de WinUI elevada: fallaban sin avisar al importar, abrir o exportar.
    /// </summary>
    public static bool IsElevated { get; } = ComputeElevated();

    private static bool ComputeElevated()
    {
        try
        {
            using var id = System.Security.Principal.WindowsIdentity.GetCurrent();
            // con UAC, el grupo de administradores solo cuenta si el proceso está elevado
            return new System.Security.Principal.WindowsPrincipal(id).IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
        }
        catch (System.Security.SecurityException)
        {
            return false;
        }
    }

    public static string ElevatedMessage => T("Trazo se está ejecutando como administrador y Windows no deja abrir ni guardar archivos así. Ciérralo y vuelve a abrirlo desde el menú Inicio.");

    /// <summary>Abre un selector de archivos; si Windows lo impide, avisa en lugar de fallar en silencio (devuelve null).</summary>
    public async Task<TResult?> PickAsync<TResult>(Func<Windows.Foundation.IAsyncOperation<TResult>> pick) where TResult : class
    {
        try
        {
            return await pick();
        }
        catch (Exception e)
        {
            Log.Error("Selector de archivos", e);
            ShowMessage(IsElevated ? ElevatedMessage : T("No se pudo abrir el selector de archivos: ") + e.Message, InfoBarSeverity.Error);
            return null;
        }
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

    internal void SetDocumentTitle(string name) => TitleText.Text = $"{name} — Trazo";

    internal void ShowStudio(PaintDocument doc)
    {
        Gallery.Visibility = Visibility.Collapsed;
        Studio.Visibility = Visibility.Visible;
        Studio.Open(doc);
        SetDocumentTitle(doc.Name);
    }

    private void CreateDocument(int width, int height, string name)
    {
        var doc = PaintDocument.CreateBlank(Device, width, height, name);
        doc.FilePath = ProjectStore.NewPath(name);
        doc.IsDirty = true;
        ShowStudio(doc);
        _ = Studio.SaveAsync();
    }

    private bool _opening;

    public async Task OpenFileAsync(string path)
    {
        // un doble toque en la galería no abre dos copias, y no se abre otra encima de una obra abierta (auditoría 2026-11-02)
        if (_opening || Studio.Visibility == Visibility.Visible) return;
        _opening = true;
        PaintDocument? doc = null;
        try
        {
            var result = await Task.Run(() => ProjectSaver.Load(path));
            doc = await PaintDocument.FromProjectDataAsync(Device, result.Project, path);
            if (result.Recovered)
            {
                Log.Error($"Recuperado {path} desde {result.Source}: {result.Problem}");
                ShowMessage(result.Source == LoadSource.Backup
                    ? T("El archivo estaba dañado y se recuperó la copia anterior. Puede faltar el último trabajo.")
                    : T("Se recuperó un guardado interrumpido."), InfoBarSeverity.Warning);
                doc.IsDirty = true; // se vuelve a guardar sano en el archivo principal
            }
            // los archivos del formato web antiguo se guardan como .trazo v2 en la carpeta de proyectos
            if (!ProjectStore.Contains(path)) doc.FilePath = ProjectStore.NewPath(doc.Name);
            // en la galería, el nombre de la obra es el de su archivo: tras renombrar o duplicar allí, coinciden
            else doc.Name = Path.GetFileNameWithoutExtension(path);
            ShowStudio(doc);
            doc = null;
        }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException or FormatException)
        {
            Log.Error($"No se pudo abrir {path}", e);
            ShowMessage(T("No se pudo abrir el archivo: ") + e.Message, InfoBarSeverity.Error);
        }
        catch (Exception e)
        {
            // GPU sin memoria, imagen que no se puede decodificar…: antes no se avisaba de nada
            Log.Error($"No se pudo abrir {path}", e);
            ShowMessage(T("No se pudo abrir el archivo."), InfoBarSeverity.Error);
        }
        finally
        {
            doc?.Dispose(); // documento a medio crear que no llegó al estudio
            _opening = false;
        }
    }

    private async Task ImportAsync()
    {
        var file = await PickAsync(() => OpenPicker(".trazo", ".psd", ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".tif", ".tiff", ".webp").PickSingleFileAsync());
        if (file is null) return;
        if (file.FileType.Equals(".trazo", StringComparison.OrdinalIgnoreCase))
        {
            await OpenFileAsync(file.Path);
            return;
        }
        if (file.FileType.Equals(PsdInterop.Extension, StringComparison.OrdinalIgnoreCase))
        {
            await ImportPsdAsync(file.Path);
            return;
        }
        await ImportImageAsync(file.Path);
    }

    /// <summary>
    /// FA-01: archivo recibido desde el Explorador (doble clic con Trazo como programa predeterminado, «Abrir con»,
    /// arrastrar sobre el icono). Un .trazo se abre; un PSD o una imagen se importan como obra nueva.
    /// </summary>
    internal async Task OpenFromExplorerAsync(string path)
    {
        Log.Info("Abrir desde el Explorador: " + path);
        switch (LaunchFile.Classify(path))
        {
            case LaunchKind.Project:
                if (SessionJournal.IsOpenElsewhere(SessionsFolder, path, Environment.ProcessId))
                {
                    ShowMessage(F("«{0}» ya está abierta en otra ventana de Trazo.", Path.GetFileNameWithoutExtension(path)), InfoBarSeverity.Warning);
                    return;
                }
                await OpenFileAsync(path);
                break;
            case LaunchKind.Psd:
                await ImportPsdAsync(path);
                break;
            case LaunchKind.Image:
                await ImportImageAsync(path);
                break;
            default:
                ShowMessage(F("Trazo no abre archivos {0}. Abre obras de Trazo, PSD e imágenes (PNG, JPG, BMP, GIF, TIFF, WebP).", Path.GetExtension(path)), InfoBarSeverity.Error);
                break;
        }
    }

    /// <summary>Una imagen como obra nueva de una capa, guardada en la galería.</summary>
    internal async Task ImportImageAsync(string path)
    {
        try
        {
            using var file = File.OpenRead(path);
            using var stream = file.AsRandomAccessStream();
            using var bmp = await CanvasBitmap.LoadAsync(Device, stream, 96);
            // RS-01: una imagen pequeña se agranda (×2 a ×4) para que lo que se dibuje encima no se vea en escalones
            var (iw, ih) = ((int)bmp.SizeInPixels.Width, (int)bmp.SizeInPixels.Height);
            var k = Core.Documents.CanvasLimits.ImportScale(iw, ih);
            var (w, h) = Core.Documents.CanvasLimits.Fit((double)iw * k, (double)ih * k);
            var doc = PaintDocument.CreateBlank(Device, w, h, Path.GetFileNameWithoutExtension(path));
            doc.Layers[0].Name = T("Imagen");
            using (var ds = doc.Layers[0].Surface.CreateDrawingSession())
                ds.DrawImage(bmp, new Windows.Foundation.Rect(0, 0, w, h), bmp.Bounds, 1, CanvasImageInterpolation.HighQualityCubic);
            doc.FilePath = ProjectStore.NewPath(doc.Name);
            doc.IsDirty = true;
            ShowStudio(doc);
            await Studio.SaveAsync();
            if (k > 1) ShowMessage(F("La imagen ({0}×{1}) se agrandó a {2}×{3} para que lo que dibujes se vea nítido.", iw, ih, w, h));
        }
        catch (Exception e)
        {
            Log.Error("Importación fallida", e);
            ShowMessage(T("No se pudo importar la imagen"), InfoBarSeverity.Error);
        }
    }

    /// <summary>EX-01: abre un PSD como documento nuevo y avisa de lo que no se pudo conservar igual.</summary>
    internal async Task ImportPsdAsync(string path)
    {
        try
        {
            var name = Path.GetFileNameWithoutExtension(path);
            var psd = await Task.Run(() =>
            {
                using var stream = File.OpenRead(path);
                return Core.IO.Psd.Read(stream);
            });
            var (doc, warnings) = PsdInterop.FromPsd(Device, psd, name);
            doc.FilePath = ProjectStore.NewPath(doc.Name);
            doc.IsDirty = true;
            ShowStudio(doc);
            await Studio.SaveAsync();
            if (warnings.Count > 0) ShowMessage(T("PSD importado. ") + string.Join(" ", warnings), InfoBarSeverity.Warning);
        }
        catch (InvalidDataException e)
        {
            Log.Info("PSD no válido: " + e.Message);
            ShowMessage(e.Message, InfoBarSeverity.Error);
        }
        catch (Exception e)
        {
            Log.Error("Importación PSD fallida", e);
            ShowMessage(T("No se pudo importar el PSD"), InfoBarSeverity.Error);
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
        App.Journal.End(); // la autoprueba termina bien: no debe parecer un cierre inesperado
        Environment.Exit(ok ? 0 : 1);
    }

    private async Task CloseDocumentAsync()
    {
        if (!await Studio.CloseAsync()) return;
        Studio.Visibility = Visibility.Collapsed;
        Gallery.Visibility = Visibility.Visible;
        TitleText.Text = "Trazo";
        await Gallery.RefreshAsync();
    }

    private bool _readyToClose, _closingWindow;

    // Al cerrar la ventana se cierra la obra como al volver a la galería (guardado completo, time-lapse incluido) y se
    // guardan las preferencias pendientes; si el guardado falla y el usuario prefiere seguir, la ventana no se cierra
    private async void OnClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (_readyToClose)
        {
            App.Journal.End(); // cierre normal: no hay nada que recuperar al volver
            return;
        }
        args.Cancel = true;
        Log.Info("Cerrar la ventana"); // i18n: interno
        if (_closingWindow) return; // segundo clic en la X mientras se guarda
        _closingWindow = true;
        try
        {
            if (!await Studio.CloseAsync()) return;
            Services.Settings.Current.Flush();
            _readyToClose = true;
            Close();
        }
        finally
        {
            _closingWindow = false;
        }
    }
}
