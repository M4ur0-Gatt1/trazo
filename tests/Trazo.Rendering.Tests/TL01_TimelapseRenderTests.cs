using Microsoft.Graphics.Canvas;
using Trazo.Core.IO;
using Trazo.Rendering;
using Windows.Media.Editing;
using Windows.Storage;
using Windows.UI;

namespace Trazo.Rendering.Tests;

/// <summary>TL-01 — Time-lapse con el motor real: captura (WARP) y vídeo MP4 con Media Foundation.</summary>
public class TL01_TimelapseRenderTests
{
    private static Color Rgb(byte r, byte g, byte b) => Color.FromArgb(255, r, g, b);

    // ---------- TL-01.5 ----------

    [Fact]
    public async Task RecorderCoalescesAndSkipsDuplicates()
    {
        using var tmp = new TempFolder();
        var project = tmp.File("obra.trazo");
        using var doc = PaintDocument.CreateBlank(Gpu.Device, 400, 300, "tl");
        var ops = new LayerOperations(doc);
        var scheduled = 0;
        var enabled = true;
        // el temporizador real lo pone la app; aquí solo se cuenta y se llama a Flush a mano
        using var rec = new TimelapseRecorder(doc, project, () => enabled, (_, _) => scheduled++);

        // tres cambios seguidos: una sola captura programada y un fotograma con el estado final
        ops.Fill(0, Rgb(200, 0, 0));
        ops.Fill(0, Rgb(0, 200, 0));
        ops.Fill(0, Rgb(0, 0, 200));
        Assert.Equal(1, scheduled);
        rec.Flush();
        // un cambio que deja la imagen igual (rellenar con el mismo color) no añade fotograma
        ops.Fill(0, Rgb(0, 0, 200));
        Assert.Equal(2, scheduled);
        rec.Flush();
        // deshacer también se graba
        doc.History.Undo();
        doc.History.Undo();
        rec.Flush();
        // desactivado no programa ni graba
        enabled = false;
        ops.Fill(0, Rgb(9, 9, 9));
        Assert.Equal(3, scheduled);
        rec.Flush();
        await rec.ReleaseAsync();
        Assert.Null(rec.LastError);
        Assert.Equal(2, rec.Captured);

        using var reader = TimelapseReader.Open(TimelapseFile.SidecarPath(project));
        Assert.Equal(TimelapseFile.FrameSize(400, 300), (reader.Width, reader.Height));
        Assert.Equal(2, reader.Frames.Count);
        Color Center(int i)
        {
            using var ms = new MemoryStream(reader.ReadFrame(i));
            using var bmp = CanvasBitmap.LoadAsync(Gpu.Device, ms.AsRandomAccessStream()).AsTask().Result;
            var p = bmp.GetPixelBytes((int)bmp.SizeInPixels.Width / 2, (int)bmp.SizeInPixels.Height / 2, 1, 1);
            return Color.FromArgb(p[3], p[2], p[1], p[0]);
        }
        // primero el azul final del grupo; después, deshecho dos veces, el verde
        var first = Center(0);
        Assert.True(first.B > 180 && first.R < 30 && first.G < 30, $"primer fotograma {first}");
        var second = Center(1);
        Assert.True(second.G > 180 && second.R < 30 && second.B < 30, $"segundo fotograma {second}");
    }

    [Fact]
    public async Task HiddenBackgroundRecordsOverWhiteAndResumesWithBands()
    {
        using var tmp = new TempFolder();
        var project = tmp.File("obra.trazo");
        using (var doc = PaintDocument.CreateBlank(Gpu.Device, 200, 200, "tl"))
        {
            doc.BackgroundVisible = false;
            using var rec = new TimelapseRecorder(doc, project, () => true, (_, _) => { });
            rec.NotifyChanged();
            rec.Flush();
            await rec.ReleaseAsync();
        }
        // la obra cambia de proporción en otra sesión: el fotograma conserva su tamaño y lleva bandas
        using (var doc = PaintDocument.CreateBlank(Gpu.Device, 400, 200, "tl"))
        {
            doc.Background = Rgb(255, 0, 0);
            using var rec = new TimelapseRecorder(doc, project, () => true, (_, _) => { });
            rec.NotifyChanged();
            rec.Flush();
            await rec.ReleaseAsync();
        }
        using var reader = TimelapseReader.Open(TimelapseFile.SidecarPath(project));
        Assert.Equal((200, 200), (reader.Width, reader.Height));
        Assert.Equal(2, reader.Frames.Count);
        using var ms = new MemoryStream(reader.ReadFrame(1));
        using var bmp = await CanvasBitmap.LoadAsync(Gpu.Device, ms.AsRandomAccessStream());
        var top = bmp.GetPixelBytes(100, 5, 1, 1);     // banda (fondo de la obra, rojo)
        var mid = bmp.GetPixelBytes(100, 100, 1, 1);   // la obra
        Assert.True(mid[2] > 230 && mid[1] < 30, "el centro es la obra roja");
        Assert.True(top[2] > 230 && top[1] < 30, "las bandas usan el color de fondo");
        using var ms0 = new MemoryStream(reader.ReadFrame(0));
        using var bmp0 = await CanvasBitmap.LoadAsync(Gpu.Device, ms0.AsRandomAccessStream());
        Assert.All(bmp0.GetPixelBytes(100, 100, 1, 1), b => Assert.True(b > 240)); // fondo oculto → blanco
    }

    // ---------- TL-01.6 ----------

    [Fact]
    public async Task ExportsPlayableMp4()
    {
        using var tmp = new TempFolder();
        var path = tmp.File("a.timelapse");
        const int w = 320, h = 180, n = 45;
        // 45 fotogramas que van de rojo a azul
        using (var writer = TimelapseWriter.Open(path, w, h))
        using (var rt = new CanvasRenderTarget(Gpu.Device, w, h, 96))
            for (var i = 0; i < n; i++)
            {
                var t = i / (float)(n - 1);
                using (var ds = rt.CreateDrawingSession()) ds.Clear(Rgb((byte)(255 * (1 - t)), 0, (byte)(255 * t)));
                writer.Append(await TimelapseRecorder.EncodeJpegAsync(Gpu.Device, rt.GetPixelBytes(), w, h));
            }

        var mp4 = tmp.File("tl.mp4");
        using (var reader = TimelapseReader.Open(path))
        await using (var fs = File.Create(mp4))
        {
            var progress = new List<double>();
            await TimelapseVideo.ExportAsync(Gpu.Device, reader, TimelapseSchedule.Full(n), fs, new Progress<double>(progress.Add));
        }
        Assert.True(new FileInfo(mp4).Length > 1000);

        // Windows lo abre: tamaño y duración (45 fotogramas + 2 s de final a 30 por segundo = 3,5 s)
        var file = await StorageFile.GetFileFromPathAsync(mp4);
        var props = await file.Properties.GetVideoPropertiesAsync();
        Assert.Equal((uint)w, props.Width);
        Assert.Equal((uint)h, props.Height);
        var clip = await MediaClip.CreateFromFileAsync(file);
        Assert.InRange(clip.OriginalDuration.TotalSeconds, 3.4, 3.6);

        // el principio es rojo y el final, azul
        var composition = new MediaComposition();
        composition.Clips.Add(clip);
        async Task<byte[]> FrameAt(double s)
        {
            using var thumb = await composition.GetThumbnailAsync(TimeSpan.FromSeconds(s), w, h, VideoFramePrecision.NearestFrame);
            using var bmp = await CanvasBitmap.LoadAsync(Gpu.Device, thumb);
            return bmp.GetPixelBytes(w / 2, h / 2, 1, 1);
        }
        var start = await FrameAt(0);
        var end = await FrameAt(3.3);
        Assert.True(start[2] > 200 && start[0] < 60, $"primer fotograma BGRA {string.Join(",", start)}");
        Assert.True(end[0] > 200 && end[2] < 60, $"último fotograma BGRA {string.Join(",", end)}");
    }
}
