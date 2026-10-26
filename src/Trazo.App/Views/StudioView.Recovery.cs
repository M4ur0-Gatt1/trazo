using Microsoft.Graphics.Canvas;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Trazo.App.Services;
using Trazo.Core.IO;
using Trazo.Rendering;
using static Trazo.Core.Localization.Tr;

namespace Trazo.App.Views;

/// <summary>Recuperación tras fallos (RC-01): pérdida de la GPU y copia de emergencia.</summary>
public sealed partial class StudioView
{
    private readonly EmergencySaver _emergency = new();
    private DateTimeOffset? _lastSaved;

    /// <summary>Hora del último guardado terminado con éxito (null si aún no se guardó en esta sesión).</summary>
    public DateTimeOffset? LastSaved => _lastSaved;
    private CanvasDevice? _watchedDevice;
    private bool _recovering;

    /// <summary>Se vigila el dispositivo del documento: si se pierde, se recupera la obra.</summary>
    private void WatchDevice(CanvasDevice device)
    {
        if (ReferenceEquals(_watchedDevice, device)) return;
        if (_watchedDevice is not null) _watchedDevice.DeviceLost -= OnDeviceLost;
        _watchedDevice = device;
        device.DeviceLost += OnDeviceLost;
    }

    private void OnDeviceLost(CanvasDevice sender, object args)
    {
        Log.Error("Se perdió el dispositivo gráfico: se recupera la obra desde el último guardado");
        DispatcherQueue.TryEnqueue(() => _ = RecoverFromDeviceLossAsync());
    }

    /// <summary>¿La excepción viene de haber perdido el dispositivo del documento?</summary>
    public bool IsDeviceLost(Exception e)
    {
        try
        {
            return _doc?.Device.IsDeviceLost(e.HResult) == true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>Avisa de la pérdida (si aún no se hizo) para que la recuperación se dispare una sola vez.</summary>
    public void ReportDeviceLost()
    {
        try
        {
            _doc?.Device.RaiseDeviceLost();
        }
        catch (Exception e)
        {
            Log.Error("No se pudo notificar la pérdida del dispositivo", e);
            DispatcherQueue.TryEnqueue(() => _ = RecoverFromDeviceLossAsync());
        }
    }

    /// <summary>
    /// Descarta todo lo que vivía en la GPU anterior y vuelve a abrir el documento desde el último guardado en el
    /// dispositivo actual, con la misma vista. Se pierden el historial y lo hecho desde ese guardado.
    /// </summary>
    public async Task<bool> RecoverFromDeviceLossAsync()
    {
        if (_recovering || _doc is null) return false;
        _recovering = true;
        try
        {
            var path = _doc.FilePath;
            var since = _lastSaved is { } t ? DateTimeOffset.Now - t : (TimeSpan?)null;
            _autosave.Stop();
            CancelInput();
            DiscardGpuSessions();
            var (scale, offset, rotation) = (_view.Scale, _view.Offset, _view.Rotation);
            var old = _doc;
            StopTimelapse();
            _doc = null;
            try
            {
                old.Dispose();
            }
            catch (Exception e)
            {
                Log.Error("Al liberar el documento del dispositivo perdido", e);
            }
            DiscardDeviceCaches();

            if (path is null || !File.Exists(path))
            {
                Window.ShowMessage(T("La tarjeta gráfica se reinició y la obra aún no se había guardado."), InfoBarSeverity.Error);
                CloseRequested?.Invoke();
                return false;
            }
            var result = await Task.Run(() => ProjectSaver.Load(path));
            var doc = await PaintDocument.FromProjectDataAsync(MainWindow.Device, result.Project, path);
            Open(doc);
            // misma vista que antes del fallo
            _fitPending = false;
            (_view.Scale, _view.Offset, _view.Rotation) = (scale, offset, rotation);
            Canvas.Invalidate();
            var ago = since is { } s ? F(" (hace {0} s)", Math.Max(1, (int)s.TotalSeconds)) : "";
            Window.ShowMessage(F("La tarjeta gráfica se reinició. Se recuperó la obra hasta el último guardado{0}.", ago), InfoBarSeverity.Warning);
            Log.Info("Obra recuperada tras perder el dispositivo gráfico");
            return true;
        }
        catch (Exception e)
        {
            Log.Error("No se pudo recuperar la obra tras perder el dispositivo gráfico", e);
            Window.ShowMessage(T("No se pudo recuperar la obra tras reiniciarse la tarjeta gráfica. El último guardado sigue en la galería."), InfoBarSeverity.Error);
            CloseRequested?.Invoke();
            return false;
        }
        finally
        {
            _recovering = false;
        }
    }

    // herramientas abiertas: sus texturas eran del dispositivo anterior, se cierran sin aplicar
    private void DiscardGpuSessions()
    {
        void Safe(Action a)
        {
            try
            {
                a();
            }
            catch (Exception e)
            {
                Log.Error("Al cerrar una herramienta tras perder el dispositivo", e);
            }
        }
        if (_transform is not null) Safe(_transform.Dispose);
        _transform = null;
        _transformDrag = null;
        TransformButton.IsChecked = false;
        if (_liquify is not null) Safe(_liquify.Dispose);
        _liquify = null;
        _liquifyPointer = null;
        _adjust = null;
        if (_selectMode) ExitSelectMode();
        _guideEdit = false;
        Sheet.Visibility = Visibility.Collapsed;
    }

    // cachés ligadas a la GPU: puntas, granos, miniaturas, vistas previas y el damero
    private void DiscardDeviceCaches()
    {
        _tips = null;
        _grains = null;
        _composition.Dispose();
        _thumbs.Clear();
        _maskThumbs.Clear();
        _previews.Clear();
        _checker?.Dispose();
        _checker = null;
        _engine = null;
        _ops = null;
    }

    /// <summary>
    /// Tras una excepción inesperada: si hay cambios sin guardar, copia «Nombre (recuperado).trazo» junto al
    /// documento sin tocar el archivo principal (como mucho una por minuto).
    /// </summary>
    public async Task EmergencySaveAsync()
    {
        var doc = _doc;
        if (doc?.FilePath is null || !doc.IsDirty || _recovering) return;
        try
        {
            var data = await doc.ToProjectDataAsync();
            var path = await Task.Run(() => _emergency.TrySave(doc.FilePath, data, DateTimeOffset.Now));
            if (path is null) return;
            Log.Info("Copia de emergencia: " + path);
            Window.ShowMessage(F("Ocurrió un error inesperado. Se guardó una copia de seguridad: «{0}».", Path.GetFileNameWithoutExtension(path)), InfoBarSeverity.Warning);
        }
        catch (Exception e)
        {
            Log.Error("No se pudo guardar la copia de emergencia", e);
        }
    }
}
