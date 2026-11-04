using Trazo.Core.IO;
using Xunit;

namespace Trazo.Core.Tests;

/// <summary>FA-01: abrir desde el Explorador.</summary>
public class FA01_LaunchFileTests
{
    [Theory]
    [InlineData(@"C:\a\obra.trazo", LaunchKind.Project)]
    [InlineData(@"C:\a\OBRA.TRAZO", LaunchKind.Project)]
    [InlineData(@"C:\a\capas.psd", LaunchKind.Psd)]
    [InlineData(@"C:\a\foto.png", LaunchKind.Image)]
    [InlineData(@"C:\a\foto.JPG", LaunchKind.Image)]
    [InlineData(@"C:\a\foto.jpeg", LaunchKind.Image)]
    [InlineData(@"C:\a\foto.webp", LaunchKind.Image)]
    [InlineData(@"C:\a\foto.tiff", LaunchKind.Image)]
    [InlineData(@"C:\a\notas.txt", LaunchKind.Unsupported)]
    [InlineData(@"C:\a\sin extension", LaunchKind.Unsupported)]
    public void FA01_1_Classify(string path, LaunchKind expected) => Assert.Equal(expected, LaunchFile.Classify(path));

    /// <summary>Regresión (2026-11-03): un PNG con Trazo como programa predeterminado abría la galería sin hacer nada.</summary>
    [Fact]
    public void FA01_2_ImageFromExplorerIsPicked()
    {
        var png = @"C:\Users\x\Downloads\Historieta en la carnicería y el alias.png";
        Assert.Equal(png, LaunchFile.Pick([png], _ => true));
        Assert.Equal(LaunchKind.Image, LaunchFile.Classify(png));
    }

    [Fact]
    public void FA01_3_OptionsAndMissingFilesAreIgnored()
    {
        var exists = new HashSet<string> { @"C:\b.png" };
        Assert.Equal(@"C:\b.png", LaunchFile.Pick(["--lang", @"C:\falta.trazo", @"C:\b.png"], exists.Contains));
        Assert.Null(LaunchFile.Pick(["--algo"], _ => true));
        Assert.Null(LaunchFile.Pick([], _ => true));
    }

    [Fact]
    public void FA01_4_SupportedPreferredUnsupportedStillReported()
    {
        Assert.Equal(@"C:\b.psd", LaunchFile.Pick([@"C:\a.txt", @"C:\b.psd"], _ => true));
        // nada que se pueda abrir: se devuelve igualmente para avisar en vez de no hacer nada
        Assert.Equal(@"C:\a.txt", LaunchFile.Pick([@"C:\a.txt"], _ => true));
    }

    [Fact]
    public void FA01_5_SameDocumentOpenInAnotherLiveInstanceIsDetected()
    {
        var dir = Path.Combine(Path.GetTempPath(), "fa01-" + Guid.NewGuid().ToString("N"));
        try
        {
            var doc = Path.Combine(dir, "Obra.trazo");
            new SessionJournal(dir, processId: 111, processStart: 5).Begin(doc, DateTimeOffset.Now);
            new SessionJournal(dir, processId: 222, processStart: 6).Begin(Path.Combine(dir, "Otra.trazo"), DateTimeOffset.Now);
            File.WriteAllText(Path.Combine(dir, "session-333.json"), "{dañado");

            Assert.True(SessionJournal.IsOpenElsewhere(dir, doc.ToUpperInvariant(), ownProcessId: 999, (_, _) => true));
            // la propia instancia no cuenta
            Assert.False(SessionJournal.IsOpenElsewhere(dir, doc, ownProcessId: 111, (_, _) => true));
            // un proceso muerto tampoco (eso es una sesión abandonada, RC-01)
            Assert.False(SessionJournal.IsOpenElsewhere(dir, doc, ownProcessId: 999, (_, _) => false));
            Assert.False(SessionJournal.IsOpenElsewhere(dir, Path.Combine(dir, "Nueva.trazo"), 999, (_, _) => true));
            // no borra nada
            Assert.Equal(3, Directory.GetFiles(dir, "session-*.json").Length);
            Assert.False(SessionJournal.IsOpenElsewhere(Path.Combine(dir, "no-existe"), doc, 999));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }
}
