using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Graphics.Canvas;
using Trazo.Core.IO;

namespace Trazo.Rendering.Tests;

/// <summary>Dispositivo WARP compartido: el mismo resultado en cualquier máquina.</summary>
public static class Gpu
{
    private static readonly Lazy<CanvasDevice> Shared = new(() => new CanvasDevice(forceSoftwareRenderer: true));
    public static CanvasDevice Device => Shared.Value;
}

public sealed record ImageDiff(int Width, int Height, int MaxChannelDiff, double MeanChannelDiff, double PercentPixelsOver8)
{
    public bool Within(double meanTolerance = 0.5, double percentTolerance = 0.5) =>
        MeanChannelDiff <= meanTolerance && PercentPixelsOver8 <= percentTolerance;

    public override string ToString() =>
        $"máx {MaxChannelDiff}, media {MeanChannelDiff:0.000}, píxeles > 8: {PercentPixelsOver8:0.000} %";
}

/// <summary>Comparación de imágenes de referencia ("golden") con métricas y tolerancias.</summary>
public static class Golden
{
    /// <summary>TRAZO_UPDATE_GOLDEN=1 regenera las referencias en lugar de comparar.</summary>
    public static bool Update => Environment.GetEnvironmentVariable("TRAZO_UPDATE_GOLDEN") == "1";

    public static string SourceDir([CallerFilePath] string file = "") => Path.Combine(Path.GetDirectoryName(file)!, "Golden");

    public static string ResultsDir
    {
        get
        {
            var d = Path.Combine(AppContext.BaseDirectory, "results");
            Directory.CreateDirectory(d);
            return d;
        }
    }

    public static ImageDiff Compare(byte[] a, byte[] b, int width, int height)
    {
        if (a.Length != b.Length) return new ImageDiff(width, height, 255, 255, 100);
        long sum = 0;
        var max = 0;
        var over = 0;
        for (var i = 0; i < a.Length; i += 4)
        {
            var pixelMax = 0;
            for (var c = 0; c < 4; c++)
            {
                var d = Math.Abs(a[i + c] - b[i + c]);
                sum += d;
                if (d > pixelMax) pixelMax = d;
            }
            if (pixelMax > max) max = pixelMax;
            if (pixelMax > 8) over++;
        }
        return new ImageDiff(width, height, max, sum / (double)a.Length, over * 100.0 / (a.Length / 4));
    }

    /// <summary>Compara el bitmap con la referencia guardada; la crea si se pide actualizar.</summary>
    public static async Task<ImageDiff> CheckAsync(CanvasBitmap actual, string category, string name)
    {
        var dir = Path.Combine(SourceDir(), category);
        var file = Path.Combine(dir, name + ".png");
        var png = await PaintDocument.EncodePngAsync(actual);
        await File.WriteAllBytesAsync(Path.Combine(ResultsDir, $"{category}-{name}.png"), png);
        if (Update || !File.Exists(file))
        {
            if (!Update) Assert.Fail($"Falta la referencia {file}. Ejecuta con TRAZO_UPDATE_GOLDEN=1 para crearla y revísala antes de confirmarla.");
            Directory.CreateDirectory(dir);
            await File.WriteAllBytesAsync(file, png);
        }
        using var expected = await PaintDocument.DecodeAsync(Gpu.Device, await File.ReadAllBytesAsync(file));
        var s = actual.SizeInPixels;
        Assert.Equal(s, expected.SizeInPixels);
        return Compare(actual.GetPixelBytes(), expected.GetPixelBytes(), (int)s.Width, (int)s.Height);
    }

    public static void AppendMetrics(string file, object row)
    {
        lock (typeof(Golden))
            File.AppendAllText(Path.Combine(ResultsDir, file), JsonSerializer.Serialize(row) + Environment.NewLine);
    }
}

/// <summary>Sistema de archivos real en una carpeta temporal que se borra al terminar.</summary>
public sealed class TempFolder : IDisposable
{
    public string Path { get; } = Directory.CreateTempSubdirectory("trazo-render-").FullName;
    public string File(string name) => System.IO.Path.Combine(Path, name);
    public void Dispose() => Directory.Delete(Path, recursive: true);
}
