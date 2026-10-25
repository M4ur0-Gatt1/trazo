using System.Numerics;
using System.Security.Cryptography;
using Trazo.Core.Brushes;
using Trazo.Core.Documents;
using Trazo.Core.IO;
using Trazo.Core.Selection;
using Trazo.Core.Strokes;
using Windows.UI;

namespace Trazo.Rendering.Tests;

/// <summary>
/// SE-01 (selección), LY-02 (máscaras) y LY-03 (grupos) con el motor real sobre WARP
/// (docs/specs/SE-01-selection.md, docs/specs/LY-02-LY-03-masks-groups.md).
/// </summary>
public class SE01_LY02_LY03_RenderTests
{
    private const int W = BR02_BrushReferenceTests.Width, H = BR02_BrushReferenceTests.Height;
    private static readonly Color Red = Color.FromArgb(255, 220, 30, 40);
    private static readonly BrushLibrary Lib = new();

    private static (PaintDocument Doc, LayerOperations Ops, BrushEngine Engine) NewDoc() => BR02_BrushReferenceTests.NewDocument();

    private static void Stroke(BrushEngine engine, ToolKind tool, string brush, Color color, float size, float y = H / 2f, float x0 = 10, float x1 = W - 10)
    {
        var pts = SyntheticStrokes.Sample(SyntheticStrokes.Line(new(x0, y), new(x1, y)), 500, 240);
        engine.Begin(pts[0], tool, Lib[brush], color, size, 1, SymmetryMode.None, 1, 1);
        foreach (var p in pts.Skip(1)) engine.Add(p);
        engine.End();
    }

    private static byte A(byte[] px, int x, int y) => px[(y * W + x) * 4 + 3];
    private static string Hash(byte[] b) => Convert.ToHexString(SHA256.HashData(b))[..16];

    /// <summary>Índices de píxel sin ninguna cobertura de selección.</summary>
    private static IEnumerable<int> Outside(SelectionMask s) => Enumerable.Range(0, W * H).Where(i => s.Data[i] == 0);

    private static void AssertOutsideUnchanged(byte[] before, byte[] after, SelectionMask s, string what)
    {
        foreach (var i in Outside(s))
            for (var c = 0; c < 4; c++)
                Assert.True(before[i * 4 + c] == after[i * 4 + c], $"{what}: cambió el píxel ({i % W}, {i / W}) fuera de la selección");
        Assert.False(before.SequenceEqual(after), $"{what}: no cambió nada dentro de la selección");
    }

    // ---------- SE-01 ----------

    [Fact]
    public void ToolsOnlyAffectSelection()
    {
        var (doc, ops, engine) = NewDoc();
        using (doc)
        {
            var layer = doc.ActiveLayer;
            Stroke(engine, ToolKind.Brush, "roundbrush", Red, 60); // contenido inicial en toda la anchura
            var sel = SelectionMask.Ellipse(W, H, 40, 20, 200, 140);
            ops.SetSelection(sel, "sel");

            var cases = new (string Name, Action Run)[]
            {
                ("pincel", () => Stroke(engine, ToolKind.Brush, "studiopen", Color.FromArgb(255, 0, 0, 255), 30, H / 2f + 20)),
                ("borrador", () => Stroke(engine, ToolKind.Eraser, "hardair", Red, 40)),
                ("difuminar", () => Stroke(engine, ToolKind.Smudge, "roundbrush", Red, 40, H / 2f - 10)),
                ("relleno", () => engine.Fill(100, 20, Color.FromArgb(255, 0, 200, 0), 46)),
                ("ajuste", () => new AdjustmentSession(doc, layer, Adjustment.All.First(a => a.Id == "invert")).Apply(ops)),
                ("invertir", () => ops.Invert(0)),
                ("borrar contenido", () => ops.Clear(0)),
            };
            foreach (var (name, run) in cases)
            {
                var before = layer.Surface.GetPixelBytes();
                run();
                AssertOutsideUnchanged(before, layer.Surface.GetPixelBytes(), sel, name);
            }
        }
    }

    [Fact]
    public void BrushPreviewRespectsSelection()
    {
        var (doc, ops, engine) = NewDoc();
        using (doc)
        {
            ops.SetSelection(SelectionMask.Rectangle(W, H, 0, 0, W / 2f, H), "sel");
            var pts = SyntheticStrokes.Sample(SyntheticStrokes.Line(new(10, 80), new(370, 80)), 400, 240);
            engine.Begin(pts[0], ToolKind.Brush, Lib["monoline"], Red, 20, 1, SymmetryMode.None, 1, 1);
            foreach (var p in pts.Skip(1)) engine.Add(p);
            var owned = new List<IDisposable>();
            using var rt = new Microsoft.Graphics.Canvas.CanvasRenderTarget(Gpu.Device, W, H, 96);
            using (var ds = rt.CreateDrawingSession()) { ds.Clear(KnownColors.Transparent); ds.DrawImage(engine.Preview(owned)!); }
            owned.ForEach(o => o.Dispose());
            var px = rt.GetPixelBytes();
            Assert.True(A(px, 100, 80) > 200, "dentro de la selección la vista previa debe mostrar el trazo");
            Assert.Equal(0, A(px, 300, 80));
            engine.End();
        }
    }

    [Fact]
    public void TransformMovesOnlySelection()
    {
        var (doc, ops, _) = NewDoc();
        using (doc)
        {
            var layer = doc.ActiveLayer;
            ops.Fill(0, Red); // capa entera roja
            ops.SetSelection(SelectionMask.Rectangle(W, H, 40, 40, 140, 120), "sel");
            var t = TransformSession.Start(doc, layer)!;
            t.Matrix = Matrix3x2.CreateTranslation(60, 0);
            t.Apply(ops);
            t.Dispose();
            var px = layer.Surface.GetPixelBytes();
            Assert.Equal(0, A(px, 60, 80));        // de donde salió el contenido (no cubierto por el destino)
            Assert.Equal(255, A(px, 180, 80));     // destino
            Assert.Equal(255, A(px, 20, 20));      // fuera de la selección: intacto
            Assert.Equal(100, doc.Selection!.Bounds.X); // la selección se movió con su contenido
        }
    }

    [Fact]
    public void CopyPasteSelection()
    {
        var (doc, ops, engine) = NewDoc();
        using (doc)
        {
            Stroke(engine, ToolKind.Brush, "roundbrush", Red, 60);
            var src = doc.ActiveLayer.Surface.GetPixelBytes();
            var sel = SelectionMask.Ellipse(W, H, 100, 30, 260, 130);
            ops.SetSelection(sel, "sel");
            var pasted = ops.CopySelectionToLayer()!;
            Assert.Equal(2, doc.Layers.Count);
            var px = pasted.Surface.GetPixelBytes();
            for (var i = 0; i < W * H; i++)
            {
                var expected = src[i * 4 + 3] * sel.Data[i] / 255.0;
                Assert.True(Math.Abs(px[i * 4 + 3] - expected) <= 1.5, $"píxel {i % W},{i / W}: {px[i * 4 + 3]} ≠ {expected:0.0}");
            }
        }
    }

    [Fact]
    public void SelectLayerContentUsesAlpha()
    {
        var (doc, ops, engine) = NewDoc();
        using (doc)
        {
            ops.SelectLayerContent(doc.ActiveLayer);
            Assert.Null(doc.Selection); // capa vacía: no hay nada que seleccionar
            Stroke(engine, ToolKind.Brush, "roundbrush", Red, 40);
            var px = doc.ActiveLayer.Surface.GetPixelBytes();
            ops.SelectLayerContent(doc.ActiveLayer);
            var sel = doc.Selection!;
            for (var i = 0; i < W * H; i++) Assert.Equal(px[i * 4 + 3], sel.Data[i]);
            doc.History.Undo();
            Assert.Null(doc.Selection);
        }
    }

    [Fact]
    public void SelectionChangesAreUndoable()
    {
        var (doc, ops, _) = NewDoc();
        using (doc)
        {
            var a = SelectionMask.Rectangle(W, H, 0, 0, 50, 50);
            ops.SetSelection(a, "a");
            ops.Select(SelectionMask.Rectangle(W, H, 40, 40, 90, 90), SelectionCombine.Add, "b");
            var combined = doc.Selection;
            ops.InvertSelection();
            doc.History.Undo();
            Assert.Same(combined, doc.Selection);
            doc.History.Undo();
            Assert.Same(a, doc.Selection);
            doc.History.Undo();
            Assert.Null(doc.Selection);
            doc.History.Redo();
            Assert.Same(a, doc.Selection);
        }
    }

    // ---------- LY-02: máscaras ----------

    [Fact]
    public void WhiteMaskIsInvisible()
    {
        var (doc, ops, engine) = NewDoc();
        using (doc)
        {
            Stroke(engine, ToolKind.Brush, "roundbrush", Red, 60);
            using var before = doc.Flatten();
            ops.AddMask(doc.ActiveLayer);
            using var after = doc.Flatten();
            Assert.Equal(before.GetPixelBytes(), after.GetPixelBytes());
        }
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(128, 128)]
    [InlineData(255, 255)]
    public void MaskLuminanceControlsVisibility(byte gray, int expectedAlpha)
    {
        var (doc, ops, _) = NewDoc();
        using (doc)
        {
            var layer = doc.ActiveLayer;
            ops.Fill(0, Red);
            ops.AddMask(layer);
            ops.Fill(0, Color.FromArgb(255, gray, gray, gray)); // con la máscara en edición, rellena la máscara
            using var flat = doc.Flatten(includeBackground: false);
            Assert.InRange(A(flat.GetPixelBytes(), 100, 80), expectedAlpha - 2, expectedAlpha + 2);
            Assert.Equal(255, A(layer.Surface.GetPixelBytes(), 100, 80)); // la capa no se tocó
        }
    }

    [Fact]
    public void PaintingTargetsMask()
    {
        var (doc, ops, engine) = NewDoc();
        using (doc)
        {
            var layer = doc.ActiveLayer;
            ops.Fill(0, Red);
            ops.AddMask(layer);
            var layerBefore = Hash(layer.Surface.GetPixelBytes());
            Stroke(engine, ToolKind.Brush, "monoline", Color.FromArgb(255, 0, 0, 0), 30); // negro oculta
            Assert.Equal(layerBefore, Hash(layer.Surface.GetPixelBytes()));
            Assert.True(layer.Mask!.GetPixelBytes()[(80 * W + 190) * 4] < 10, "el trazo negro debe quedar en la máscara");
            using var flat = doc.Flatten(includeBackground: false);
            Assert.True(A(flat.GetPixelBytes(), 190, 80) < 10, "donde la máscara es negra la capa no se ve");

            // el borrador sobre la máscara vuelve a mostrar (restaura blanco)
            Stroke(engine, ToolKind.Eraser, "monoline", Red, 40);
            Assert.True(layer.Mask!.GetPixelBytes()[(80 * W + 190) * 4] > 245);
        }
    }

    [Fact]
    public void ApplyMaskPreservesComposite()
    {
        var (doc, ops, engine) = NewDoc();
        using (doc)
        {
            var layer = doc.ActiveLayer;
            Stroke(engine, ToolKind.Brush, "roundbrush", Red, 80);
            ops.AddMask(layer);
            Stroke(engine, ToolKind.Brush, "softair", Color.FromArgb(255, 0, 0, 0), 90); // borde suave en la máscara
            using var before = doc.Flatten();
            ops.ApplyMask(layer);
            Assert.Null(layer.Mask);
            using var after = doc.Flatten();
            var diff = Golden.Compare(before.GetPixelBytes(), after.GetPixelBytes(), W, H);
            Assert.True(diff.MaxChannelDiff <= 1, diff.ToString());
        }
    }

    [Fact]
    public void InvertMaskTwice()
    {
        var (doc, ops, engine) = NewDoc();
        using (doc)
        {
            var layer = doc.ActiveLayer;
            ops.AddMask(layer);
            Stroke(engine, ToolKind.Brush, "softair", Color.FromArgb(255, 0, 0, 0), 90);
            var m = layer.Mask!.GetPixelBytes();
            ops.InvertMask(layer);
            Assert.NotEqual(m, layer.Mask!.GetPixelBytes());
            ops.InvertMask(layer);
            Assert.Equal(m, layer.Mask!.GetPixelBytes());
        }
    }

    // ---------- LY-03: grupos ----------

    private static (PaintDocument, LayerOperations, BrushEngine) ThreeLayers()
    {
        var (doc, ops, engine) = NewDoc();
        Stroke(engine, ToolKind.Brush, "roundbrush", Red, 60, 50);
        ops.Add();
        Stroke(engine, ToolKind.Brush, "roundbrush", Color.FromArgb(255, 30, 160, 60), 60, 80);
        ops.Add();
        Stroke(engine, ToolKind.Brush, "roundbrush", Color.FromArgb(255, 40, 60, 200), 60, 110);
        return (doc, ops, engine);
    }

    [Fact]
    public void NeutralGroupIsInvisible()
    {
        var (doc, ops, _) = ThreeLayers();
        using (doc)
        {
            using var before = doc.Flatten();
            ops.GroupWithBelow(2); // capas 1 y 2 en un grupo
            Assert.Equal(doc.Layers[1].GroupId, doc.Layers[2].GroupId);
            using var after = doc.Flatten();
            var diff = Golden.Compare(before.GetPixelBytes(), after.GetPixelBytes(), W, H);
            Assert.True(diff.MaxChannelDiff <= 1, diff.ToString());
        }
    }

    [Fact]
    public void GroupOpacityAndVisibility()
    {
        var (doc, ops, _) = ThreeLayers();
        using (doc)
        {
            ops.GroupWithBelow(2);
            var g = doc.Group(doc.Layers[2].GroupId)!;
            using (var full = doc.Flatten(includeBackground: false))
                Assert.Equal(255, A(full.GetPixelBytes(), 190, 110));
            ops.SetGroup(g, 0.5f, x => x.Opacity, (x, v) => x.Opacity = v, "opacidad");
            using (var half = doc.Flatten(includeBackground: false))
                Assert.InRange(A(half.GetPixelBytes(), 190, 110), 125, 130);
            ops.SetGroup(g, false, x => x.Visible, (x, v) => x.Visible = v, "visible");
            using (var hidden = doc.Flatten(includeBackground: false))
            {
                Assert.Equal(0, A(hidden.GetPixelBytes(), 190, 110));  // capa del grupo
                Assert.Equal(255, A(hidden.GetPixelBytes(), 190, 50)); // capa fuera del grupo
            }
        }
    }

    [Fact]
    public void MergeGroupPreservesComposite()
    {
        var (doc, ops, _) = ThreeLayers();
        using (doc)
        {
            ops.GroupWithBelow(2);
            var g = doc.Group(doc.Layers[2].GroupId)!;
            ops.SetGroup(g, BlendMode.Multiply, x => x.Blend, (x, v) => x.Blend = v, "fusión");
            ops.SetGroup(g, 0.7f, x => x.Opacity, (x, v) => x.Opacity = v, "opacidad");
            using var before = doc.Flatten();
            ops.MergeGroup(g.Id);
            Assert.Equal(2, doc.Layers.Count);
            Assert.Empty(doc.Groups);
            using var after = doc.Flatten();
            var diff = Golden.Compare(before.GetPixelBytes(), after.GetPixelBytes(), W, H);
            Assert.True(diff.MaxChannelDiff <= 2, diff.ToString());
            doc.History.Undo();
            Assert.Equal(3, doc.Layers.Count);
            Assert.Single(doc.Groups);
        }
    }

    [Fact]
    public void MovingIntoGroupJoinsAndUndoRestores()
    {
        var (doc, ops, _) = ThreeLayers();
        using (doc)
        {
            ops.Add(); // cuarta capa arriba del todo, sin grupo
            ops.GroupWithBelow(2); // grupo con las capas 1 y 2
            var gid = doc.Layers[1].GroupId;
            var top = doc.Layers[3];
            Assert.Null(top.GroupId);
            ops.Move(3, 2); // entre las dos capas del grupo
            Assert.Equal(gid, top.GroupId);
            Assert.True(GroupRules.AreContiguous(doc.GroupIds));
            doc.History.Undo();
            Assert.Null(top.GroupId);
            Assert.Equal(3, doc.Layers.IndexOf(top));
        }
    }

    // ---------- LY-01 ampliado: deshacer exacto y persistencia ----------

    private static string State(PaintDocument d)
    {
        using var flat = d.Flatten();
        return string.Join("|", d.Layers.Select(l => $"{l.Id}:{l.GroupId}:{l.HasMask}:{Hash(l.Surface.GetPixelBytes())}:{(l.Mask is null ? "" : Hash(l.Mask.GetPixelBytes()))}"))
               + "|" + string.Join(",", d.Groups.Select(g => $"{g.Id}:{g.Opacity}:{g.Blend}:{g.Visible}"))
               + $"|sel:{(d.Selection is null ? "-" : Hash(d.Selection.Data))}|{Hash(flat.GetPixelBytes())}";
    }

    [Fact]
    public async Task MasksGroupsSelectionUndoRedoAndPersistence()
    {
        var (doc, ops, engine) = ThreeLayers();
        using var tmp = new TempFolder();
        using (doc)
        {
            var masked = doc.Layers[1];
            var steps = new Action[]
            {
                () => ops.AddMask(doc.Layers[1]),
                () => Stroke(engine, ToolKind.Brush, "softair", Color.FromArgb(255, 0, 0, 0), 80),
                () => ops.GroupWithBelow(2),
                () => ops.SetGroup(doc.Group(doc.Layers[2].GroupId)!, 0.6f, g => g.Opacity, (g, v) => g.Opacity = v, "opacidad"),
                () => ops.SetSelection(SelectionMask.Ellipse(W, H, 20, 20, 200, 140), "sel"),
                () => { doc.Layers[1].EditingMask = false; doc.ActiveIndex = 0; Stroke(engine, ToolKind.Brush, "studiopen", Red, 20, 30); },
                () => ops.InvertMask(doc.Layers[1]),
                () => ops.FlipCanvas(true),
                () => ops.CreateGroup(0),
                () => ops.SelectLayerContent(doc.Layers[0]),
                () => ops.CopySelectionToLayer(),
                () => ops.Move(1, 3),
                () => ops.Ungroup(doc.Layers[0].GroupId!),
                () => ops.AddMask(doc.Layers[0]),
                () => ops.DeleteMask(doc.Layers[0]),
                () => ops.ApplyMask(masked),
                () => ops.MergeGroup(masked.GroupId!),
                () => ops.CreateGroup(0),
                () => ops.DeleteGroup(doc.Layers[0].GroupId!),
                () => ops.Deselect(),
                () => ops.AddMask(doc.Layers[0]),
                () => { doc.ActiveIndex = 0; Stroke(engine, ToolKind.Brush, "softair", Color.FromArgb(255, 0, 0, 0), 80); },
                () => ops.GroupWithBelow(1),
            };
            var states = new List<string> { State(doc) };
            var baseUndo = doc.History.UndoCount;
            foreach (var step in steps)
            {
                step();
                states.Add(State(doc));
                // un paso que no cambia nada haría trivial la comprobación de deshacer
                Assert.True(states[^1] != states[^2], $"el paso {states.Count - 1} no cambió el documento");
                Assert.Equal(baseUndo + states.Count - 1, doc.History.UndoCount); // un paso de historial por operación
            }
            for (var i = states.Count - 2; i >= 0; i--)
            {
                doc.History.Undo();
                Assert.True(states[i] == State(doc), $"deshacer hasta el paso {i}");
            }
            for (var i = 1; i < states.Count; i++)
            {
                doc.History.Redo();
                Assert.True(states[i] == State(doc), $"rehacer el paso {i}");
            }

            var file = tmp.File("v3.trazo");
            await ProjectSaver.SaveAsync(file, await doc.ToProjectDataAsync());
            using var reopened = await PaintDocument.FromProjectDataAsync(Gpu.Device, ProjectSaver.Load(file).Project, file);
            Assert.Equal(doc.Layers.Select(l => (l.Id, l.GroupId, l.HasMask)), reopened.Layers.Select(l => (l.Id, l.GroupId, l.HasMask)));
            Assert.Equal(doc.Groups.Select(g => g.ToProperties()), reopened.Groups.Select(g => g.ToProperties()));
            using var a = doc.Flatten();
            using var b = reopened.Flatten();
            var diff = Golden.Compare(a.GetPixelBytes(), b.GetPixelBytes(), W, H);
            Assert.True(diff.MaxChannelDiff <= 2, diff.ToString());
        }
    }

    // ---------- Referencia visual ----------

    [Fact]
    public async Task MaskAndGroupSceneMatchesReference()
    {
        var (doc, ops, engine) = ThreeLayers();
        using (doc)
        {
            ops.AddMask(doc.Layers[2]);
            doc.ActiveIndex = 2;
            Stroke(engine, ToolKind.Brush, "softair", Color.FromArgb(255, 0, 0, 0), 120, 110, 150, 250);
            ops.GroupWithBelow(2);
            ops.SetGroup(doc.Group(doc.Layers[2].GroupId)!, BlendMode.Multiply, g => g.Blend, (g, v) => g.Blend = v, "fusión");
            using var flat = doc.Flatten();
            var diff = await Golden.CheckAsync(flat, "masks-groups", "scene");
            Assert.True(diff.Within(), diff.ToString());
        }
    }
}
