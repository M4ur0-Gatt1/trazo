using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.Graphics.Canvas;
using Trazo.Core.IO;
using Windows.Media.Core;
using Windows.Media.MediaProperties;
using Windows.Media.Transcoding;
using Windows.Storage.Streams;

namespace Trazo.Rendering;

/// <summary>TL-01: exporta un time-lapse a MP4 H.264 con Media Foundation de Windows, a 30 fotogramas por segundo.</summary>
public static class TimelapseVideo
{
    /// <summary>
    /// Escribe el vídeo en <paramref name="output"/> siguiendo <paramref name="schedule"/> (índices de fotograma, uno por
    /// fotograma del vídeo; ver <see cref="TimelapseSchedule"/>). Lanza InvalidOperationException si Windows no puede codificar.
    /// </summary>
    public static async Task ExportAsync(CanvasDevice device, TimelapseReader reader, IReadOnlyList<int> schedule, Stream output,
        IProgress<double>? progress = null, CancellationToken ct = default)
    {
        if (schedule.Count == 0) throw new InvalidOperationException("El time-lapse no tiene fotogramas.");
        int w = reader.Width, h = reader.Height;
        var frameTicks = TimeSpan.TicksPerSecond / TimelapseSchedule.Fps;

        var props = VideoEncodingProperties.CreateUncompressed(MediaEncodingSubtypes.Bgra8, (uint)w, (uint)h);
        props.FrameRate.Numerator = TimelapseSchedule.Fps;
        props.FrameRate.Denominator = 1;
        var source = new MediaStreamSource(new VideoStreamDescriptor(props))
        {
            BufferTime = TimeSpan.Zero,
            Duration = TimeSpan.FromTicks(schedule.Count * frameTicks),
        };

        // los fotogramas repetidos (los 2 s finales) se decodifican una sola vez
        var next = 0;
        var cachedIndex = -1;
        byte[]? cached = null;
        source.SampleRequested += async (_, e) =>
        {
            if (next >= schedule.Count || ct.IsCancellationRequested)
            {
                e.Request.Sample = null; // fin del vídeo
                return;
            }
            var deferral = e.Request.GetDeferral();
            try
            {
                var index = schedule[next];
                if (index != cachedIndex)
                {
                    cached = await DecodeAsync(device, reader.ReadFrame(index), w, h);
                    cachedIndex = index;
                }
                var sample = MediaStreamSample.CreateFromBuffer(cached!.AsBuffer(), TimeSpan.FromTicks(next * frameTicks));
                sample.Duration = TimeSpan.FromTicks(frameTicks);
                sample.KeyFrame = true;
                e.Request.Sample = sample;
                next++;
                progress?.Report((double)next / schedule.Count);
            }
            finally
            {
                deferral.Complete();
            }
        };

        var profile = MediaEncodingProfile.CreateMp4(VideoEncodingQuality.HD1080p);
        profile.Audio = null;
        profile.Video.Width = (uint)w;
        profile.Video.Height = (uint)h;
        profile.Video.FrameRate.Numerator = TimelapseSchedule.Fps;
        profile.Video.FrameRate.Denominator = 1;
        // unos 0,15 bits por píxel y fotograma: dibujos con mucho detalle sin archivos enormes
        profile.Video.Bitrate = (uint)Math.Clamp(w * h * TimelapseSchedule.Fps * 0.15, 1_000_000, 16_000_000);

        using var ras = output.AsRandomAccessStream();
        var transcoder = new MediaTranscoder { HardwareAccelerationEnabled = true };
        var prepared = await transcoder.PrepareMediaStreamSourceTranscodeAsync(source, ras, profile);
        if (!prepared.CanTranscode)
            throw new InvalidOperationException($"Windows no puede codificar el vídeo ({prepared.FailureReason}).");
        await prepared.TranscodeAsync().AsTask(ct);
        await ras.FlushAsync();
    }

    /// <summary>JPEG → BGRA del tamaño del vídeo (los fotogramas de otro tamaño se escalan).</summary>
    private static async Task<byte[]> DecodeAsync(CanvasDevice device, byte[] jpeg, int w, int h)
    {
        using var ms = new InMemoryRandomAccessStream();
        await ms.WriteAsync(jpeg.AsBuffer());
        ms.Seek(0);
        using var bmp = await CanvasBitmap.LoadAsync(device, ms, 96);
        if (bmp.SizeInPixels.Width == w && bmp.SizeInPixels.Height == h) return bmp.GetPixelBytes();
        using var rt = new CanvasRenderTarget(device, w, h, 96);
        using (var ds = rt.CreateDrawingSession()) ds.DrawImage(bmp, new Windows.Foundation.Rect(0, 0, w, h));
        return rt.GetPixelBytes();
    }
}
