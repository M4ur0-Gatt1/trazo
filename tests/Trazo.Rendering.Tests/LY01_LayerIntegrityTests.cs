using System.Security.Cryptography;
using Trazo.Core.Brushes;
using Trazo.Core.Documents;
using Trazo.Core.IO;
using Windows.UI;

namespace Trazo.Rendering.Tests;

/// <summary>
/// LY-01 — Integridad de capas (docs/protocols/LY-01.md) y FI-01 de extremo a extremo con el
/// renderizador real: cada operación deja un estado coherente, deshacer restaura exactamente el
/// estado anterior y guardar y reabrir no cambia el contenido.
/// </summary>
public class LY01_LayerIntegrityTests
{
    private sealed record Snapshot(string Structure, string[] LayerHashes, string Flat);

    private static string Hash(byte[] b) => Convert.ToHexString(SHA256.HashData(b))[..16];

    private static Snapshot Take(PaintDocument d)
    {
        var structure = string.Join("|", d.Layers.Select(l => $"{l.Id}:{l.Name}:{l.Opacity:0.###}:{l.Blend}:{l.Visible}:{l.AlphaLock}:{l.Clip}"))
                        + $"|bg:{d.Background}:{d.BackgroundVisible}|active:{d.ActiveIndex}";
        using var flat = d.Flatten();
        return new Snapshot(structure, d.Layers.Select(l => Hash(l.Surface.GetPixelBytes())).ToArray(), Hash(flat.GetPixelBytes()));
    }

    private static void AssertSame(Snapshot a, Snapshot b, string step)
    {
        Assert.True(a.Structure == b.Structure, $"{step}: estructura distinta\n{a.Structure}\n{b.Structure}");
        Assert.True(a.LayerHashes.SequenceEqual(b.LayerHashes), $"{step}: píxeles de capa distintos");
        Assert.True(a.Flat == b.Flat, $"{step}: imagen compuesta distinta");
    }

    /// <summary>Secuencia de operaciones representativa; cada una debe poder deshacerse y rehacerse.</summary>
    private static List<(string Name, Action Run)> Operations(PaintDocument doc, LayerOperations ops, BrushEngine engine)
    {
        var lib = new BrushLibrary();
        return
        [
            ("pintar capa 1", () => BR02_BrushReferenceTests.Paint(engine, lib["roundbrush"])),
            ("nueva capa", () => ops.Add()),
            ("pintar capa 2", () => BR02_BrushReferenceTests.Paint(engine, lib["charcoal"])),
            ("opacidad", () => ops.Set(doc.ActiveLayer, "opacity", 0.6f, l => l.Opacity, (l, v) => l.Opacity = v, "opacidad")),
            ("modo multiplicar", () => ops.Set(doc.ActiveLayer, "blend", BlendMode.Multiply, l => l.Blend, (l, v) => l.Blend = v, "fusión")),
            ("duplicar", () => ops.Duplicate(doc.ActiveIndex)),
            ("máscara de recorte", () => ops.Set(doc.ActiveLayer, "clip", true, l => l.Clip, (l, v) => l.Clip = v, "recorte")),
            ("ocultar", () => ops.Set(doc.Layers[0], "visible", false, l => l.Visible, (l, v) => l.Visible = v, "visibilidad")),
            ("mostrar", () => ops.Set(doc.Layers[0], "visible", true, l => l.Visible, (l, v) => l.Visible = v, "visibilidad")),
            ("mover capa", () => ops.Move(doc.ActiveIndex, 0)),
            ("combinar abajo", () => ops.MergeDown(doc.Layers.Count - 1)),
            ("rellenar", () => engine.Fill(5, 5, Color.FromArgb(255, 250, 200, 40), 46)),
            ("invertir", () => ops.Invert(0)),
            ("voltear lienzo", () => ops.FlipCanvas(true)),
            ("fondo", () => ops.SetBackground(Color.FromArgb(255, 20, 20, 30), true)),
            ("eliminar capa", () => ops.Remove(0)),
        ];
    }

    [Fact]
    public void EveryOperationUndoesAndRedoesExactly()
    {
        var (doc, ops, engine) = BR02_BrushReferenceTests.NewDocument();
        using (doc)
        {
            var states = new List<Snapshot> { Take(doc) };
            var steps = Operations(doc, ops, engine);
            foreach (var (name, run) in steps)
            {
                var count = doc.History.UndoCount;
                run();
                Assert.True(doc.History.UndoCount == count + 1, $"«{name}» no registró su propio paso de historial");
                Assert.InRange(doc.ActiveIndex, 0, doc.Layers.Count - 1);
                states.Add(Take(doc));
            }

            // deshacer todo, comprobando cada estado intermedio
            for (var i = states.Count - 2; i >= 0 && doc.History.CanUndo; i--)
            {
                doc.History.Undo();
                if (doc.History.UndoCount == i) AssertSame(states[i], Take(doc), $"deshacer hasta el paso {i}");
            }
            AssertSame(states[0], Take(doc), "estado inicial tras deshacer todo");

            while (doc.History.CanRedo) doc.History.Redo();
            AssertSame(states[^1], Take(doc), "estado final tras rehacer todo");
        }
    }

    [Fact]
    public async Task SaveAndReopenPreservesDocument()
    {
        var (doc, ops, engine) = BR02_BrushReferenceTests.NewDocument();
        using var tmp = new TempFolder();
        using (doc)
        {
            foreach (var (_, run) in Operations(doc, ops, engine).Take(9)) run();
            var file = tmp.File("obra.trazo");
            await ProjectSaver.SaveAsync(file, await doc.ToProjectDataAsync());
            var loaded = ProjectSaver.Load(file);
            Assert.False(loaded.Recovered);
            using var reopened = await PaintDocument.FromProjectDataAsync(Gpu.Device, loaded.Project, file);

            var a = Take(doc);
            var b = Take(reopened);
            Assert.Equal(a.Structure, b.Structure);
            // PNG guarda color sin premultiplicar: en píxeles casi transparentes el redondeo puede variar 1 nivel
            for (var i = 0; i < doc.Layers.Count; i++)
            {
                var diff = Golden.Compare(doc.Layers[i].Surface.GetPixelBytes(), reopened.Layers[i].Surface.GetPixelBytes(), doc.Width, doc.Height);
                Assert.True(diff.MaxChannelDiff <= 1, $"capa {i + 1}: {diff}");
            }
        }
    }

    [Fact]
    public async Task InterruptedSaveDoesNotDamageExistingFile()
    {
        var (doc, _, engine) = BR02_BrushReferenceTests.NewDocument();
        using var tmp = new TempFolder();
        using (doc)
        {
            BR02_BrushReferenceTests.Paint(engine, new BrushLibrary()["studiopen"]);
            var file = tmp.File("obra.trazo");
            await ProjectSaver.SaveAsync(file, await doc.ToProjectDataAsync());
            var original = await File.ReadAllBytesAsync(file);

            // el archivo de destino bloqueado por otro programa hace fallar el reemplazo
            using (new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                var data = await doc.ToProjectDataAsync();
                var e = await Assert.ThrowsAsync<SaveFailedException>(() => ProjectSaver.SaveAsync(file, data));
                Assert.Equal(SaveStage.Replace, e.Stage);
            }
            Assert.Equal(original, await File.ReadAllBytesAsync(file));
            Assert.False(File.Exists(ProjectSaver.TempPath(file)));
        }
    }
}
