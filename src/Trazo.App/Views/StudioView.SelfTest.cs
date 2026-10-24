using System.Text;
using Microsoft.Graphics.Canvas;
using Microsoft.UI;
using Trazo.App.Painting;
using Trazo.Core.IO;
using Trazo.Core.Strokes;
using Windows.UI;

namespace Trazo.App.Views;

/// <summary>
/// Autoprueba de extremo a extremo (Trazo.exe --selftest carpeta): pinta con el motor
/// real en la GPU, aplica operaciones y escribe un informe y capturas en la carpeta.
/// </summary>
public sealed partial class StudioView
{
    public async Task<bool> RunSelfTestAsync(string outDir)
    {
        Directory.CreateDirectory(outDir);
        var report = new StringBuilder();
        var failures = 0;
        void Check(string name, bool ok)
        {
            report.AppendLine($"{(ok ? "OK  " : "FAIL")} {name}");
            if (!ok) failures++;
        }

        try
        {
            var doc = _doc!;
            var ops = _ops!;
            var engine = _engine!;
            Color Pixel(Layer l, int x, int y)
            {
                var p = l.Surface.GetPixelBytes(x, y, 1, 1);
                return Color.FromArgb(p[3], p[2], p[1], p[0]);
            }
            void Stroke(ToolKind tool, string brush, float size, float opacity, Color color, IEnumerable<InputPoint> pts, SymmetryMode sym = SymmetryMode.None)
            {
                var list = pts.ToList();
                engine.Begin(list[0], tool, _brushes[brush], color, size, opacity, sym, _view.Scale);
                foreach (var p in list.Skip(1)) engine.Add(p);
                engine.End();
            }
            static IEnumerable<InputPoint> Line(float x0, float y0, float x1, float y1, int n = 60, float p = 0.8f) =>
                Enumerable.Range(0, n + 1).Select(i => new InputPoint(x0 + (x1 - x0) * i / n, y0 + (y1 - y0) * i / n, p));

            // 1. Todos los pinceles pintan
            var red = Color.FromArgb(255, 220, 30, 40);
            foreach (var b in _brushes.All)
            {
                var l = ops.Add()!;
                Stroke(ToolKind.Brush, b.Id, b.Size, 1, red, Line(100, 300, 900, 300));
                var bounds = Trazo.Core.Imaging.FloodFill.ContentBounds(l.Surface.GetPixelBytes(), doc.Width, doc.Height);
                Check($"pincel {b.Id} pinta", !bounds.IsEmpty);
                ops.Remove(doc.Layers.IndexOf(l));
            }

            // 2. Pintar, borrar, deshacer
            var layer = ops.Add()!;
            Stroke(ToolKind.Brush, "monoline", 30, 1, red, Line(100, 500, 900, 500));
            Check("monolínea opaca", Pixel(layer, 500, 500).A > 240 && Pixel(layer, 500, 500).R > 200);
            for (var i = 0; i < 3; i++) Stroke(ToolKind.Eraser, "hardair", 60, 1, Colors.Black, Line(400, 500, 600, 500));
            Check("borrador quita la pintura", Pixel(layer, 500, 500).A < 40);
            for (var i = 0; i < 3; i++) doc.History.Undo();
            Check("deshacer restaura", Pixel(layer, 500, 500).A > 240);
            doc.History.Redo();

            // 3. Difuminar
            Stroke(ToolKind.Smudge, "roundbrush", 40, 0.8f, Colors.Black, Line(200, 500, 200, 640, 30, 1));
            Check("difuminar arrastra pintura", Pixel(layer, 200, 560).A > 0);

            // 4. Relleno dentro de un rectángulo cerrado
            var frame = ops.Add()!;
            var mono = _brushes["monoline"];
            var keep = mono.StreamLine;
            mono.StreamLine = 0;
            Stroke(ToolKind.Brush, "monoline", 6, 1, Colors.Black,
                Line(1000, 100, 1100, 100, 20, 1).Concat(Line(1100, 100, 1100, 200, 20, 1)).Concat(Line(1100, 200, 1000, 200, 20, 1)).Concat(Line(1000, 200, 1000, 100, 20, 1)));
            mono.StreamLine = keep;
            var fillLayer = ops.Add()!;
            engine.Fill(1050, 150, Color.FromArgb(255, 255, 204, 0), _settings.FillTolerance);
            Check("relleno dentro del marco", Pixel(fillLayer, 1050, 150).G > 190);
            Check("relleno no se escapa", Pixel(fillLayer, 1050, 60).A == 0);

            // 5. Simetría vertical
            var symLayer = ops.Add()!;
            Stroke(ToolKind.Brush, "monoline", 10, 1, red, Line(100, 700, 300, 700, 10), SymmetryMode.Vertical);
            Check("simetría refleja", Pixel(symLayer, doc.Width - 200, 700).A > 0);

            // 6. QuickShape
            var shapeLayer = ops.Add()!;
            engine.Begin(new InputPoint(100, 100), ToolKind.Brush, mono, Colors.Blue, 8, 1, SymmetryMode.None, 1);
            foreach (var p in Line(100, 100, 700, 130, 30).Skip(1)) engine.Add(p with { Y = p.Y + MathF.Sin(p.X) * 4 });
            Check("QuickShape reconoce línea", engine.TrySnapShape(1) && engine.Shape?.Kind == ShapeKind.Line);
            engine.End();
            Check("QuickShape dibuja", Pixel(shapeLayer, 400, 115).A > 0);

            // 7. Modos de fusión, opacidad, recorte, combinar
            ops.Set(fillLayer, "blend", Core.Documents.BlendMode.Multiply, l => l.Blend, (l, v) => l.Blend = v, "fusión");
            ops.Set(fillLayer, "opacity", 0.5f, l => l.Opacity, (l, v) => l.Opacity = v, "opacidad");
            ops.Set(fillLayer, "clip", true, l => l.Clip, (l, v) => l.Clip = v, "recorte");
            using (var flat = doc.Flatten()) Check("composición con recorte y multiplicar", flat.GetPixelBytes(1050, 150, 1, 1)[3] == 255);
            var count = doc.Layers.Count;
            ops.MergeDown(doc.Layers.IndexOf(fillLayer));
            Check("combinar hacia abajo", doc.Layers.Count == count - 1);
            doc.History.Undo();
            Check("deshacer combinar", doc.Layers.Count == count);

            // 8. Ajustes y transformar
            _adjust = new AdjustmentSession(doc, layer, Adjustment.All.First(a => a.Id == "invert"));
            _adjust.Apply(ops);
            _adjust = null;
            Check("ajuste invertir", Pixel(layer, 300, 500).G > 150);
            var tr = TransformSession.Start(doc, symLayer)!;
            tr.Flip(horizontal: true);
            tr.Apply(ops);
            tr.Dispose();
            Check("transformar voltea", Pixel(symLayer, 200, 700).A > 0);
            ops.FlipCanvas(true);
            doc.History.Undo();

            // 9. Cuentagotas
            Check("cuentagotas", doc.SampleColor(1050, 150, null) is not null);

            // 10. Guardar y volver a abrir
            Canvas.Invalidate();
            doc.IsDirty = true;
            await SaveAsync();
            Check("archivo guardado", doc.FilePath is not null && File.Exists(doc.FilePath));
            ProjectData data;
            await using (var fs = File.OpenRead(doc.FilePath!)) data = TrazoFile.Read(fs);
            using var reopened = await PaintDocument.FromProjectDataAsync(doc.Device, data, doc.FilePath);
            Check("se reabre con las mismas capas", reopened.Layers.Count == doc.Layers.Count && reopened.Width == doc.Width);
            using (var a = doc.Flatten())
            using (var b = reopened.Flatten())
                Check("misma imagen tras reabrir", a.GetPixelBytes(1050, 150, 1, 1).SequenceEqual(b.GetPixelBytes(1050, 150, 1, 1)));

            using (var final = doc.Flatten())
                await File.WriteAllBytesAsync(Path.Combine(outDir, "selftest-render.png"), await PaintDocument.EncodePngAsync(final));
        }
        catch (Exception e)
        {
            report.AppendLine("ERROR " + e);
            failures++;
        }
        report.AppendLine(failures == 0 ? "RESULTADO: todo correcto" : $"RESULTADO: {failures} fallos");
        await File.WriteAllTextAsync(Path.Combine(outDir, "selftest.txt"), report.ToString());
        return failures == 0;
    }
}
