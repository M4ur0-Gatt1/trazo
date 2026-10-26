using System.Buffers.Binary;
using System.Text;

namespace Trazo.Core.IO;

/// <summary>
/// TL-01: archivo de time-lapse junto a la obra (<c>obra.trazo.timelapse</c>). Cabecera <c>TRZTL1</c> con el tamaño
/// del fotograma y, después, fotogramas JPEG precedidos de su longitud y del tiempo transcurrido. Solo se añade al
/// final; un fotograma a medias (cierre inesperado) se descarta al leer.
/// </summary>
public static class TimelapseFile
{
    public const string Extension = ".timelapse";
    public const int MaxSide = 1080;
    internal const int HeaderSize = 6 + 2 + 4 + 4;
    private const int MaxFrameBytes = 64 * 1024 * 1024;
    private static readonly byte[] Magic = Encoding.ASCII.GetBytes("TRZTL1");

    public static string SidecarPath(string projectPath) => projectPath + Extension;

    /// <summary>Tamaño del fotograma: lado mayor como mucho <see cref="MaxSide"/>, lados pares y misma proporción.</summary>
    public static (int Width, int Height) FrameSize(int canvasW, int canvasH)
    {
        var k = Math.Min(1.0, (double)MaxSide / Math.Max(canvasW, canvasH));
        static int Even(double v) => Math.Max(2, (int)Math.Round(v / 2) * 2);
        return (Even(canvasW * k), Even(canvasH * k));
    }

    internal static byte[] Header(int w, int h)
    {
        var b = new byte[HeaderSize];
        Magic.CopyTo(b, 0);
        BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(6), 1);
        BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(8), w);
        BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(12), h);
        return b;
    }

    /// <summary>Lee la cabecera; lanza InvalidDataException si no es un time-lapse válido.</summary>
    internal static (int W, int H) ReadHeader(Stream s)
    {
        var b = new byte[HeaderSize];
        s.Position = 0;
        if (s.ReadAtLeast(b, HeaderSize, throwOnEndOfStream: false) < HeaderSize || !b.AsSpan(0, 6).SequenceEqual(Magic))
            throw new InvalidDataException("No es un archivo de time-lapse de Trazo."); // i18n: interno
        if (BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(6)) != 1) throw new InvalidDataException("Versión de time-lapse desconocida."); // i18n: interno
        var w = BinaryPrimitives.ReadInt32LittleEndian(b.AsSpan(8));
        var h = BinaryPrimitives.ReadInt32LittleEndian(b.AsSpan(12));
        if (w < 2 || h < 2 || w > 8192 || h > 8192 || w % 2 != 0 || h % 2 != 0) throw new InvalidDataException("Tamaño de fotograma no válido en el time-lapse."); // i18n: interno
        return (w, h);
    }

    /// <summary>Recorre los fotogramas completos; devuelve su índice y dónde termina el último completo.</summary>
    internal static (List<TimelapseFrame> Frames, long End) Scan(Stream s)
    {
        var frames = new List<TimelapseFrame>();
        var head = new byte[8];
        long pos = HeaderSize;
        var lastElapsed = 0;
        while (true)
        {
            s.Position = pos;
            if (s.ReadAtLeast(head, 8, throwOnEndOfStream: false) < 8) break;
            var len = BinaryPrimitives.ReadInt32LittleEndian(head);
            var elapsed = BinaryPrimitives.ReadInt32LittleEndian(head.AsSpan(4));
            // longitud imposible o tiempo hacia atrás: el resto no es de fiar
            if (len <= 0 || len > MaxFrameBytes || elapsed < lastElapsed || pos + 8 + len > s.Length) break;
            frames.Add(new TimelapseFrame(pos + 8, len, elapsed));
            lastElapsed = elapsed;
            pos += 8 + len;
        }
        return (frames, pos);
    }
}

/// <summary>Un fotograma dentro del archivo: dónde están sus bytes JPEG y cuándo se grabó (ms desde el principio).</summary>
public readonly record struct TimelapseFrame(long Offset, int Length, int ElapsedMs);

/// <summary>Añade fotogramas al final del archivo, creándolo si no existe y descartando un final incompleto.</summary>
public sealed class TimelapseWriter : IDisposable
{
    private readonly FileStream _s;
    private readonly int _baseElapsed;
    private readonly DateTime _opened;

    private TimelapseWriter(FileStream s, int w, int h, int count, int baseElapsed, Func<DateTime> clock)
    {
        _s = s;
        Width = w;
        Height = h;
        Count = count;
        _baseElapsed = baseElapsed;
        _clock = clock;
        _opened = clock();
    }

    private readonly Func<DateTime> _clock;

    public int Width { get; }
    public int Height { get; }
    public int Count { get; private set; }
    public long Bytes => _s.Length;

    /// <summary>
    /// Abre (o crea con el tamaño dado) el time-lapse. Si ya existe, conserva su tamaño de fotograma y sigue contando el
    /// tiempo donde lo dejó. Un archivo dañado se aparta como <c>.dañado</c> y se empieza otro. // i18n: interno
    /// </summary>
    public static TimelapseWriter Open(string path, int width, int height, Func<DateTime>? clock = null)
    {
        clock ??= () => DateTime.UtcNow;
        var s = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.Read);
        try
        {
            if (s.Length > 0)
            {
                try
                {
                    var (w, h) = TimelapseFile.ReadHeader(s);
                    var (frames, end) = TimelapseFile.Scan(s);
                    s.SetLength(end); // un fotograma a medias se descarta
                    s.Position = end;
                    return new TimelapseWriter(s, w, h, frames.Count, frames.Count > 0 ? frames[^1].ElapsedMs : 0, clock);
                }
                catch (InvalidDataException)
                {
                    s.Dispose();
                    var aside = path + ".dañado"; // i18n: interno
                    File.Move(path, aside, overwrite: true);
                    s = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.Read);
                }
            }
            s.Write(TimelapseFile.Header(width, height));
            s.Flush();
            return new TimelapseWriter(s, width, height, 0, 0, clock);
        }
        catch
        {
            s.Dispose();
            throw;
        }
    }

    /// <summary>Añade un fotograma JPEG y lo deja escrito en el disco.</summary>
    public void Append(ReadOnlySpan<byte> jpeg)
    {
        var elapsed = _baseElapsed + (int)Math.Min(int.MaxValue - _baseElapsed, Math.Max(0, (_clock() - _opened).TotalMilliseconds));
        var head = new byte[8];
        BinaryPrimitives.WriteInt32LittleEndian(head, jpeg.Length);
        BinaryPrimitives.WriteInt32LittleEndian(head.AsSpan(4), elapsed);
        _s.Position = _s.Length;
        _s.Write(head);
        _s.Write(jpeg);
        _s.Flush();
        Count++;
    }

    public void Dispose() => _s.Dispose();
}

/// <summary>Lee un time-lapse: índice de fotogramas y bytes de cada uno bajo demanda.</summary>
public sealed class TimelapseReader : IDisposable
{
    private readonly FileStream _s;

    private TimelapseReader(FileStream s, int w, int h, List<TimelapseFrame> frames)
    {
        _s = s;
        Width = w;
        Height = h;
        Frames = frames;
    }

    public int Width { get; }
    public int Height { get; }
    public IReadOnlyList<TimelapseFrame> Frames { get; }
    public long Bytes => _s.Length;
    public TimeSpan Recorded => Frames.Count == 0 ? TimeSpan.Zero : TimeSpan.FromMilliseconds(Frames[^1].ElapsedMs);

    public static TimelapseReader Open(string path)
    {
        var s = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        try
        {
            var (w, h) = TimelapseFile.ReadHeader(s);
            var (frames, _) = TimelapseFile.Scan(s);
            return new TimelapseReader(s, w, h, frames);
        }
        catch
        {
            s.Dispose();
            throw;
        }
    }

    public byte[] ReadFrame(int index)
    {
        var f = Frames[index];
        var b = new byte[f.Length];
        _s.Position = f.Offset;
        _s.ReadExactly(b);
        return b;
    }

    public void Dispose() => _s.Dispose();
}

/// <summary>Qué fotograma va en cada fotograma del vídeo exportado (TL-01).</summary>
public static class TimelapseSchedule
{
    public const int Fps = 30;
    public const int HoldSeconds = 2;

    /// <summary>Vídeo completo: todos los fotogramas una vez y el último 2 s más.</summary>
    public static int[] Full(int frames) =>
        frames == 0 ? [] : [.. Enumerable.Range(0, frames), .. Enumerable.Repeat(frames - 1, HoldSeconds * Fps)];

    /// <summary>
    /// Vídeo de como mucho <paramref name="seconds"/> s (contando los 2 s finales): si hay más fotogramas de los que
    /// caben, se eligen repartidos por igual, empezando en el primero y terminando en el último. No se inventan
    /// fotogramas: con pocos, el vídeo es más corto.
    /// </summary>
    public static int[] Short(int frames, int seconds = 30)
    {
        if (frames == 0) return [];
        var room = Math.Max(1, (seconds - HoldSeconds) * Fps);
        if (frames <= room) return Full(frames);
        var picked = new int[room];
        for (var i = 0; i < room; i++) picked[i] = room == 1 ? frames - 1 : (int)Math.Round(i * (frames - 1) / (double)(room - 1));
        return [.. picked, .. Enumerable.Repeat(frames - 1, HoldSeconds * Fps)];
    }
}

/// <summary>Operaciones de la galería sobre una obra y su time-lapse a la vez (TL-01.7).</summary>
public static class ProjectFiles
{
    public static void Move(string from, string to)
    {
        File.Move(from, to);
        var tl = TimelapseFile.SidecarPath(from);
        if (File.Exists(tl)) File.Move(tl, TimelapseFile.SidecarPath(to), overwrite: true);
    }

    public static void Copy(string from, string to)
    {
        File.Copy(from, to);
        var tl = TimelapseFile.SidecarPath(from);
        if (File.Exists(tl)) File.Copy(tl, TimelapseFile.SidecarPath(to), overwrite: true);
    }
}
