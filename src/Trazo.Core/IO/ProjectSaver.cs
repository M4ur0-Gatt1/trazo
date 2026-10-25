using System.Buffers.Binary;

namespace Trazo.Core.IO;

/// <summary>Etapa del guardado en la que ocurrió un fallo.</summary>
public enum SaveStage
{
    Serialize,
    WriteTemporary,
    Flush,
    Verify,
    Replace,
}

/// <summary>El guardado no se completó. El archivo anterior sigue intacto.</summary>
public sealed class SaveFailedException(SaveStage stage, string message, Exception? inner = null) : IOException(message, inner)
{
    public SaveStage Stage { get; } = stage;
    public bool IsDiskFull => InnerException is IOException io && (io.HResult & 0xFFFF) is 0x70 or 0x27;
}

/// <summary>Operaciones de archivo usadas por el guardado; se sustituyen en las pruebas para simular fallos.</summary>
public interface IProjectFileSystem
{
    bool Exists(string path);
    Stream Create(string path);
    Stream OpenRead(string path);
    /// <summary>Fuerza la escritura física al disco.</summary>
    void Flush(Stream stream);
    /// <summary>Sustituye <paramref name="destination"/> por <paramref name="source"/> de forma atómica, guardando la versión anterior en <paramref name="backup"/>.</summary>
    void Replace(string source, string destination, string backup);
    void Move(string source, string destination);
    void Delete(string path);
}

public sealed class PhysicalFileSystem : IProjectFileSystem
{
    public static readonly PhysicalFileSystem Instance = new();

    public bool Exists(string path) => File.Exists(path);
    public Stream Create(string path) => new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16);
    public Stream OpenRead(string path) => new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16);
    public void Flush(Stream stream) => (stream as FileStream)?.Flush(flushToDisk: true);
    public void Replace(string source, string destination, string backup) => File.Replace(source, destination, backup, ignoreMetadataErrors: true);
    public void Move(string source, string destination) => File.Move(source, destination, overwrite: true);
    public void Delete(string path) => File.Delete(path);
}

public enum LoadSource
{
    Primary,
    Backup,
    Temporary,
}

public sealed record LoadResult(ProjectData Project, LoadSource Source, string? Problem)
{
    public bool Recovered => Source != LoadSource.Primary;
}

/// <summary>
/// Guardado transaccional de proyectos (FI-01):
/// serializar → escribir temporal → volcar a disco → verificar releyendo → reemplazo atómico con copia .bak.
/// Si cualquier etapa falla, se borra el temporal, el archivo anterior queda intacto y se lanza <see cref="SaveFailedException"/>.
/// </summary>
public static class ProjectSaver
{
    public static string TempPath(string path) => path + ".tmp";
    public static string BackupPath(string path) => path + ".bak";

    public static void Save(string path, ProjectData project, IProjectFileSystem? fs = null)
    {
        fs ??= PhysicalFileSystem.Instance;
        var tmp = TempPath(path);

        byte[] bytes;
        try
        {
            using var ms = new MemoryStream();
            TrazoFile.Write(ms, project);
            bytes = ms.ToArray();
        }
        catch (Exception e)
        {
            throw new SaveFailedException(SaveStage.Serialize, "No se pudo preparar el archivo para guardar.", e);
        }

        var stage = SaveStage.WriteTemporary;
        try
        {
            using (var s = fs.Create(tmp))
            {
                s.Write(bytes);
                stage = SaveStage.Flush;
                s.Flush();
                fs.Flush(s);
            }

            stage = SaveStage.Verify;
            ProjectData check;
            using (var r = fs.OpenRead(tmp)) check = TrazoFile.Read(r);
            Verify(project, check);

            stage = SaveStage.Replace;
            if (fs.Exists(path)) fs.Replace(tmp, path, BackupPath(path));
            else fs.Move(tmp, path);
        }
        catch (Exception e) when (e is not SaveFailedException)
        {
            TryDelete(fs, tmp);
            var message = stage switch
            {
                SaveStage.WriteTemporary or SaveStage.Flush when IsDiskFull(e) => "No hay espacio suficiente en el disco. El archivo anterior sigue intacto.",
                SaveStage.Verify => "La copia escrita no superó la verificación. El archivo anterior sigue intacto.",
                _ => "No se pudo guardar. El archivo anterior sigue intacto.",
            };
            throw new SaveFailedException(stage, message, e);
        }
    }

    public static Task SaveAsync(string path, ProjectData project, IProjectFileSystem? fs = null) =>
        Task.Run(() => Save(path, project, fs));

    /// <summary>
    /// Abre un proyecto. Si el archivo principal falta o está dañado, intenta la copia
    /// .bak y después el temporal de un guardado interrumpido, e informa del problema.
    /// </summary>
    public static LoadResult Load(string path, IProjectFileSystem? fs = null)
    {
        fs ??= PhysicalFileSystem.Instance;
        string? problem = null;
        foreach (var (candidate, source) in new[] { (path, LoadSource.Primary), (BackupPath(path), LoadSource.Backup), (TempPath(path), LoadSource.Temporary) })
        {
            if (!fs.Exists(candidate)) continue;
            try
            {
                using var s = fs.OpenRead(candidate);
                var project = TrazoFile.Read(s);
                ValidateLayers(project);
                return new LoadResult(project, source, problem);
            }
            catch (Exception e) when (e is InvalidDataException or IOException or FormatException)
            {
                problem ??= $"{Path.GetFileName(candidate)}: {e.Message}";
            }
        }
        throw new InvalidDataException(problem ?? $"No existe {path}");
    }

    /// <summary>Comprueba que lo releído coincide con lo que se quería guardar.</summary>
    internal static void Verify(ProjectData expected, ProjectData actual)
    {
        if (actual.Width != expected.Width || actual.Height != expected.Height)
            throw new InvalidDataException("Dimensiones distintas tras releer");
        if (!actual.Groups.SequenceEqual(expected.Groups))
            throw new InvalidDataException("Grupos distintos tras releer");
        if (actual.Layers.Count != expected.Layers.Count)
            throw new InvalidDataException("Número de capas distinto tras releer");
        for (var i = 0; i < actual.Layers.Count; i++)
        {
            if (actual.Layers[i].Properties != expected.Layers[i].Properties)
                throw new InvalidDataException($"Propiedades distintas en la capa {i + 1}");
            if (!actual.Layers[i].Png.AsSpan().SequenceEqual(expected.Layers[i].Png))
                throw new InvalidDataException($"Píxeles distintos en la capa {i + 1}");
            if (!(actual.Layers[i].MaskPng ?? []).AsSpan().SequenceEqual(expected.Layers[i].MaskPng ?? []))
                throw new InvalidDataException($"Máscara distinta en la capa {i + 1}");
        }
        ValidateLayers(actual);
    }

    /// <summary>Cada capa debe ser un PNG válido del tamaño del lienzo.</summary>
    public static void ValidateLayers(ProjectData p)
    {
        for (var i = 0; i < p.Layers.Count; i++)
        {
            var (w, h) = PngSize(p.Layers[i].Png) ?? throw new InvalidDataException($"La capa {i + 1} no es un PNG válido");
            if (w != p.Width || h != p.Height)
                throw new InvalidDataException($"La capa {i + 1} mide {w}×{h} y el lienzo {p.Width}×{p.Height}");
            if (p.Layers[i].MaskPng is { } mask)
            {
                var (mw, mh) = PngSize(mask) ?? throw new InvalidDataException($"La máscara de la capa {i + 1} no es un PNG válido");
                if (mw != p.Width || mh != p.Height) throw new InvalidDataException($"La máscara de la capa {i + 1} no mide lo que el lienzo");
            }
        }
    }

    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    /// <summary>Lee ancho y alto de la cabecera IHDR de un PNG.</summary>
    public static (int Width, int Height)? PngSize(ReadOnlySpan<byte> png)
    {
        if (png.Length < 24 || !png[..8].SequenceEqual(PngSignature) || !png.Slice(12, 4).SequenceEqual("IHDR"u8)) return null;
        var w = BinaryPrimitives.ReadInt32BigEndian(png.Slice(16, 4));
        var h = BinaryPrimitives.ReadInt32BigEndian(png.Slice(20, 4));
        return w > 0 && h > 0 ? (w, h) : null;
    }

    private static bool IsDiskFull(Exception e) => e is IOException io && (io.HResult & 0xFFFF) is 0x70 or 0x27;

    private static void TryDelete(IProjectFileSystem fs, string path)
    {
        try
        {
            if (fs.Exists(path)) fs.Delete(path);
        }
        catch (IOException)
        {
            // si no se puede borrar, se recuperará o sobrescribirá en el próximo guardado
        }
    }
}
