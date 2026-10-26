using System.Diagnostics;
using System.Text.Json;
using static Trazo.Core.Localization.Tr;

namespace Trazo.Core.IO;

/// <summary>Una sesión de trabajo abierta (o abandonada) según su diario.</summary>
public sealed record SessionEntry
{
    public int ProcessId { get; init; }
    /// <summary>Hora de inicio del proceso (ticks UTC): distingue un pid reutilizado.</summary>
    public long ProcessStart { get; init; }
    public string? Document { get; init; }
    public DateTimeOffset Opened { get; init; }
    public DateTimeOffset? LastSaved { get; init; }
}

/// <summary>
/// Diario de sesión (RC-01): mientras un documento está abierto existe <c>session-&lt;pid&gt;.json</c>; al cerrar
/// bien se borra. Si al arrancar queda el diario de un proceso que ya no existe, la sesión terminó de forma inesperada.
/// </summary>
public sealed class SessionJournal
{
    private readonly string _folder;
    private readonly int _pid;
    private readonly long _start;
    private SessionEntry? _entry;

    /// <param name="processId">Para pruebas; por defecto, el proceso actual.</param>
    public SessionJournal(string folder, int? processId = null, long? processStart = null)
    {
        _folder = folder;
        using var me = Process.GetCurrentProcess();
        _pid = processId ?? me.Id;
        _start = processStart ?? me.StartTime.ToUniversalTime().Ticks;
    }

    public string FilePath => Path.Combine(_folder, $"session-{_pid}.json");

    /// <summary>Hay un documento abierto: se crea o actualiza el diario.</summary>
    public void Begin(string? document, DateTimeOffset now)
    {
        _entry = new SessionEntry { ProcessId = _pid, ProcessStart = _start, Document = document, Opened = now };
        Write();
    }

    /// <summary>El documento se guardó bien: se anota la hora.</summary>
    public void MarkSaved(DateTimeOffset when, string? document = null)
    {
        if (_entry is null) return;
        _entry = _entry with { LastSaved = when, Document = document ?? _entry.Document };
        Write();
    }

    /// <summary>Cierre normal: se borra el diario.</summary>
    public void End()
    {
        _entry = null;
        try
        {
            File.Delete(FilePath);
        }
        catch (IOException)
        {
            // si no se puede borrar ahora, el próximo arranque lo verá como de un proceso vivo o lo descartará
        }
    }

    private void Write()
    {
        Directory.CreateDirectory(_folder);
        var tmp = FilePath + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(_entry!, TrazoJson.Default.SessionEntry));
        File.Move(tmp, FilePath, overwrite: true);
    }

    /// <summary>
    /// Devuelve (y borra) los diarios de procesos que ya no existen. Los de procesos vivos no se tocan; los dañados
    /// se borran y se cuentan en <paramref name="discarded"/>.
    /// </summary>
    public static List<SessionEntry> CollectAbandoned(string folder, Func<int, long, bool>? isAlive, out int discarded)
    {
        isAlive ??= IsProcessAlive;
        discarded = 0;
        var abandoned = new List<SessionEntry>();
        if (!Directory.Exists(folder)) return abandoned;
        foreach (var file in Directory.EnumerateFiles(folder, "session-*.json"))
        {
            SessionEntry? entry = null;
            try
            {
                entry = JsonSerializer.Deserialize(File.ReadAllText(file), TrazoJson.Default.SessionEntry);
            }
            catch (Exception e) when (e is JsonException or IOException or NotSupportedException)
            {
                entry = null;
            }
            if (entry is null || entry.ProcessId <= 0)
            {
                TryDelete(file);
                discarded++;
                continue;
            }
            if (isAlive(entry.ProcessId, entry.ProcessStart)) continue;
            abandoned.Add(entry);
            TryDelete(file);
        }
        return abandoned;
    }

    private static void TryDelete(string file)
    {
        try
        {
            File.Delete(file);
        }
        catch (IOException)
        {
        }
    }

    /// <summary>Vivo = existe un proceso con ese pid y la misma hora de inicio.</summary>
    public static bool IsProcessAlive(int pid, long start)
    {
        try
        {
            using var p = Process.GetProcessById(pid);
            return p.StartTime.ToUniversalTime().Ticks == start;
        }
        catch (ArgumentException)
        {
            return false; // no existe
        }
        catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return true; // existe pero no se puede consultar: se deja en paz
        }
    }
}

/// <summary>
/// Copia de emergencia tras una excepción inesperada (RC-01): «Nombre (recuperado).trazo» junto al documento,
/// sin tocar el archivo principal, como mucho una por minuto.
/// </summary>
public sealed class EmergencySaver
{
    public static readonly TimeSpan MinInterval = TimeSpan.FromMinutes(1);
    private DateTimeOffset _last = DateTimeOffset.MinValue;

    public static string PathFor(string documentPath)
    {
        var dir = Path.GetDirectoryName(documentPath) ?? ".";
        var name = Path.GetFileNameWithoutExtension(documentPath);
        return Path.Combine(dir, F("{0} (recuperado){1}", name, TrazoFile.Extension));
    }

    /// <summary>Guarda la copia y devuelve su ruta, o null si se guardó otra hace menos de un minuto.</summary>
    public string? TrySave(string documentPath, ProjectData data, DateTimeOffset now)
    {
        if (now - _last < MinInterval) return null;
        var path = PathFor(documentPath);
        ProjectSaver.Save(path, data with { Name = data.Name + T(" (recuperado)") });
        _last = now;
        return path;
    }
}
