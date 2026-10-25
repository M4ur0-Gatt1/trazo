using System.Numerics;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Effects;
using Trazo.Core.Brushes;
using Trazo.Core.Documents;
using Trazo.Core.Geometry;
using Trazo.Core.Imaging;
using Trazo.Core.IO;
using Trazo.Core.Strokes;
using Trazo.Rendering;
using Windows.UI;

namespace Trazo.Rendering.Tests;

/// <summary>
/// PF-01: las cachés que aceleran el autoguardado y la vista no pueden cambiar ni un byte de lo guardado ni de lo
/// que se ve (salvo el redondeo a 8 bits de la parte fija de la vista).
/// </summary>
public class PF01_CacheTests
{
    private const int W = BR02_BrushReferenceTests.Width, H = BR02_BrushReferenceTests.Height;
    private static readonly BrushLibrary Lib = new();

    private static void Stroke(BrushEngine engine, float y, Color color, string brush = "roundbrush", float size = 30)
    {
        engine.Begin(new InputPoint(20, y), ToolKind.Brush, Lib[brush], color, size, 1, SymmetryMode.None, 1, seed: (int)y);
        for (var k = 1; k <= 25; k++) engine.Add(new InputPoint(20 + k * 13, y + MathF.Sin(k) * 10));
        engine.End();
    }

    private static Color C(int r, int g, int b) => Color.FromArgb(255, (byte)r, (byte)g, (byte)b);

    // ---------- Autoguardado incremental ----------

    private static async Task AssertSameAsFullEncode(PaintDocument doc, string step)
    {
        var data = await doc.ToProjectDataAsync();
        for (var i = 0; i < doc.Layers.Count; i++)
        {
            var l = doc.Layers[i];
            var fresh = await PaintDocument.EncodePngAsync(l.Surface);
            Assert.True(data.Layers[i].Png.SequenceEqual(fresh), $"{step}: capa {i} desactualizada en la caché");
            if (l.Mask is null) Assert.Null(data.Layers[i].MaskPng);
            else
            {
                var freshMask = await PaintDocument.EncodePngAsync(l.Mask);
                Assert.True(data.Layers[i].MaskPng!.SequenceEqual(freshMask), $"{step}: máscara {i} desactualizada");
            }
        }
    }

    [Fact]
    public async Task IncrementalSaveMatchesFullEncode()
    {
        var (doc, ops, engine) = BR02_BrushReferenceTests.NewDocument();
        using (doc)
        {
            Stroke(engine, 40, C(200, 30, 30));
            ops.Add();
            Stroke(engine, 80, C(30, 160, 60));
            ops.Add();
            Stroke(engine, 120, C(40, 60, 200));
            await AssertSameAsFullEncode(doc, "inicio");

            var steps = new (string Name, Action Do)[]
            {
                ("pintar", () => Stroke(engine, 100, C(250, 200, 0))),
                ("rellenar", () => ops.Fill(1, C(10, 10, 10))),
                ("invertir", () => ops.Invert(0)),
                ("borrar", () => ops.Clear(2)),
                ("deshacer", () => doc.History.Undo()),
                ("rehacer", () => doc.History.Redo()),
                ("deshacer otra vez", () => doc.History.Undo()),
                ("máscara", () => ops.AddMask(doc.Layers[1])),
                ("pintar en la máscara", () => { doc.ActiveIndex = 1; Stroke(engine, 60, C(0, 0, 0), "softair", 80); }),
                ("invertir la máscara", () => ops.InvertMask(doc.Layers[1])),
                ("aplicar la máscara", () => ops.ApplyMask(doc.Layers[1])),
                ("otra máscara y quitarla", () => { ops.AddMask(doc.Layers[0]); ops.DeleteMask(doc.Layers[0]); }),
                ("selección y borrar dentro", () => { ops.SetSelection(SelectionMaskHelpers.Box(W, H), "sel"); ops.Clear(0); ops.Deselect(); }),
                ("voltear el lienzo", () => ops.FlipCanvas(true)),
                ("duplicar", () => ops.Duplicate(1)),
                ("mover", () => ops.Move(3, 0)),
                ("combinar hacia abajo", () => ops.MergeDown(1)),
                ("transformar", () =>
                {
                    doc.ActiveIndex = 0;
                    using var t = TransformSession.Start(doc, doc.ActiveLayer)!;
                    t.SetMode(TransformMode.Distort);
                    var pts = t.State.HandlePoints();
                    t.State.MovePoint(0, pts[0] + new Vector2(20, 10));
                    t.Apply(ops);
                }),
                ("licuar", () =>
                {
                    using var lq = LiquifySession.Start(doc, doc.ActiveLayer);
                    lq.Mode = LiquifyMode.TwirlRight;
                    lq.Size = 120;
                    lq.DabAt(new Vector2(150, 80), 1);
                    lq.Apply(ops);
                }),
                ("agrupar (sin cambiar píxeles)", () => ops.GroupWithBelow(1)),
            };
            foreach (var (name, act) in steps)
            {
                act();
                await AssertSameAsFullEncode(doc, name);
            }

            // solo se vuelve a codificar lo que cambió
            var before = doc.PngEncodes;
            await doc.ToProjectDataAsync();
            Assert.Equal(before, doc.PngEncodes);
            doc.ActiveIndex = doc.Layers.Count - 1;
            Stroke(engine, 30, C(90, 0, 90));
            await doc.ToProjectDataAsync();
            Assert.Equal(before + 1, doc.PngEncodes);
        }
    }

    [Fact]
    public async Task OpenedFileReusesItsPngs()
    {
        using var tmp = new TempFolder();
        var file = tmp.File("a.trazo");
        var (doc, ops, engine) = BR02_BrushReferenceTests.NewDocument();
        using (doc)
        {
            Stroke(engine, 50, C(200, 30, 30));
            ops.Add();
            Stroke(engine, 100, C(30, 30, 200));
            ops.AddMask(doc.Layers[1]);
            await ProjectSaver.SaveAsync(file, await doc.ToProjectDataAsync());
        }
        var original = ProjectSaver.Load(file).Project;
        using var reopened = await PaintDocument.FromProjectDataAsync(Gpu.Device, original, file);
        var data = await reopened.ToProjectDataAsync();
        Assert.Equal(0, reopened.PngEncodes);                         // nada cambió: se guardan los PNG del archivo
        Assert.Equal(original.Layers[0].Png, data.Layers[0].Png);
        Assert.Equal(original.Layers[1].MaskPng, data.Layers[1].MaskPng);
        var ops2 = new LayerOperations(reopened);
        ops2.Invert(0);
        var after = await reopened.ToProjectDataAsync();
        Assert.Equal(1, reopened.PngEncodes);
        Assert.Equal(original.Layers[1].Png, after.Layers[1].Png);
        var fresh = await PaintDocument.EncodePngAsync(reopened.Layers[0].Surface);
        Assert.True(after.Layers[0].Png.SequenceEqual(fresh));
    }

    // ---------- Caché de composición de la vista ----------

    private static byte[] Render(PaintDocument doc, Func<List<IDisposable>, ICanvasImage> image)
    {
        var owned = new List<IDisposable>();
        using var rt = new CanvasRenderTarget(doc.Device, W, H, 96);
        using (var ds = rt.CreateDrawingSession())
        {
            ds.Clear(Color.FromArgb(0, 0, 0, 0));
            ds.DrawImage(image(owned));
        }
        owned.ForEach(o => o.Dispose());
        return rt.GetPixelBytes();
    }

    private static int MaxDiff(byte[] a, byte[] b) => Enumerable.Range(0, a.Length).Max(i => Math.Abs(a[i] - b[i]));

    /// <summary>Documento aleatorio: capas con trazos, opacidades, modos, recortes, visibilidad, máscaras y grupos.</summary>
    private static (PaintDocument, LayerOperations, BrushEngine) RandomDocument(Random rng)
    {
        var (doc, ops, engine) = BR02_BrushReferenceTests.NewDocument();
        doc.BackgroundVisible = rng.Next(2) == 0;
        var n = rng.Next(2, 8);
        var modes = Enum.GetValues<BlendMode>();
        for (var i = 0; i < n; i++)
        {
            if (i > 0) ops.Add();
            Stroke(engine, rng.Next(20, H - 20), C(rng.Next(256), rng.Next(256), rng.Next(256)), rng.Next(2) == 0 ? "roundbrush" : "gouache", rng.Next(20, 70));
            var l = doc.ActiveLayer;
            if (rng.Next(3) == 0) l.Opacity = (float)rng.NextDouble();
            if (rng.Next(2) == 0) l.Blend = modes[rng.Next(modes.Length)];
            if (i > 0 && rng.Next(4) == 0) l.Clip = true;
            if (rng.Next(6) == 0) l.Visible = false;
            if (rng.Next(4) == 0)
            {
                ops.AddMask(l);
                Stroke(engine, rng.Next(20, H - 20), C(0, 0, 0), "softair", 90);
                l.EditingMask = false;
            }
        }
        // grupos contiguos
        for (var tries = 0; tries < 2; tries++)
        {
            var at = rng.Next(1, doc.Layers.Count);
            if (doc.Layers[at].GroupId is null && rng.Next(2) == 0)
            {
                ops.GroupWithBelow(at);
                if (doc.Group(doc.Layers[at].GroupId) is { } g)
                {
                    g.Opacity = rng.Next(2) == 0 ? 1 : (float)rng.NextDouble();
                    g.Blend = rng.Next(2) == 0 ? BlendMode.Normal : modes[rng.Next(modes.Length)];
                }
            }
        }
        doc.ActiveIndex = rng.Next(doc.Layers.Count);
        return (doc, ops, engine);
    }

    [Fact]
    public void CompositionCacheMatchesFullCompose()
    {
        var rng = new Random(2026);
        var worst = 0;
        for (var c = 0; c < 25; c++)
        {
            var (doc, ops, engine) = RandomDocument(rng);
            using (doc)
            using (var cache = new CompositionCache())
            {
                // sin vista previa, y con una capa (o su máscara) sustituida por una imagen distinta
                var target = doc.Layers[rng.Next(doc.Layers.Count)];
                object key = target.Mask is not null && rng.Next(2) == 0 ? new MaskKey(target) : target;
                foreach (var withOverride in new[] { false, true, true })
                {
                    var overrides = new Dictionary<object, ICanvasImage>();
                    var owned = new List<IDisposable>();
                    if (withOverride)
                    {
                        var replacement = new HueRotationEffect { Source = key is MaskKey ? target.Mask : target.Surface, Angle = 2 };
                        owned.Add(replacement);
                        overrides[key] = replacement;
                    }
                    var full = Render(doc, o => doc.Compose(overrides, o));
                    var cached = Render(doc, o => cache.Compose(doc, overrides, o));
                    owned.ForEach(o => o.Dispose());
                    var d = MaxDiff(full, cached);
                    worst = Math.Max(worst, d);
                    Assert.True(d <= 1, $"caso {c}: diferencia {d}");
                }
            }
        }
        Assert.True(worst <= 1);
    }

    [Fact]
    public void CompositionCacheReusesFixedPartAndInvalidates()
    {
        var (doc, ops, engine) = BR02_BrushReferenceTests.NewDocument();
        using (doc)
        using (var cache = new CompositionCache())
        {
            for (var i = 0; i < 6; i++)
            {
                if (i > 0) ops.Add();
                Stroke(engine, 20 + i * 22, C(40 * i, 200 - 30 * i, 120));
            }
            var top = doc.ActiveLayer;
            void Frame(Dictionary<object, ICanvasImage>? overrides = null) => Render(doc, o => cache.Compose(doc, overrides, o));

            // pintando la capa de arriba: la parte fija (las 5 de debajo) se compone una sola vez
            engine.Begin(new InputPoint(30, 140), ToolKind.Brush, Lib["roundbrush"], C(0, 0, 0), 20, 1, SymmetryMode.None, 1, seed: 1);
            for (var k = 1; k <= 10; k++)
            {
                engine.Add(new InputPoint(30 + k * 20, 140));
                var owned = new List<IDisposable>();
                var overrides = new Dictionary<object, ICanvasImage>();
                if (engine.Preview(owned) is { } s && engine.PreviewKey is { } pk) overrides[pk] = s;
                Frame(overrides);
                owned.ForEach(o => o.Dispose());
            }
            engine.End();
            Assert.Equal(1, cache.Rebuilds);

            // sin vista previa se guarda la imagen entera; mientras nada cambie, no se recompone
            Frame();
            Frame();
            Assert.Equal(2, cache.Rebuilds);

            // cualquier cambio visible invalida, y el resultado sigue igual que sin caché
            var changes = new (string, Action)[]
            {
                ("pintar una capa", () => { doc.ActiveIndex = 1; Stroke(engine, 60, C(255, 0, 0)); }),
                ("visibilidad", () => doc.Layers[2].Visible = false),
                ("opacidad", () => doc.Layers[0].Opacity = 0.4f),
                ("modo", () => doc.Layers[3].Blend = BlendMode.Screen),
                ("recorte", () => doc.Layers[4].Clip = true),
                ("máscara", () => ops.AddMask(doc.Layers[1])),
                ("grupo", () => ops.GroupWithBelow(3)),
                ("opacidad del grupo", () => doc.Group(doc.Layers[3].GroupId)!.Opacity = 0.5f),
                ("orden", () => ops.Move(0, 5)),
                ("fondo", () => doc.Background = C(10, 20, 30)),
                ("deshacer", () => doc.History.Undo()),
            };
            foreach (var (name, change) in changes)
            {
                var rebuilds = cache.Rebuilds;
                change();
                var cached = Render(doc, o => cache.Compose(doc, null, o));
                Assert.True(cache.Rebuilds == rebuilds + 1, $"«{name}» no invalidó la caché");
                Assert.True(MaxDiff(Render(doc, o => doc.Compose(null, o)), cached) <= 1, $"«{name}»: imagen distinta");
            }
            _ = top;
        }
    }
}

internal static class SelectionMaskHelpers
{
    public static Trazo.Core.Selection.SelectionMask Box(int w, int h) => Trazo.Core.Selection.SelectionMask.Rectangle(w, h, w * 0.3f, h * 0.2f, w * 0.7f, h * 0.8f);
}
