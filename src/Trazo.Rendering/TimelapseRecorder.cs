using System.Buffers.Binary;
using System.Security.Cryptography;
using Microsoft.Graphics.Canvas;
using Trazo.Core.IO;
using Windows.Storage.Streams;
using Windows.UI;

namespace Trazo.Rendering;

/// <summary>
/// TL-01: graba el time-lapse de un documento. Cada cambio del historial programa una captura; los cambios seguidos
/// se agrupan y se captura como mucho un fotograma por intervalo, con el estado final. La lectura de la GPU se hace en
/// el hilo que llama (el de la interfaz); la codificación JPEG y la escritura van en segundo plano, en orden.
/// </summary>
public sealed class TimelapseRecorder : IDisposable
{
    public static readonly TimeSpan DefaultInterval = TimeSpan.FromMilliseconds(200);

    private readonly PaintDocument _doc;
    private readonly string _path;
    private readonly Func<bool> _enabled;
    private readonly Action<TimeSpan, Action> _schedule;
    private readonly TimeSpan _interval;
    private readonly object _gate = new();
    private TimelapseWriter? _writer;
    private Task _queue = Task.CompletedTask;
    private ulong _lastHash;
    private bool _pending, _scheduled, _disposed;

    /// <param name="schedule">Ejecuta la acción tras el retardo en el hilo de la interfaz (en pruebas, se puede ignorar y llamar a <see cref="Flush"/>).</param>
    public TimelapseRecorder(PaintDocument doc, string projectPath, Func<bool> enabled, Action<TimeSpan, Action> schedule, TimeSpan? interval = null)
    {
        _doc = doc;
        _path = TimelapseFile.SidecarPath(projectPath);
        _enabled = enabled;
        _schedule = schedule;
        _interval = interval ?? DefaultInterval;
        doc.History.Changed += OnHistoryChanged;
    }

    public string Path => _path;

    /// <summary>Fotogramas guardados en esta sesión (sin contar los repetidos que se descartan).</summary>
    public int Captured { get; private set; }

    /// <summary>Último error al grabar (la grabación nunca interrumpe el trabajo; se informa en el registro).</summary>
    public Exception? LastError { get; private set; }

    private void OnHistoryChanged(object? sender, EventArgs e) => NotifyChanged();

    public void NotifyChanged()
    {
        if (_disposed || !_enabled()) return;
        _pending = true;
        if (_scheduled) return;
        _scheduled = true;
        _schedule(_interval, Flush);
    }

    /// <summary>Captura ahora si hay un cambio pendiente. Se llama desde el hilo de la interfaz.</summary>
    public void Flush()
    {
        _scheduled = false;
        if (!_pending || _disposed) return;
        _pending = false;
        if (!_enabled()) return;
        try
        {
            CaptureFrame();
        }
        catch (Exception e)
        {
            LastError = e;
        }
    }

    private void CaptureFrame()
    {
        var (w, h) = _writer is { } open ? (open.Width, open.Height) : TimelapseFile.FrameSize(_doc.Width, _doc.Height);
        byte[] bgra;
        using (var flat = _doc.Flatten(includeBackground: false))
        using (var frame = new CanvasRenderTarget(_doc.Device, w, h, 96))
        {
            using (var ds = frame.CreateDrawingSession())
            {
                // el fondo como se ve (blanco si está oculto); si el lienzo cambió de proporción, con bandas
                var bg = _doc.BackgroundVisible ? _doc.Background : Color.FromArgb(255, 255, 255, 255);
                ds.Clear(bg);
                var k = Math.Min((double)w / _doc.Width, (double)h / _doc.Height);
                var dw = _doc.Width * k;
                var dh = _doc.Height * k;
                ds.DrawImage(flat, new Windows.Foundation.Rect((w - dw) / 2, (h - dh) / 2, dw, dh), flat.Bounds, 1, CanvasImageInterpolation.HighQualityCubic);
            }
            bgra = frame.GetPixelBytes();
        }
        var hash = BinaryPrimitives.ReadUInt64LittleEndian(SHA256.HashData(bgra));
        if (hash == _lastHash) return; // igual que el anterior: no se guarda
        _lastHash = hash;
        Captured++;
        var device = _doc.Device;
        lock (_gate) _queue = _queue.ContinueWith(_ => WriteAsync(device, bgra, w, h), TaskScheduler.Default).Unwrap();
    }

    private async Task WriteAsync(CanvasDevice device, byte[] bgra, int w, int h)
    {
        try
        {
            var jpeg = await EncodeJpegAsync(device, bgra, w, h);
            lock (_gate)
            {
                if (_disposed) return;
                _writer ??= TimelapseWriter.Open(_path, w, h);
                _writer.Append(jpeg);
            }
        }
        catch (Exception e)
        {
            LastError = e;
        }
    }

    public static async Task<byte[]> EncodeJpegAsync(CanvasDevice device, byte[] bgra, int w, int h, float quality = 0.85f)
    {
        using var bmp = CanvasBitmap.CreateFromBytes(device, bgra, w, h, Windows.Graphics.DirectX.DirectXPixelFormat.B8G8R8A8UIntNormalized);
        using var ms = new InMemoryRandomAccessStream();
        await bmp.SaveAsync(ms, CanvasBitmapFileFormat.Jpeg, quality);
        var bytes = new byte[ms.Size];
        using var reader = new DataReader(ms.GetInputStreamAt(0));
        await reader.LoadAsync((uint)ms.Size);
        reader.ReadBytes(bytes);
        return bytes;
    }

    /// <summary>Espera a que lo capturado esté escrito en el disco (al cerrar la obra, para exportar y en las pruebas).</summary>
    public Task DrainAsync()
    {
        lock (_gate) return _queue;
    }

    /// <summary>Cierra el archivo para que otro lo lea (el reproductor) y lo vuelve a abrir en la siguiente captura.</summary>
    public async Task ReleaseAsync()
    {
        await DrainAsync();
        lock (_gate)
        {
            _writer?.Dispose();
            _writer = null;
        }
    }

    public void Dispose()
    {
        _doc.History.Changed -= OnHistoryChanged;
        Task pending;
        lock (_gate) pending = _queue;
        // lo que ya se capturó se termina de escribir
        try { pending.Wait(TimeSpan.FromSeconds(5)); } catch (AggregateException) { }
        lock (_gate)
        {
            _disposed = true;
            _writer?.Dispose();
            _writer = null;
        }
    }
}
