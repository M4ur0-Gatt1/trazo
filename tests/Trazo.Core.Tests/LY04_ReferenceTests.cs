using System.IO.Compression;
using Trazo.Core.Documents;
using Trazo.Core.IO;

namespace Trazo.Core.Tests;

/// <summary>LY-04 — Capa de referencia (docs/specs/LY-04-reference-layer.md), parte sin GPU.</summary>
public sealed class LY04_ReferenceTests
{
    private static readonly byte[] FakePng = [0x89, 0x50, 0x4E, 0x47, 1, 2, 3];

    private static ProjectData Project(params bool[] refs) => new()
    {
        Name = "ref", Width = 64, Height = 64,
        Layers = [.. refs.Select((r, i) => new LayerData(new LayerProperties { Name = $"c{i}", Reference = r }, FakePng))],
        ThumbnailPng = FakePng,
    };

    private static (ProjectData Read, string Manifest) RoundTrip(ProjectData p)
    {
        using var ms = new MemoryStream();
        TrazoFile.Write(ms, p);
        ms.Position = 0;
        string manifest;
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Read, leaveOpen: true))
        using (var r = new StreamReader(zip.GetEntry("manifest.json")!.Open())) manifest = r.ReadToEnd();
        ms.Position = 0;
        return (TrazoFile.Read(ms), manifest);
    }

    // ---------- LY-04.1 ----------

    [Fact]
    public void ReferenceFlagRoundTripsInTrazo()
    {
        var (read, manifest) = RoundTrip(Project(false, true, false));
        Assert.Equal([false, true, false], read.Layers.Select(l => l.Properties.Reference));
        // solo se escribe en la capa que lo tiene: un archivo sin referencia es igual que antes
        Assert.Equal(1, manifest.Split("\"reference\"").Length - 1);
        var (none, plain) = RoundTrip(Project(false, false));
        Assert.DoesNotContain("reference", plain);
        Assert.All(none.Layers, l => Assert.False(l.Properties.Reference));

        // dos marcadas (archivo editado a mano): se queda la primera
        var (two, _) = RoundTrip(Project(false, true, true));
        Assert.Equal([false, true, false], two.Layers.Select(l => l.Properties.Reference));
    }
}
