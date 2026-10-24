using System.Buffers.Binary;
using Trazo.Core.Documents;
using Trazo.Core.IO;

namespace Trazo.Core.Tests;

/// <summary>
/// FI-01 — Guardado y recuperación ante fallos (docs/protocols/FI-01.md).
/// Un sistema de archivos en memoria inyecta fallos en cada etapa del guardado.
/// </summary>
public class FI01_PersistenceTests
{
    private const string Path = @"C:\obras\dibujo.trazo";

    /// <summary>PNG mínimo con cabecera IHDR válida (suficiente para la validación de tamaño).</summary>
    internal static byte[] Png(int w, int h, byte marker = 0)
    {
        var b = new byte[33];
        new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }.CopyTo(b, 0);
        BinaryPrimitives.WriteInt32BigEndian(b.AsSpan(8), 13);
        "IHDR"u8.CopyTo(b.AsSpan(12));
        BinaryPrimitives.WriteInt32BigEndian(b.AsSpan(16), w);
        BinaryPrimitives.WriteInt32BigEndian(b.AsSpan(20), h);
        b[32] = marker;
        return b;
    }

    private static ProjectData Project(byte marker, int layers = 2) => new()
    {
        Name = "Dibujo", Width = 64, Height = 48, Created = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
        Layers = Enumerable.Range(0, layers).Select(i => new LayerData(new LayerProperties { Id = $"l{i}", Name = $"Capa {i + 1}" }, Png(64, 48, marker))).ToList(),
    };

    private static byte Marker(ProjectData p) => p.Layers[0].Png[32];

    public enum Fault { None, MidWrite, DiskFull, Flush, CorruptWrite, Replace }

    private sealed class MemoryFileSystem : IProjectFileSystem
    {
        public readonly Dictionary<string, byte[]> Files = new(StringComparer.OrdinalIgnoreCase);
        public Fault Fault;

        public bool Exists(string path) => Files.ContainsKey(path);
        public Stream OpenRead(string path) => new MemoryStream(Files[path], writable: false);
        public void Delete(string path) => Files.Remove(path);
        public void Move(string source, string destination)
        {
            if (Fault == Fault.Replace) throw new IOException("acceso denegado (simulado)");
            Files[destination] = Files[source];
            Files.Remove(source);
        }

        public void Replace(string source, string destination, string backup)
        {
            if (Fault == Fault.Replace) throw new IOException("el archivo está en uso (simulado)");
            Files[backup] = Files[destination];
            Files[destination] = Files[source];
            Files.Remove(source);
        }

        public Stream Create(string path) => new Capture(this, path);

        public void Flush(Stream stream)
        {
            if (Fault == Fault.Flush) throw new IOException("error de E/S al volcar (simulado)");
        }

        private sealed class Capture(MemoryFileSystem fs, string path) : MemoryStream
        {
            // La sobrecarga con arreglo es la principal: MemoryStream derivado redirige la de Span hacia ella
            public override void Write(ReadOnlySpan<byte> buffer) => Write(buffer.ToArray(), 0, buffer.Length);

            public override void Write(byte[] buffer, int offset, int count)
            {
                switch (fs.Fault)
                {
                    case Fault.MidWrite:
                        base.Write(buffer, offset, count / 2);
                        Commit();
                        throw new IOException("el dispositivo dejó de responder (simulado)");
                    case Fault.DiskFull:
                        Commit();
                        throw new IOException("no hay espacio (simulado)", unchecked((int)0x80070070));
                    case Fault.CorruptWrite:
                        var copy = buffer.AsSpan(offset, count).ToArray();
                        // corrupción silenciosa de un byte dentro de los datos de una capa (cabecera IHDR):
                        // un byte al azar podría caer en metadatos del ZIP que no se usan y la prueba sería intermitente
                        var ihdr = copy.AsSpan().IndexOf("IHDR"u8);
                        copy[ihdr + 8] ^= 0xFF;
                        base.Write(copy, 0, copy.Length);
                        return;
                    default:
                        base.Write(buffer, offset, count);
                        return;
                }
            }
            private void Commit() => fs.Files[path] = ToArray();

            protected override void Dispose(bool disposing)
            {
                if (disposing) Commit();
                base.Dispose(disposing);
            }
        }
    }

    private static MemoryFileSystem WithSavedVersion(byte marker)
    {
        var fs = new MemoryFileSystem();
        ProjectSaver.Save(Path, Project(marker), fs);
        return fs;
    }

    [Fact]
    public void SaveCreatesFileAndBackupOfPreviousVersion()
    {
        var fs = WithSavedVersion(1);
        Assert.False(fs.Exists(ProjectSaver.BackupPath(Path)));
        ProjectSaver.Save(Path, Project(2), fs);
        Assert.Equal(2, Marker(ProjectSaver.Load(Path, fs).Project));
        Assert.Equal(1, Marker(TrazoFile.Read(fs.OpenRead(ProjectSaver.BackupPath(Path)))));
        Assert.False(fs.Exists(ProjectSaver.TempPath(Path)));
    }

    // El archivo anterior queda recuperable; un guardado incompleto no se presenta como exitoso;
    // los temporales se limpian.
    [Theory]
    [InlineData(Fault.MidWrite, SaveStage.WriteTemporary)]
    [InlineData(Fault.DiskFull, SaveStage.WriteTemporary)]
    [InlineData(Fault.Flush, SaveStage.Flush)]
    [InlineData(Fault.CorruptWrite, SaveStage.Verify)]
    [InlineData(Fault.Replace, SaveStage.Replace)]
    public void FailedSaveKeepsPreviousVersionIntact(Fault fault, SaveStage expectedStage)
    {
        var fs = WithSavedVersion(1);
        var before = fs.Files[Path].ToArray();
        fs.Fault = fault;

        var e = Assert.Throws<SaveFailedException>(() => ProjectSaver.Save(Path, Project(2), fs));

        Assert.Equal(expectedStage, e.Stage);
        Assert.Contains("anterior sigue intacto", e.Message);
        Assert.Equal(before, fs.Files[Path]);
        Assert.False(fs.Exists(ProjectSaver.TempPath(Path)));
        fs.Fault = Fault.None;
        Assert.Equal(1, Marker(ProjectSaver.Load(Path, fs).Project));
    }

    [Fact]
    public void DiskFullIsReportedClearly()
    {
        var fs = WithSavedVersion(1);
        fs.Fault = Fault.DiskFull;
        var e = Assert.Throws<SaveFailedException>(() => ProjectSaver.Save(Path, Project(2), fs));
        Assert.True(e.IsDiskFull);
        Assert.Contains("espacio", e.Message);
    }

    [Fact]
    public void FirstSaveFailureLeavesNoFileBehind()
    {
        var fs = new MemoryFileSystem { Fault = Fault.MidWrite };
        Assert.Throws<SaveFailedException>(() => ProjectSaver.Save(Path, Project(1), fs));
        Assert.Empty(fs.Files);
    }

    // Recuperación: el archivo principal dañado se recupera desde la copia anterior
    [Fact]
    public void CorruptPrimaryRecoversFromBackup()
    {
        var fs = WithSavedVersion(1);
        ProjectSaver.Save(Path, Project(2), fs);
        fs.Files[Path] = fs.Files[Path][..(fs.Files[Path].Length / 3)]; // truncado (p. ej. corte de luz)
        var r = ProjectSaver.Load(Path, fs);
        Assert.True(r.Recovered);
        Assert.Equal(LoadSource.Backup, r.Source);
        Assert.Equal(1, Marker(r.Project));
        Assert.NotNull(r.Problem);
    }

    [Fact]
    public void InterruptedFirstSaveIsRecoveredFromTemporary()
    {
        var fs = new MemoryFileSystem();
        using (var ms = new MemoryStream())
        {
            TrazoFile.Write(ms, Project(5));
            fs.Files[ProjectSaver.TempPath(Path)] = ms.ToArray(); // cierre inesperado justo antes de renombrar
        }
        var r = ProjectSaver.Load(Path, fs);
        Assert.Equal(LoadSource.Temporary, r.Source);
        Assert.Equal(5, Marker(r.Project));
    }

    [Fact]
    public void LoadFailsClearlyWhenNothingIsValid()
    {
        var fs = new MemoryFileSystem();
        fs.Files[Path] = "basura"u8.ToArray();
        var e = Assert.Throws<InvalidDataException>(() => ProjectSaver.Load(Path, fs));
        Assert.Contains("dibujo.trazo", e.Message);
    }

    // Integridad: capas de tamaño incorrecto o PNG inválidos se detectan
    [Fact]
    public void RejectsLayerWithWrongSize()
    {
        var p = Project(1) with { Layers = [new LayerData(new LayerProperties(), Png(10, 10))] };
        Assert.Throws<InvalidDataException>(() => ProjectSaver.ValidateLayers(p));
        Assert.Throws<SaveFailedException>(() => ProjectSaver.Save(Path, p, new MemoryFileSystem()));
    }

    [Fact]
    public void RejectsInvalidPng()
    {
        var p = Project(1) with { Layers = [new LayerData(new LayerProperties(), [1, 2, 3])] };
        Assert.Throws<InvalidDataException>(() => ProjectSaver.ValidateLayers(p));
    }

    [Fact]
    public void PhysicalSaveRoundTripsOnDisk()
    {
        var dir = Directory.CreateTempSubdirectory("trazo-fi01-");
        try
        {
            var file = System.IO.Path.Combine(dir.FullName, "obra.trazo");
            ProjectSaver.Save(file, Project(1));
            ProjectSaver.Save(file, Project(2));
            Assert.Equal(2, Marker(ProjectSaver.Load(file).Project));
            Assert.True(File.Exists(ProjectSaver.BackupPath(file)));
            Assert.False(File.Exists(ProjectSaver.TempPath(file)));
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }
}
