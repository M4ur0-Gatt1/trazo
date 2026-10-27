using Trazo.Core.Imaging;
using Trazo.Rendering;
using Windows.UI;

namespace Trazo.Rendering.Tests;

/// <summary>LY-04 — Capa de referencia con el motor real (WARP).</summary>
public class LY04_ReferenceRenderTests
{
    private const int W = 200, H = 200;

    /// <summary>Línea: círculo negro cerrado. Mancha: rectángulo rojo dentro del círculo, en otra capa. Color: vacía y activa.</summary>
    private static (PaintDocument Doc, LayerOperations Ops, BrushEngine Engine, Layer Line, Layer Blob, Layer Paint) Scene()
    {
        var doc = PaintDocument.CreateBlank(Gpu.Device, W, H, "LY-04");
        var line = doc.Layers[0];
        line.Name = "Línea";
        using (var ds = line.Surface.CreateDrawingSession()) ds.DrawCircle(100, 100, 70, Color.FromArgb(255, 0, 0, 0), 4);
        var blob = doc.CreateLayer("Mancha");
        using (var ds = blob.Surface.CreateDrawingSession()) ds.FillRectangle(80, 80, 40, 40, Color.FromArgb(255, 220, 30, 30));
        doc.Layers.Add(blob);
        var paint = doc.CreateLayer("Color");
        doc.Layers.Insert(0, paint); // debajo de la línea, como se colorea
        doc.ActiveIndex = 0;
        var ops = new LayerOperations(doc);
        return (doc, ops, new BrushEngine(doc, ops, new TipCache(Gpu.Device)), line, blob, paint);
    }

    private static byte A(Layer l, int x, int y) => l.Surface.GetPixelBytes(x, y, 1, 1)[3];

    // ---------- LY-04.2 ----------

    [Fact]
    public async Task OnlyOneReferenceAndUndo()
    {
        var (doc, ops, _, line, blob, _) = Scene();
        using (doc)
        {
            Assert.Null(doc.ReferenceLayer);
            ops.SetReference(line);
            Assert.Same(line, doc.ReferenceLayer);
            ops.SetReference(blob);
            Assert.Equal((false, true), (line.Reference, blob.Reference));
            Assert.Single(doc.Layers, l => l.Reference);
            doc.History.Undo();
            Assert.Same(line, doc.ReferenceLayer);
            doc.History.Undo();
            Assert.Null(doc.ReferenceLayer);
            doc.History.Redo();
            Assert.Same(line, doc.ReferenceLayer);

            // eliminar la capa de referencia deja el documento sin referencia; deshacer la recupera
            ops.Remove(doc.Layers.IndexOf(line));
            Assert.Null(doc.ReferenceLayer);
            doc.History.Undo();
            Assert.Same(line, doc.ReferenceLayer);

            // duplicarla no crea una segunda referencia
            ops.Duplicate(doc.Layers.IndexOf(line));
            Assert.Single(doc.Layers, l => l.Reference);

            // y la marca sobrevive a guardar y reabrir
            var data = await doc.ToProjectDataAsync();
            Assert.Single(data.Layers, l => l.Properties.Reference);
            using var reopened = await PaintDocument.FromProjectDataAsync(Gpu.Device, data, null);
            Assert.Equal("Línea", reopened.ReferenceLayer?.Name);
        }
    }

    // ---------- LY-04.3 ----------

    [Fact]
    public void FillUsesReferenceLayer()
    {
        var black = Color.FromArgb(255, 30, 120, 220);
        // sin referencia: la mancha roja frena el relleno y deja un hueco
        var (doc, ops, engine, line, _, paint) = Scene();
        using (doc)
        {
            Assert.True(engine.Fill(50, 100, black, 46));
            Assert.Equal(255, A(paint, 50, 100));
            Assert.Equal(0, A(paint, 100, 100)); // dentro de la mancha
            Assert.Equal(0, A(paint, 100, 15));  // fuera del círculo
        }
        // con la línea como referencia: el interior entero, mancha incluida, y nada fuera
        (doc, ops, engine, line, _, paint) = Scene();
        using (doc)
        {
            ops.SetReference(line);
            Assert.True(engine.Fill(50, 100, black, 46));
            Assert.Equal(255, A(paint, 50, 100));
            Assert.Equal(255, A(paint, 100, 100));
            Assert.Equal(0, A(paint, 100, 15));
            Assert.Equal(0, A(paint, 3, 3));
            // el relleno se pinta en la capa activa, no en la referencia
            Assert.Equal(0, A(line, 100, 100));
            doc.History.Undo();
            Assert.Equal(0, A(paint, 50, 100));
        }
    }

    // ---------- LY-04.4 ----------

    [Fact]
    public void HiddenReferenceAndAutoSelection()
    {
        var (doc, ops, engine, line, _, paint) = Scene();
        using (doc)
        {
            ops.SetReference(line);
            line.Visible = false; // se colorea sin ver la línea
            Assert.True(engine.Fill(100, 100, Color.FromArgb(255, 0, 200, 0), 46));
            Assert.Equal(255, A(paint, 100, 100));
            Assert.Equal(0, A(paint, 100, 15)); // la línea oculta sigue delimitando

            // la selección automática sale de la misma fuente
            Assert.Equal(line.Surface.GetPixelBytes(), doc.FillSource());
            var mask = FloodFill.Compute(doc.FillSource(), W, H, 100, 100, 46);
            Assert.NotNull(mask);
            Assert.Equal(255, mask.Mask[100 * W + 100]); // la máscara mide lo que el lienzo
            Assert.True(mask.Bounds.X > 20 && mask.Bounds.X + mask.Bounds.Width < 180, $"la selección no sale del círculo: {mask.Bounds}");

            // sin referencia vuelve a la imagen aplanada
            ops.SetReference(null);
            using var flat = doc.Flatten();
            Assert.Equal(flat.GetPixelBytes(), doc.FillSource());
        }
    }
}
