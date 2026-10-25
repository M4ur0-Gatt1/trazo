using Trazo.Core.Documents;
using Trazo.Core.IO;

namespace Trazo.Core.Tests;

/// <summary>RC-01 — Recuperación tras fallos (docs/specs/RC-01-recovery.md), parte sin GPU.</summary>
public sealed class RC01_RecoveryTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("trazo-rc01-").FullName;
    private static readonly DateTimeOffset T0 = new(2026, 10, 25, 12, 0, 0, TimeSpan.Zero);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string Sessions => Path.Combine(_dir, "sessions");

    [Fact]
    public void AbandonedSessionIsDetected()
    {
        var j = new SessionJournal(Sessions, processId: 4242, processStart: 111);
        j.Begin(@"C:\obras\gato.trazo", T0);
        j.MarkSaved(T0.AddMinutes(3));
        // el proceso 4242 ya no existe: nadie llamó a End()
        var found = SessionJournal.CollectAbandoned(Sessions, (_, _) => false, out var discarded);
        var e = Assert.Single(found);
        Assert.Equal(@"C:\obras\gato.trazo", e.Document);
        Assert.Equal(T0.AddMinutes(3), e.LastSaved);
        Assert.Equal(0, discarded);
        // se avisa una sola vez
        Assert.Empty(SessionJournal.CollectAbandoned(Sessions, (_, _) => false, out _));
    }

    [Fact]
    public void LiveSessionIsIgnored()
    {
        new SessionJournal(Sessions, processId: 10, processStart: 1).Begin("vivo.trazo", T0);
        new SessionJournal(Sessions, processId: 20, processStart: 2).Begin("muerto.trazo", T0);
        var found = SessionJournal.CollectAbandoned(Sessions, (pid, _) => pid == 10, out _);
        Assert.Equal("muerto.trazo", Assert.Single(found).Document);
        Assert.True(File.Exists(Path.Combine(Sessions, "session-10.json")));
        // y la instancia real (este proceso de pruebas) cuenta como viva
        var me = new SessionJournal(Sessions);
        me.Begin("yo.trazo", T0);
        Assert.DoesNotContain(SessionJournal.CollectAbandoned(Sessions, null, out _), s => s.Document == "yo.trazo");
        me.End();
    }

    [Fact]
    public void ReusedPidIsAbandoned()
    {
        using var me = System.Diagnostics.Process.GetCurrentProcess();
        // mismo pid que este proceso pero otra hora de inicio: es un pid reutilizado
        new SessionJournal(Sessions, processId: me.Id, processStart: 12345).Begin("viejo.trazo", T0);
        var found = SessionJournal.CollectAbandoned(Sessions, null, out _);
        Assert.Equal("viejo.trazo", Assert.Single(found).Document);
    }

    [Fact]
    public void CleanEndRemovesJournal()
    {
        var j = new SessionJournal(Sessions, processId: 7, processStart: 7);
        j.Begin("a.trazo", T0);
        Assert.True(File.Exists(j.FilePath));
        j.End();
        Assert.False(File.Exists(j.FilePath));
        Assert.Empty(SessionJournal.CollectAbandoned(Sessions, (_, _) => false, out _));
    }

    [Fact]
    public void CorruptJournalIsDiscarded()
    {
        Directory.CreateDirectory(Sessions);
        File.WriteAllText(Path.Combine(Sessions, "session-99.json"), "{ esto no es json");
        File.WriteAllText(Path.Combine(Sessions, "session-98.json"), "null");
        var found = SessionJournal.CollectAbandoned(Sessions, (_, _) => false, out var discarded);
        Assert.Empty(found);
        Assert.Equal(2, discarded);
        Assert.Empty(Directory.GetFiles(Sessions));
        // sin carpeta tampoco falla
        Assert.Empty(SessionJournal.CollectAbandoned(Path.Combine(_dir, "no-existe"), null, out _));
    }

    [Fact]
    public void SaveUpdatesLastSaved()
    {
        var j = new SessionJournal(Sessions, processId: 5, processStart: 5);
        j.Begin("a.trazo", T0);
        j.MarkSaved(T0.AddSeconds(3));
        j.MarkSaved(T0.AddSeconds(9), "b.trazo");
        var e = Assert.Single(SessionJournal.CollectAbandoned(Sessions, (_, _) => false, out _));
        Assert.Equal(T0.AddSeconds(9), e.LastSaved);
        Assert.Equal("b.trazo", e.Document);
        Assert.Equal(T0, e.Opened);
    }

    [Fact]
    public void EmergencyCopyLeavesPrimaryUntouched()
    {
        var primary = Path.Combine(_dir, "paisaje.trazo");
        ProjectData Project(byte marker) => new()
        {
            Name = "paisaje", Width = 32, Height = 24,
            Layers = [new(new LayerProperties { Id = "a" }, FI01_PersistenceTests.Png(32, 24, marker))],
        };
        ProjectSaver.Save(primary, Project(1));
        var before = File.ReadAllBytes(primary);

        var saver = new EmergencySaver();
        var copy = saver.TrySave(primary, Project(2), T0);
        Assert.Equal(Path.Combine(_dir, "paisaje (recuperado).trazo"), copy);
        Assert.Equal(before, File.ReadAllBytes(primary));                     // el principal no cambia
        var loaded = ProjectSaver.Load(copy!).Project;
        Assert.Equal("paisaje (recuperado)", loaded.Name);
        Assert.Equal(FI01_PersistenceTests.Png(32, 24, 2), loaded.Layers[0].Png);

        Assert.Null(saver.TrySave(primary, Project(3), T0.AddSeconds(30)));    // menos de un minuto: no
        Assert.NotNull(saver.TrySave(primary, Project(3), T0.AddSeconds(61)));
    }
}
