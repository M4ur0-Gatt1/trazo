using System.Numerics;
using Microsoft.Graphics.Canvas;
using Trazo.Core.Brushes;
using Trazo.Core.IO;
using Trazo.Core.Selection;
using Trazo.Core.Strokes;
using Trazo.Rendering;
using Windows.UI;

namespace Trazo.Rendering.Tests;

/// <summary>
/// Regresiones de la auditoría del motor del 2026-11-02 (docs/results/2026-11-02.md), con WARP. Cada prueba fallaba con
/// el código anterior a la corrección.
/// </summary>
public class AUD02_EngineRegressionTests
{
    private const int W = 200, H = 120;
    private static readonly BrushLibrary Lib = new();
    private static readonly Color Red = Color.FromArgb(255, 220, 30, 40);
    private static readonly Color Blue = Color.FromArgb(255, 30, 40, 220);
    private static readonly Color Green = Color.FromArgb(255, 20, 200, 20);

    private static (PaintDocument Doc, LayerOperations Ops, BrushEngine Engine, TipCache Tips) New()
    {
        var doc = PaintDocument.CreateBlank(Gpu.Device, W, H, "aud");
        doc.BackgroundVisible = false;
        var ops = new LayerOperations(doc);
        var tips = new TipCache(Gpu.Device);
        return (doc, ops, new BrushEngine(doc, ops, tips), tips);
    }

    private static void Stroke(BrushEngine e, ToolKind tool, string brush, Color c, float size, float y = H / 2f)
    {
        var pts = SyntheticStrokes.Sample(SyntheticStrokes.Line(new(10, y), new(W - 10, y)), 300, 240);
        e.Begin(pts[0], tool, Lib[brush], c, size, 1, SymmetryMode.None, 1, 1);
        foreach (var p in pts.Skip(1)) e.Add(p);
        e.End();
    }

    private static int MaxDiff(byte[] a, byte[] b)
    {
        var m = 0;
        for (var i = 0; i < a.Length; i++) m = Math.Max(m, Math.Abs(a[i] - b[i]));
        return m;
    }

    private static byte[] Flat(PaintDocument d)
    {
        using var f = d.Flatten(includeBackground: false);
        return f.GetPixelBytes();
    }

    private static bool Alive(CanvasBitmap b) => Record.Exception(() => b.GetPixelBytes(0, 0, 1, 1)) is null;

    // ---------- 1. La caché de puntas no libera una punta en uso ----------

    [Fact]
    public void TipCacheNeverDisposesTipInUse()
    {
        var (doc, _, engine, tips) = New();
        using (doc)
        {
            tips.Get(TipShape.Round, KnownColors.White);
            for (var i = 0; i < 63; i++) tips.Get(TipShape.Round, Color.FromArgb(255, (byte)i, 1, 2));
            // antes: la punta blanca de una forma nueva vaciaba la caché y liberaba la de color recién entregada
            Assert.Null(Record.Exception(() => Stroke(engine, ToolKind.Brush, "charcoal", Red, 20)));
            Assert.Contains(Flat(doc).Where((_, i) => i % 4 == 3), a => a > 0);
            // la caché sigue acotada y las blancas no cuentan
            for (var i = 0; i < 200; i++) tips.Get(TipShape.Round, Color.FromArgb(255, 9, (byte)i, 9));
            Assert.Equal(TipCache.MaxTinted, tips.TintedCount);
            // la usada más hace poco sobrevive
            var recent = tips.Get(TipShape.Round, Color.FromArgb(255, 9, 199, 9));
            tips.Get(TipShape.Round, Color.FromArgb(255, 1, 1, 1));
            Assert.True(Alive(recent));
        }
    }

    // ---------- 2. Difuminar se ve mientras se arrastra ----------

    [Fact]
    public void SmudgeIsVisibleWhileDragging()
    {
        var (doc, ops, engine, _) = New();
        using (doc)
        {
            ops.Fill(0, Red);
            Stroke(engine, ToolKind.Brush, "roundbrush", Blue, 30, 40);
            using var cache = new CompositionCache();
            var o0 = new List<IDisposable>();
            using (var rt = new CanvasRenderTarget(Gpu.Device, W, H, 96))
            using (var ds = rt.CreateDrawingSession()) ds.DrawImage(cache.Compose(doc, null, o0));
            o0.ForEach(o => o.Dispose());
            var pts = SyntheticStrokes.Sample(SyntheticStrokes.Line(new(100, 10), new(100, 110)), 300, 240);
            engine.Begin(pts[0], ToolKind.Smudge, Lib["roundbrush"], Red, 40, 1, SymmetryMode.None, 1, 1);
            foreach (var p in pts.Skip(1)) engine.Add(p);
            // la vista: lo que hace la app en cada fotograma (vista previa del trazo como sustitución de la capa)
            var owned = new List<IDisposable>();
            var ov = new Dictionary<object, ICanvasImage>();
            if (engine.Preview(owned) is { } s && engine.PreviewKey is { } k) ov[k] = s;
            using var view = new CanvasRenderTarget(Gpu.Device, W, H, 96);
            using (var ds = view.CreateDrawingSession())
            {
                ds.Clear(KnownColors.Transparent);
                ds.DrawImage(cache.Compose(doc, ov, owned));
            }
            owned.ForEach(o => o.Dispose());
            var diff = MaxDiff(view.GetPixelBytes(), Flat(doc));
            engine.End();
            Assert.True(diff <= 1, $"la vista difiere en {diff} niveles a mitad del trazo");
        }
    }

    // ---------- 3. Transformar una máscara con selección deja blanco ----------

    [Fact]
    public void TransformingMaskWithSelectionVacatesToVisible()
    {
        var (doc, ops, _, _) = New();
        using (doc)
        {
            var layer = doc.ActiveLayer;
            ops.Fill(0, Red);
            ops.AddMask(layer);
            ops.SetSelection(SelectionMask.Rectangle(W, H, 20, 20, 80, 80), "s");
            using (var t = TransformSession.Start(doc, layer)!)
            {
                t.Matrix = Matrix3x2.CreateTranslation(100, 0);
                t.Apply(ops);
            }
            var i = (50 * W + 50) * 4;
            var m = layer.Mask!.GetPixelBytes();
            Assert.Equal((255, 255, 255, 255), (m[i], m[i + 1], m[i + 2], m[i + 3]));
            Assert.Equal(255, Flat(doc)[i + 3]);
        }
    }

    // ---------- 4. Quitar un grupo no activa un recorte dormido ----------

    [Theory]
    [InlineData("combinar")]
    [InlineData("desagrupar")]
    [InlineData("eliminar")]
    public void RemovingGroupKeepsDormantClipDormant(string how)
    {
        var (doc, ops, engine, _) = New();
        using (doc)
        {
            Stroke(engine, ToolKind.Brush, "hardair", Red, 20, 30);
            ops.Add();
            Stroke(engine, ToolKind.Brush, "hardair", Red, 20, 60);
            ops.GroupWithBelow(1);
            var groupId = doc.Layers[0].GroupId!;
            // debajo del grupo, una capa suelta: al eliminar el grupo, el recorte se apoyaría en ella
            var under = doc.CreateLayer("debajo");
            using (var ds = under.Surface.CreateDrawingSession()) ds.FillRectangle(0, 0, 40, H, Green);
            doc.Layers.Insert(0, under);
            ops.Add(at: 3);
            doc.Layers[3].GroupId = null;
            ops.Fill(3, Blue);
            ops.Set(doc.Layers[3], "clip", true, l => l.Clip, (l, v) => l.Clip = v, "clip");
            var top = doc.Layers[3];
            var before = Flat(doc);
            switch (how)
            {
                case "combinar": ops.MergeGroup(groupId); break;
                case "desagrupar": ops.Ungroup(groupId); break;
                default: ops.DeleteGroup(groupId); break;
            }
            Assert.False(top.Clip);
            if (how != "eliminar") Assert.True(MaxDiff(before, Flat(doc)) <= 2, $"{how}: la imagen cambia en {MaxDiff(before, Flat(doc))}");
            // deshacer devuelve la marca
            doc.History.Undo();
            Assert.True(top.Clip);
            Assert.True(MaxDiff(before, Flat(doc)) <= 2);
        }
    }

    // ---------- 5. Combinar hacia abajo no cambia la imagen ----------

    [Theory]
    [InlineData("opacidad")]
    [InlineData("cadena de recorte")]
    [InlineData("recorte sobre su base")]
    public void MergeDownKeepsComposite(string kase)
    {
        var (doc, ops, engine, _) = New();
        using (doc)
        {
            doc.Layers[0].Name = "base";
            ops.SetSelection(SelectionMask.Rectangle(W, H, 0, 0, W, H / 2f + 30), "s");
            ops.Fill(0, Red);
            ops.Deselect();
            ops.Add();
            Stroke(engine, ToolKind.Brush, "hardair", Blue, 30, 40);
            ops.Add();
            Stroke(engine, ToolKind.Brush, "hardair", Green, 30, 80);
            int merge;
            switch (kase)
            {
                case "opacidad":
                    doc.Layers[1].Opacity = 0.5f;
                    merge = 2;
                    break;
                case "cadena de recorte":
                    doc.Layers[1].Clip = true;
                    doc.Layers[2].Clip = true;
                    merge = 2;
                    break;
                default:
                    doc.Layers[0].Opacity = 0.6f;
                    doc.Layers[1].Clip = true;
                    merge = 1;
                    break;
            }
            var before = Flat(doc);
            ops.MergeDown(merge);
            Assert.Equal(2, doc.Layers.Count);
            var diff = MaxDiff(before, Flat(doc));
            Assert.True(diff <= 2, $"{kase}: la imagen cambia en {diff} niveles");
            doc.History.Undo();
            Assert.Equal(3, doc.Layers.Count);
            Assert.True(MaxDiff(before, Flat(doc)) <= 2);
        }
    }

    [Fact]
    public void MergeDownRefusesCasesWithoutExactResult()
    {
        var (doc, ops, engine, _) = New();
        using (doc)
        {
            var messages = new List<string>();
            ops.Message += messages.Add;
            ops.Fill(0, Red);
            ops.Add();
            Stroke(engine, ToolKind.Brush, "hardair", Blue, 30, 40);
            ops.Add();
            Stroke(engine, ToolKind.Brush, "hardair", Green, 30, 80);
            // capa normal sobre una de recorte
            doc.Layers[1].Clip = true;
            ops.MergeDown(2);
            Assert.Equal(3, doc.Layers.Count);
            // sobre una capa oculta
            doc.Layers[1].Clip = false;
            doc.Layers[1].Visible = false;
            ops.MergeDown(2);
            Assert.Equal(3, doc.Layers.Count);
            Assert.Equal(2, messages.Count);
        }
    }

    // ---------- 6. Las capas que salen del historial se liberan, y solo esas ----------

    [Fact]
    public void LayersLeavingHistoryAreReleasedOnlyWhenTheyCannotReturn()
    {
        var (doc, ops, _, _) = New();
        using (doc)
        {
            // eliminada y descartada hecha: se libera
            ops.Add();
            var removed = doc.Layers[1];
            ops.Remove(1);
            doc.History.Clear();
            Assert.False(Alive(removed.Surface));

            // eliminada, deshecha (vuelve al documento) y descartada al hacer otra cosa: sigue viva
            ops.Add();
            var back = doc.Layers[1];
            ops.Remove(1);
            doc.History.Undo();
            ops.Fill(0, Red); // descarta el «rehacer eliminar»
            Assert.Contains(back, doc.Layers);
            Assert.True(Alive(back.Surface));

            // añadida, deshecha y descartada: se libera
            var added = ops.Add()!;
            doc.History.Undo();
            ops.Fill(0, Blue);
            Assert.False(Alive(added.Surface));

            // la memoria del historial cuenta las capas eliminadas
            var bytes = doc.History.Bytes;
            ops.Remove(doc.Layers.IndexOf(back));
            Assert.Equal(bytes + (long)W * H * 4, doc.History.Bytes);
        }
    }

    // ---------- 7. El time-lapse respeta el tamaño de su archivo ----------

    [Fact]
    public async Task TimelapseKeepsFileFrameSizeAfterReopen()
    {
        using var tmp = new TempFolder();
        var project = tmp.File("obra.trazo");
        using (var doc = PaintDocument.CreateBlank(Gpu.Device, 200, 200, "tl"))
        {
            using var rec = new TimelapseRecorder(doc, project, () => true, (_, _) => { });
            rec.NotifyChanged();
            rec.Flush();
            await rec.ReleaseAsync();
        }
        using (var doc = PaintDocument.CreateBlank(Gpu.Device, 400, 200, "tl"))
        {
            var ops = new LayerOperations(doc);
            using var rec = new TimelapseRecorder(doc, project, () => true, (_, _) => { });
            ops.Fill(0, Blue);
            rec.NotifyChanged();
            rec.Flush();
            await rec.ReleaseAsync(); // como al abrir el reproductor
            ops.Fill(0, Red);
            rec.NotifyChanged();
            rec.Flush();
            await rec.ReleaseAsync();
        }
        using var reader = TimelapseReader.Open(TimelapseFile.SidecarPath(project));
        Assert.Equal(3, reader.Frames.Count);
        for (var i = 0; i < reader.Frames.Count; i++)
        {
            using var ms = new MemoryStream(reader.ReadFrame(i));
            using var bmp = await CanvasBitmap.LoadAsync(Gpu.Device, ms.AsRandomAccessStream());
            Assert.Equal((200u, 200u), (bmp.SizeInPixels.Width, bmp.SizeInPixels.Height));
            if (i == 0) continue;
            // la obra de 400 × 200 va centrada con bandas del color de fondo (blanco) arriba y abajo
            var band = bmp.GetPixelBytes(100, 10, 1, 1);
            var art = bmp.GetPixelBytes(100, 100, 1, 1);
            Assert.True(band[0] > 230 && band[1] > 230 && band[2] > 230, $"fotograma {i}: banda {string.Join(",", band)}");
            Assert.True(i == 1 ? art[0] > 180 && art[2] < 80 : art[2] > 180 && art[0] < 80, $"fotograma {i}: obra {string.Join(",", art)}");
        }
    }

    // ---------- 8. Transformar con selección es un solo paso ----------

    [Fact]
    public void TransformWithSelectionIsOneUndoStep()
    {
        var (doc, ops, _, _) = New();
        using (doc)
        {
            ops.Fill(0, Red);
            ops.SetSelection(SelectionMask.Rectangle(W, H, 20, 20, 80, 80), "s");
            var before = doc.ActiveLayer.Surface.GetPixelBytes();
            var selBefore = doc.Selection!.Bounds;
            var n = doc.History.UndoCount;
            using (var t = TransformSession.Start(doc, doc.ActiveLayer)!)
            {
                t.Matrix = Matrix3x2.CreateTranslation(60, 0);
                t.Apply(ops);
            }
            Assert.Equal(n + 1, doc.History.UndoCount);
            Assert.NotEqual(selBefore, doc.Selection!.Bounds);
            doc.History.Undo();
            // un deshacer devuelve a la vez los píxeles y la selección
            Assert.Equal(selBefore, doc.Selection!.Bounds);
            Assert.Equal(before, doc.ActiveLayer.Surface.GetPixelBytes());
            doc.History.Redo();
            Assert.NotEqual(selBefore, doc.Selection!.Bounds);
        }
    }

    // ---------- 9. Deshacer combinar devuelve la edición de la máscara ----------

    [Fact]
    public void UndoMergeDownRestoresMaskEditing()
    {
        var (doc, ops, _, _) = New();
        using (doc)
        {
            ops.Fill(0, Red);
            ops.AddMask(doc.Layers[0]);
            ops.Add();
            Assert.True(doc.Layers[0].EditingMask);
            ops.MergeDown(1);
            doc.History.Undo();
            Assert.True(doc.Layers[0].HasMask);
            Assert.True(doc.Layers[0].EditingMask);
        }
    }

    // ---------- 10. Un fotograma dañado no tumba la app al exportar ----------

    [Fact]
    public async Task CorruptFrameFailsExportCleanly()
    {
        using var tmp = new TempFolder();
        var path = tmp.File("a.timelapse");
        using (var w = TimelapseWriter.Open(path, 64, 64))
        using (var rt = new CanvasRenderTarget(Gpu.Device, 64, 64, 96))
        {
            for (var i = 0; i < 5; i++) w.Append(await TimelapseRecorder.EncodeJpegAsync(Gpu.Device, rt.GetPixelBytes(), 64, 64));
            w.Append([0xFF, 0xD8, 1, 2, 3, 4, 5, 6, 7, 8]); // «JPEG» que no se puede decodificar
        }
        using var reader = TimelapseReader.Open(path);
        await using var fs = File.Create(tmp.File("v.mp4"));
        // antes: la excepción salía de un manejador async void y terminaba el proceso de pruebas
        await Assert.ThrowsAnyAsync<Exception>(() => TimelapseVideo.ExportAsync(Gpu.Device, reader, TimelapseSchedule.Full(6), fs));
    }
}
