using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using Trazo.Core.Documents;
using Trazo.Core.History;
using Trazo.Core.IO;

namespace Trazo.Core.Tests;

/// <summary>
/// Regresiones de la auditoría del 2026-11-02 (docs/results/2026-11-02.md), parte sin GPU: cada prueba falla con el
/// código anterior a la corrección.
/// </summary>
public sealed class AUD01_CoreRegressionTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("trazo-aud01-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static ProjectData Project(byte marker) => new()
    {
        Name = "Obra", Width = 64, Height = 48,
        Layers = [new(new LayerProperties { Id = "a", Name = "Capa 1" }, FI01_PersistenceTests.Png(64, 48, marker))],
        ThumbnailPng = FI01_PersistenceTests.Png(8, 6),
    };

    private static byte[] Bytes(ProjectData p)
    {
        using var ms = new MemoryStream();
        TrazoFile.Write(ms, p);
        return ms.ToArray();
    }

    /// <summary>El mismo archivo con el manifiesto cambiado por <paramref name="edit"/>.</summary>
    private static byte[] WithManifest(byte[] trazo, Func<string, string> edit)
    {
        using var ms = new MemoryStream();
        ms.Write(trazo);
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Update, leaveOpen: true))
        {
            var entry = zip.GetEntry("manifest.json")!;
            string json;
            using (var r = new StreamReader(entry.Open())) json = r.ReadToEnd();
            entry.Delete();
            using var w = new StreamWriter(zip.CreateEntry("manifest.json").Open());
            w.Write(edit(json));
        }
        return ms.ToArray();
    }

    // ---------- 1. Un manifiesto dañado no impide recuperar la copia .bak ----------

    [Fact]
    public void DamagedManifestFallsBackToBackup()
    {
        var good = Bytes(Project(7));
        var damaged = new Dictionary<string, Func<string, string>>
        {
            ["cortado"] = j => j[..(j.Length / 2)],
            ["ancho de texto"] = j => Regex.Replace(j, "\"width\":\\s*\\d+", "\"width\": \"x\""),
            ["capas nulas"] = j => Regex.Replace(j, "\"layers\":\\s*\\[", "\"layers\": null, \"x\": ["),
            ["capa nula"] = j => Regex.Replace(j, "\"layers\":\\s*\\[", "\"layers\": [null,"),
            ["archivo nulo"] = j => Regex.Replace(j, "\"file\":\\s*\"[^\"]+\"", "\"file\": null"),
        };
        foreach (var (what, edit) in damaged)
        {
            var bad = WithManifest(good, edit);
            // el lector solo lanza InvalidDataException
            var e = Assert.Throws<InvalidDataException>(() => TrazoFile.Read(new MemoryStream(bad)));
            Assert.False(string.IsNullOrWhiteSpace(e.Message), what);
            // y la carga recupera la copia anterior en lugar de fallar
            var path = Path.Combine(_dir, $"{what}.trazo");
            File.WriteAllBytes(path, bad);
            File.WriteAllBytes(ProjectSaver.BackupPath(path), good);
            var result = ProjectSaver.Load(path);
            Assert.Equal(LoadSource.Backup, result.Source);
            Assert.Equal(7, result.Project.Layers[0].Png[32]);
        }
        // formato web antiguo con base64 roto o capa nula
        foreach (var v1 in new[] { "{\"format\":\"trazo\",\"width\":64,\"height\":48,\"layers\":[{\"png\":\"data:image/png;base64,@@@\"}]}",
                                   "{\"format\":\"trazo\",\"width\":64,\"height\":48,\"layers\":[null]}" })
            Assert.Throws<InvalidDataException>(() => TrazoFile.Read(new MemoryStream(Encoding.UTF8.GetBytes(v1))));
    }

    // ---------- 4. Un archivo bloqueado por otro programa no abre la copia vieja en silencio ----------

    [Fact]
    public void LockedPrimaryIsNotReplacedByBackup()
    {
        var path = Path.Combine(_dir, "bloqueado.trazo");
        File.WriteAllBytes(path, Bytes(Project(9)));
        File.WriteAllBytes(ProjectSaver.BackupPath(path), Bytes(Project(1)));
        using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var e = Assert.ThrowsAny<IOException>(() => ProjectSaver.Load(path));
            Assert.Contains("Otro programa", e.Message);
        }
        // al soltarlo se abre el principal, el más nuevo
        Assert.Equal(9, ProjectSaver.Load(path).Project.Layers[0].Png[32]);
    }

    // ---------- 5. Límites del lector de .trazo ----------

    [Fact]
    public void TrazoReaderBoundsEntriesAreaAndLayers()
    {
        // una entrada que dice descomprimir a más del tope se rechaza sin descomprimirla (bomba ZIP)
        using (var ms = new MemoryStream())
        {
            ms.Write(Bytes(Project(1)));
            using (var zip = new ZipArchive(ms, ZipArchiveMode.Update, leaveOpen: true))
            {
                zip.GetEntry("layers/000.png")!.Delete();
                using var s = zip.CreateEntry("layers/000.png", CompressionLevel.Fastest).Open();
                var zeros = new byte[1 << 20];
                for (long n = 0; n <= TrazoFile.MaxEntryBytes; n += zeros.Length) s.Write(zeros);
            }
            Assert.True(ms.Length < 16 * 1024 * 1024, $"el archivo de prueba es pequeño ({ms.Length / 1024} kB)");
            var e = Assert.Throws<InvalidDataException>(() => TrazoFile.Read(new MemoryStream(ms.ToArray())));
            Assert.Contains("demasiado grande", e.Message);
        }
        // lienzo con más área de la admitida (16384 × 16384 son 4 veces el máximo)
        var big = Project(1) with { Width = 16384, Height = 16384, Layers = [new(new LayerProperties(), FI01_PersistenceTests.Png(16384, 16384))] };
        Assert.Throws<InvalidDataException>(() => TrazoFile.Read(new MemoryStream(Bytes(big))));
        // más capas de las que caben en ese tamaño
        var max = CanvasLimits.MaxLayers(4096, 4096);
        var many = Project(1) with
        {
            Width = 4096, Height = 4096,
            Layers = [.. Enumerable.Range(0, max + 1).Select(i => new LayerData(new LayerProperties { Id = $"l{i}" }, FI01_PersistenceTests.Png(4096, 4096)))],
        };
        Assert.Contains("capas", Assert.Throws<InvalidDataException>(() => TrazoFile.Read(new MemoryStream(Bytes(many)))).Message);
    }

    // ---------- 2 y 3. PSD: memoria acotada ----------

    private sealed class Be
    {
        public readonly List<byte> B = [];
        public Be U8(int v) { B.Add((byte)v); return this; }
        public Be U16(int v) { B.Add((byte)(v >> 8)); B.Add((byte)v); return this; }
        public Be U32(long v) { for (var i = 3; i >= 0; i--) B.Add((byte)(v >> (8 * i))); return this; }
        public Be Ascii(string s) { B.AddRange(Encoding.ASCII.GetBytes(s)); return this; }
    }

    [Fact]
    public void PsdCompositeAreaDoesNotOverflow()
    {
        // gris RLE de 30000 × 30000: el área por 4 no cabe en un int. Antes: OverflowException
        const int side = 30000;
        var row = new List<byte>();
        Psd.PackBits(new byte[side], row);
        var file = new Be().Ascii("8BPS").U16(1).U32(0).U16(0).U16(1).U32(side).U32(side).U16(8).U16(1).U32(0).U32(0).U32(0).U16(1);
        var bytes = new byte[file.B.Count + side * 2 + side * row.Count];
        file.B.CopyTo(bytes);
        for (var y = 0; y < side; y++) BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(file.B.Count + y * 2), (ushort)row.Count);
        for (var y = 0; y < side; y++) row.CopyTo(bytes, file.B.Count + side * 2 + y * row.Count);
        Assert.Contains("demasiado grande", Assert.Throws<InvalidDataException>(() => Psd.Read(bytes)).Message);
    }

    [Fact]
    public void PsdLayersWithoutChannelsReserveNothing()
    {
        // 20 capas de 23170 × 23170 sin ningún canal: antes reservaba unos 40 GB y acababa en OutOfMemoryException
        var rec = new Be().U16(20);
        for (var i = 0; i < 20; i++)
            rec.U32(0).U32(0).U32(23170).U32(23170).U16(0).Ascii("8BIMnorm").U8(255).U8(0).U8(0).U8(0).U32(8).U32(0).U32(0);
        if (rec.B.Count % 2 != 0) rec.U8(0);
        var file = new Be().Ascii("8BPS").U16(1).U32(0).U16(0).U16(3).U32(4).U32(4).U16(8).U16(3).U32(0).U32(0)
            .U32(rec.B.Count + 4).U32(rec.B.Count);
        file.B.AddRange(rec.B);
        file.U16(0);
        file.B.AddRange(new byte[4 * 4 * 3]);
        var psd = Psd.Read(file.B.ToArray());
        Assert.Equal(20, psd.Layers.Count);
        Assert.All(psd.Layers, l => Assert.Equal((0, 0), (l.Width, l.Height)));
    }

    // ---------- 6. Un fotograma que falla a medias no estropea los siguientes ----------

    /// <summary>Flujo en memoria que falla a mitad de una escritura cuando se le pide.</summary>
    private sealed class FlakyStream : MemoryStream
    {
        public bool FailNext;

        // MemoryStream llama de un Write al otro: la lógica va en uno solo y el otro copia a un arreglo
        public override void Write(ReadOnlySpan<byte> buffer) => Write(buffer.ToArray(), 0, buffer.Length);

        public override void Write(byte[] buffer, int offset, int count)
        {
            if (FailNext && count > 8)
            {
                FailNext = false;
                base.Write(buffer, offset, count / 2);
                throw new IOException("disco lleno (simulado)");
            }
            base.Write(buffer, offset, count);
        }
    }

    [Fact]
    public void FailedTimelapseFrameDoesNotHideLaterFrames()
    {
        var s = new FlakyStream();
        using var w = TimelapseWriter.Create(s, 32, 32);
        byte[] Frame(int n) => [.. Enumerable.Range(0, 40 + n).Select(i => (byte)(i + n))];
        w.Append(Frame(0));
        w.Append(Frame(1));
        s.FailNext = true;
        Assert.Throws<IOException>(() => w.Append(Frame(2)));
        for (var i = 3; i < 13; i++) w.Append(Frame(i));
        // antes: el fotograma a medias desalineaba el archivo y solo se veían 3
        var (frames, _) = TimelapseFile.Scan(s);
        Assert.Equal(12, frames.Count);
        Assert.Equal(12, w.Count);
    }

    // ---------- 7. Una acción que falla al deshacer no deja memoria fantasma ----------

    private sealed class Failing(long bytes) : IUndoableAction
    {
        public string Label => "falla";
        public long Bytes { get; } = bytes;
        public void Undo() => throw new InvalidOperationException("falla al deshacer");
        public void Redo() { }
    }

    [Fact]
    public void FailingUndoReleasesItsBytes()
    {
        var h = new UndoStack(maxSteps: 10, maxBytes: 100);
        h.Push(new Failing(60));
        Assert.Equal(60, h.Bytes);
        Assert.Throws<InvalidOperationException>(() => h.Undo());
        Assert.Equal(0, h.Bytes);
        Assert.Equal(0, h.UndoCount);
        Assert.Equal(0, h.RedoCount);
    }
}
