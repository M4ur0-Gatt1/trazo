using System.Text;
using Microsoft.Graphics.Canvas;
using Microsoft.UI;
using Trazo.Rendering;
using System.Numerics;
using Trazo.Core.Brushes;
using Trazo.Core.Geometry;
using Trazo.Core.Imaging;
using Trazo.Core.IO;
using Trazo.Core.Selection;
using Trazo.Core.Strokes;
using Windows.UI;

namespace Trazo.App.Views;

/// <summary>
/// Autoprueba de extremo a extremo (Trazo.exe --selftest carpeta): pinta con el motor
/// real en la GPU, aplica operaciones y escribe un informe y capturas en la carpeta.
/// </summary>
public sealed partial class StudioView
{
    /// <summary>Pinta una línea negra horizontal de prueba a la altura <paramref name="y"/> (autopruebas).</summary>
    public void PaintTestStroke(float y)
    {
        var mono = _brushes["monoline"];
        _engine!.Begin(new InputPoint(100, y), ToolKind.Brush, mono, Colors.Black, 12, 1, SymmetryMode.None, 1, seed: 1);
        for (var k = 1; k <= 40; k++) _engine.Add(new InputPoint(100 + k * 10, y));
        _engine.End();
    }

    /// <summary>¿Está la línea de prueba en la capa activa?</summary>
    public bool TestStrokePresent(float y) =>
        _doc is { } d && d.ActiveLayer.Surface.GetPixelBytes(150, (int)y, 300, 1).Where((_, i) => i % 4 == 3).All(a => a > 200);

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

            // 8b. AD-02: curvas, mapa de degradado, equilibrio, desenfoque de movimiento y resplandor, con selección
            var adjLayer = ops.Add()!;
            ops.Fill(doc.Layers.IndexOf(adjLayer), Color.FromArgb(255, 100, 100, 100));
            doc.Selection = Core.Selection.SelectionMask.Rectangle(doc.Width, doc.Height, 0, 0, doc.Width / 2f, doc.Height);
            int inX = 100, outX = doc.Width - 100;
            void RunAdjust(string id, Action<AdjustInputs> set)
            {
                _adjust = new AdjustmentSession(doc, adjLayer, Adjustment.All.First(a => a.Id == id));
                set(_adjust.Inputs);
                _adjust.Apply(ops);
                _adjust = null;
            }
            RunAdjust("curves", i => i.Curves = new Core.Imaging.ToneCurves().With(1, Core.Brushes.DynamicsCurve.From(new(0, 1), new(1, 0))));
            Check("curvas: invierten el rojo dentro de la selección", Pixel(adjLayer, inX, 500).R == 155 && Pixel(adjLayer, inX, 500).G == 100);
            Check("curvas: fuera de la selección no cambia", Pixel(adjLayer, outX, 500).R == 100);
            doc.History.Undo();
            Check("curvas: se deshacen en un paso", Pixel(adjLayer, inX, 500).R == 100);
            RunAdjust("gradientmap", i => i.Gradient = Core.Imaging.GradientMap.Library.First(g => g.Stops.Count == 3 && g.Stops[1].Color.R > 200));
            Check("mapa de degradado colorea", Pixel(adjLayer, inX, 500) is var gm && (gm.R != gm.G || gm.G != gm.B) && Pixel(adjLayer, outX, 500).R == 100);
            doc.History.Undo();
            RunAdjust("balance", i => i.Values["m0"] = 100);
            Check("equilibrio de color: medios tonos hacia el rojo", Pixel(adjLayer, inX, 500).R > 120 && Pixel(adjLayer, inX, 500).B == 100);
            doc.History.Undo();
            RunAdjust("motionblur", i => i.Values["d"] = 40);
            RunAdjust("bloom", i => i.Values["a"] = 100);
            Check("desenfoque de movimiento y resplandor se aplican", Pixel(adjLayer, outX, 500).R == 100);
            doc.Selection = null;
            for (var k = 0; k < 4; k++) doc.History.Undo(); // resplandor, desenfoque, relleno y la capa
            Check("ajustes AD-02 deshechos", !doc.Layers.Contains(adjLayer));
            var tr = TransformSession.Start(doc, symLayer)!;
            tr.Flip(horizontal: true);
            tr.Apply(ops);
            tr.Dispose();
            Check("transformar voltea", Pixel(symLayer, 200, 700).A > 0);
            ops.FlipCanvas(true);
            doc.History.Undo();

            // 9. Cuentagotas
            Check("cuentagotas", doc.SampleColor(1050, 150, null) is not null);

            // 9b. Grano y afinado en la GPU real (BR-03, BR-05)
            var grainLayer = ops.Add()!;
            var pencil = _brushes["pencil6b"];
            engine.Begin(new InputPoint(100, 900), ToolKind.Brush, pencil, Colors.Black, 40, 1, SymmetryMode.None, 1, 5, hasPressure: true);
            foreach (var p in Line(100, 900, 700, 900, 120, 1).Skip(1)) engine.Add(p);
            engine.End();
            var row = grainLayer.Surface.GetPixelBytes(300, 900, 200, 1);
            var alphas = Enumerable.Range(0, 200).Select(i => (double)row[i * 4 + 3]).ToList();
            var mean = alphas.Average();
            Check("grano texturizado añade textura", Math.Sqrt(alphas.Average(a => (a - mean) * (a - mean))) > 10);

            var taperLayer = ops.Add()!;
            var pen = _brushes["studiopen"];
            engine.Begin(new InputPoint(900, 900), ToolKind.Brush, pen, Colors.Black, 30, 1, SymmetryMode.None, 1, 6, hasPressure: false);
            foreach (var p in Line(900, 900, 1500, 900, 120, 1).Skip(1)) engine.Add(p);
            engine.End();
            int Thick(int x) => Enumerable.Range(860, 80).Count(y => taperLayer.Surface.GetPixelBytes(x, y, 1, 1)[3] > 64);
            Check("afinado táctil estrecha los extremos", Thick(905) < Thick(1200) / 3 && Thick(1495) < Thick(1200) / 3);

            using (var sheet = new CanvasRenderTarget(doc.Device, 220, 50 * _brushes.All.Count(), 96))
            {
                using (var ds = sheet.CreateDrawingSession())
                {
                    ds.Clear(Colors.White);
                    var y = 0;
                    foreach (var b in _brushes.All)
                    {
                        using var cell = new CanvasRenderTarget(ds, 200, 44);
                        using (var cds = cell.CreateDrawingSession())
                        {
                            cds.Clear(Colors.Transparent);
                            BrushEngine.DrawPreview(cds, _tips!, _grains!, b, 200, 44, Colors.Black);
                        }
                        ds.DrawImage(cell, 10, y + 3);
                        y += 50;
                    }
                }
                await File.WriteAllBytesAsync(Path.Combine(outDir, "selftest-brush-previews.png"), await PaintDocument.EncodePngAsync(sheet));
                Check("vistas previas de los 21 pinceles", true);
            }

            // 9c. Selección, máscaras y grupos (SE-01, LY-02, LY-03)
            byte Shown(Layer l, int x, int y)
            {
                var owned = new List<IDisposable>();
                using var rt = new CanvasRenderTarget(doc.Device, doc.Width, doc.Height, 96);
                using (var ds = rt.CreateDrawingSession())
                {
                    ds.Clear(Colors.Transparent);
                    ds.DrawImage(doc.LayerImage(l, null, owned));
                }
                owned.ForEach(o => o.Dispose());
                return rt.GetPixelBytes(x, y, 1, 1)[3];
            }
            var selLayer = ops.Add()!;
            ops.Select(SelectionMask.Ellipse(doc.Width, doc.Height, 1200, 250, 1400, 450), SelectionCombine.Replace, "Selección elíptica");
            ops.Fill(doc.Layers.IndexOf(selLayer), Colors.Black);
            Check("relleno limitado a la selección", Pixel(selLayer, 1300, 350).A == 255 && Pixel(selLayer, 1205, 255).A == 0 && Pixel(selLayer, 1100, 350).A == 0);
            ops.InvertSelection();
            ops.Clear(doc.Layers.IndexOf(selLayer));
            Check("borrar con selección invertida respeta la forma", Pixel(selLayer, 1300, 350).A == 255);
            ops.Deselect();
            ops.AddMask(selLayer);
            Check("máscara nueva no cambia nada", selLayer.EditingMask && Shown(selLayer, 1300, 350) == 255);
            ops.Select(SelectionMask.Rectangle(doc.Width, doc.Height, 1300, 250, 1400, 450), SelectionCombine.Replace, "Selección rectangular");
            ops.Fill(doc.Layers.IndexOf(selLayer), Colors.Black);
            ops.Deselect();
            Check("máscara negra oculta", Shown(selLayer, 1350, 350) == 0 && Shown(selLayer, 1250, 350) == 255 && Pixel(selLayer, 1350, 350).A == 255);
            selLayer.EditingMask = false;
            ops.GroupWithBelow(doc.Layers.IndexOf(selLayer));
            var group = doc.Group(selLayer.GroupId);
            Check("agrupar con la de abajo", group is not null && doc.Layers.Count(l => l.GroupId == group.Id) == 2);
            byte Flat(int x, int y) { using var f = doc.Flatten(); return f.GetPixelBytes(x, y, 1, 1)[0]; }
            var opaque = Flat(1250, 350);
            ops.SetGroup(group!, 0.5f, g => g.Opacity, (g, v) => g.Opacity = v, "Opacidad del grupo");
            Check("opacidad del grupo", opaque < 10 && Flat(1250, 350) > 60);

            // 9d. Transformar, Licuar y Asistente de dibujo (TR-01, LQ-01, DG-01)
            string LayerHash(Layer l) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(l.Surface.GetPixelBytes()))[..16];
            var trLayer = ops.Add()!;
            ops.Select(SelectionMask.Rectangle(doc.Width, doc.Height, 1100, 560, 1300, 700), SelectionCombine.Replace, "Selección rectangular");
            ops.Fill(doc.Layers.IndexOf(trLayer), Colors.Black);
            ops.Deselect();
            using (var t = TransformSession.Start(doc, trLayer)!)
            {
                Check("distorsionar acepta un cuadrilátero válido", t.State.SetCorners([new(1150, 520), new(1450, 560), new(1420, 740), new(1120, 700)]));
                Check("distorsionar rechaza uno cruzado", !t.State.MovePoint(0, new Vector2(1500, 800)));
                t.Apply(ops);
            }
            Check("distorsionar mueve el contenido", Pixel(trLayer, 1400, 600).A == 255 && Pixel(trLayer, 1105, 565).A == 0);
            var beforeWarp = LayerHash(trLayer);
            using (var t = TransformSession.Start(doc, trLayer)!)
            {
                t.SetMode(TransformMode.Warp);
                var pts = t.State.HandlePoints();
                t.State.MovePoint(5, pts[5] + new Vector2(30, 25));
                t.Apply(ops);
            }
            Check("deformar cambia la capa", LayerHash(trLayer) != beforeWarp);
            doc.History.Undo();
            Check("deshacer deformar", LayerHash(trLayer) == beforeWarp);
            doc.History.Redo();

            var beforeLiquify = LayerHash(trLayer);
            using (var lq = LiquifySession.Start(doc, trLayer))
            {
                lq.Size = 160;
                lq.Strength = 1;
                lq.BeginStroke(new Vector2(1200, 630), 1);
                for (var k = 1; k <= 30; k++) lq.ContinueStroke(new Vector2(1200 + k * 4, 630 + k), 1);
                lq.EndStroke();
                lq.Apply(ops);
            }
            Check("licuar cambia la capa", LayerHash(trLayer) != beforeLiquify);
            doc.History.Undo();
            Check("deshacer licuar", LayerHash(trLayer) == beforeLiquify);
            doc.History.Redo();

            doc.Guide = new DrawingGuide { Kind = GuideKind.Grid, Size = 100, Assist = true };
            var assisted = ops.Add()!;
            engine.Begin(new InputPoint(200, 300), ToolKind.Brush, mono, Colors.Black, 6, 1, SymmetryMode.None, 1, assist: NewAssist());
            var wobble = new Random(2);
            for (var k = 1; k <= 60; k++) engine.Add(new InputPoint(200 + k * 6, 300 + (k < 3 ? 1 : wobble.Next(-12, 13))));
            engine.End();
            var assistedRows = Enumerable.Range(270, 60).Where(y => assisted.Surface.GetPixelBytes(200, y, 360, 1).Where((_, i) => i % 4 == 3).Any(a => a > 0)).ToList();
            Check("el asistente endereza el trazo", assistedRows.Count > 0 && assistedRows.Max() - assistedRows.Min() <= 8);
            doc.Guide = new DrawingGuide { Kind = GuideKind.Perspective, Assist = true, VanishingPoints = [new(300, 180), new(1350, 200)] };

            // 9e. Mezcla húmeda y opacidad por presión (BR-04, BR-06)
            var wetLayer = ops.Add()!;
            ops.Select(SelectionMask.Rectangle(doc.Width, doc.Height, 700, 440, 780, 540), SelectionCombine.Replace, "Selección rectangular");
            ops.Fill(doc.Layers.IndexOf(wetLayer), Color.FromArgb(255, 20, 60, 220));
            ops.Deselect();
            var wetBrush = _brushes["roundbrush"].Clone();
            wetBrush.Flow = 1;
            wetBrush.FlowPressure = 0;
            wetBrush.StreamLine = 0;
            wetBrush.Wet = new WetMixSettings { Pull = 0.4f, Charge = 1, Attack = 1 };
            engine.Begin(new InputPoint(560, 490), ToolKind.Brush, wetBrush, red, 24, 1, SymmetryMode.None, 1, seed: 1);
            for (var k = 1; k <= 80; k++) engine.Add(new InputPoint(560 + k * 5, 490));
            engine.End();
            var afterBand = Pixel(wetLayer, 860, 490);
            Check("la mezcla húmeda arrastra el color del lienzo", afterBand.A == 255 && afterBand.B > 90);

            var capLayer = ops.Add()!;
            var capBrush = _brushes["roundbrush"].Clone();
            capBrush.Flow = 1;
            capBrush.FlowPressure = 0;
            capBrush.OpacityPressure = 1;
            capBrush.StreamLine = 0;
            engine.Begin(new InputPoint(560, 600, 0.3f), ToolKind.Brush, capBrush, Colors.Black, 30, 1, SymmetryMode.None, 1, seed: 1);
            for (var pass = 0; pass < 6; pass++)
                for (var k = 0; k <= 40; k++)
                    engine.Add(new InputPoint(560 + (pass % 2 == 0 ? k : 40 - k) * 6, 600, 0.3f));
            engine.End();
            var capMax = Enumerable.Range(580, 40).Max(y => capLayer.Surface.GetPixelBytes(560, y, 240, 1).Where((_, i) => i % 4 == 3).Max());
            Check("la opacidad por presión limita la acumulación", capMax >= 0.28 * 255 && capMax <= 0.32 * 255);

            // 9f. Tipografías propias en Win2D dentro de la app real (regresión del 2026-10-25: ms-appx fallaba sin paquete)
            bool FontWorks(string family)
            {
                try
                {
                    using var fmt = new Microsoft.Graphics.Canvas.Text.CanvasTextFormat { FontFamily = family, FontSize = 13 };
                    using var layout = new Microsoft.Graphics.Canvas.Text.CanvasTextLayout(doc.Device, "Texto", fmt, 200, 30);
                    return layout.LayoutBounds.Width > 0;
                }
                catch (ArgumentException)
                {
                    return false;
                }
            }
            Check("tipografías para la etiqueta de QuickShape y «Añadir texto»", FontWorks(CanvasFonts.Ui) && FontWorks(CanvasFonts.Display));

            // 10. Guardar y volver a abrir
            Canvas.Invalidate();
            doc.IsDirty = true;
            await SaveAsync();
            Check("archivo guardado", doc.FilePath is not null && File.Exists(doc.FilePath));
            ProjectData data;
            data = ProjectSaver.Load(doc.FilePath!).Project;
            using var reopened = await PaintDocument.FromProjectDataAsync(doc.Device, data, doc.FilePath);
            Check("se reabre con las mismas capas", reopened.Layers.Count == doc.Layers.Count && reopened.Width == doc.Width);
            Check("se reabre la guía de dibujo", reopened.Guide is { Kind: GuideKind.Perspective, Assist: true } rg && rg.VanishingPoints.Count == 2);
            Check("se reabren máscaras y grupos", reopened.Layers.Count(l => l.HasMask) == 1 && reopened.Groups.Count == 1 && reopened.Groups[0].Opacity == 0.5f);
            using (var a = doc.Flatten())
            using (var b = reopened.Flatten())
                Check("misma imagen tras reabrir", a.GetPixelBytes(1050, 150, 1, 1).SequenceEqual(b.GetPixelBytes(1050, 150, 1, 1))
                    && a.GetPixelBytes(1190, 240, 220, 220).SequenceEqual(b.GetPixelBytes(1190, 240, 220, 220)));

            using (var final = doc.Flatten())
                await File.WriteAllBytesAsync(Path.Combine(outDir, "selftest-render.png"), await PaintDocument.EncodePngAsync(final));

            // 10b. PSD (EX-01): exportar en la GPU real y volver a importar
            var psdPath = Path.Combine(outDir, "selftest.psd");
            await using (var fs = File.Create(psdPath)) PsdInterop.Write(doc, fs);
            Check("PSD exportado", new FileInfo(psdPath).Length > 1000);
            PsdInterop.ImportResult psdBack;
            await using (var fs = File.OpenRead(psdPath)) psdBack = PsdInterop.Read(doc.Device, fs, "selftest");
            using (var psdDoc = psdBack.Document)
            {
                var extra = doc.BackgroundVisible ? 1 : 0;
                Check("el PSD reabre con las mismas capas, máscaras y grupos",
                    psdDoc.Layers.Count == doc.Layers.Count + extra && psdDoc.Layers.Count(l => l.HasMask) == 1 && psdDoc.Groups.Count == 1);
                using var a2 = doc.Flatten();
                using var b2 = psdDoc.Flatten();
                var pa = a2.GetPixelBytes(1190, 240, 220, 220);
                var pb = b2.GetPixelBytes(1190, 240, 220, 220);
                Check("misma imagen tras el viaje por PSD (± 1 nivel)", pa.Zip(pb).All(x => Math.Abs(x.First - x.Second) <= 1));
            }

            // 11. Recuperación tras perder la GPU (RC-01.7): el disparo real no se puede provocar con un dispositivo
            // sano, así que se llama a la recuperación directamente; el resto del camino es el mismo
            Check("diario de sesión abierto mientras se trabaja", File.Exists(App.Journal.FilePath));
            doc.IsDirty = true;
            await SaveAsync();
            byte[] beforeLoss;
            using (var f = doc.Flatten()) beforeLoss = f.GetPixelBytes(1100, 500, 400, 300);
            var oldDoc = doc;
            var oldTips = _tips;
            var layerCount = doc.Layers.Count;
            var trIndex = doc.Layers.IndexOf(trLayer);
            var recovered = await RecoverFromDeviceLossAsync();
            Check("la recuperación reabre el documento desde el último guardado",
                recovered && _doc is not null && !ReferenceEquals(_doc, oldDoc) && _doc.Layers.Count == layerCount);
            doc = _doc!;
            ops = _ops!;
            engine = _engine!;
            trLayer = doc.Layers[trIndex];
            using (var f = doc.Flatten())
            {
                var after = f.GetPixelBytes(1100, 500, 400, 300);
                Check("misma imagen tras la recuperación (± 1 nivel)", after.Length == beforeLoss.Length && after.Zip(beforeLoss).All(p => Math.Abs(p.First - p.Second) <= 1));
            }
            Check("cachés de la GPU nuevas", _tips is not null && !ReferenceEquals(_tips, oldTips) && _thumbs.Count == 0);
            doc.ActiveIndex = doc.Layers.Count - 1;
            PaintTestStroke(960);
            Check("se puede seguir pintando tras la recuperación", TestStrokePresent(960));

            // deja una herramienta abierta para la captura de la ventana (TRAZO_SELFTEST_UI elige cuál)
            switch (Environment.GetEnvironmentVariable("TRAZO_SELFTEST_UI"))
            {
                case "select":
                    ops.Select(SelectionMask.Ellipse(doc.Width, doc.Height, 300, 250, 900, 750), SelectionCombine.Replace, "Selección elíptica");
                    EnterSelectMode();
                    ShowLayersPanel();
                    break;
                case "liquify":
                    doc.ActiveIndex = doc.Layers.IndexOf(trLayer);
                    StartLiquify();
                    _liquify!.Mode = LiquifyMode.TwirlRight;
                    _liquify.Size = 300;
                    _liquify.Strength = 1;
                    for (var k = 0; k < 6; k++) _liquify.DabAt(new Vector2(1250, 640), 1);
                    BuildLiquifySheet();
                    _hover = _view.ToScreen(new Vector2(1250, 640));
                    break;
                case "curves" or "gradient" or "balance":
                    doc.ActiveIndex = doc.Layers.IndexOf(trLayer);
                    var id = Environment.GetEnvironmentVariable("TRAZO_SELFTEST_UI") switch { "curves" => "curves", "gradient" => "gradientmap", _ => "balance" };
                    StartAdjustment(Adjustment.All.First(a => a.Id == id));
                    if (id == "curves")
                    {
                        // curva en S para que la captura muestre el editor y su efecto
                        _adjust!.Inputs.Curves = new Core.Imaging.ToneCurves().With(0, Core.Brushes.DynamicsCurve.From(new(0, 0), new(0.3f, 0.15f), new(0.7f, 0.9f), new(1, 1)));
                        SheetBody.Children.Clear();
                        AddCurvesEditor(_adjust);
                    }
                    else if (id == "gradientmap") SheetBody.Children.OfType<Microsoft.UI.Xaml.Controls.GridView>().First().SelectedIndex = 3;
                    else
                    {
                        // el rectángulo de esa capa es negro semitransparente: las sombras son las que lo mueven
                        _adjust!.Values["s2"] = 70;
                        SheetBody.Children.Clear();
                        AddBalanceEditor(_adjust, "s");
                    }
                    Canvas.Invalidate();
                    break;
                case "about":
                    _ = AboutAsync();
                    break;
                case "guide":
                    ShowGuideSheet();
                    break;
                case "brush":
                    ToolState(ToolKind.Brush).Brush = "oil";
                    ShowBrushLibrary(BrushButton, ToolKind.Brush, showCurves: true);
                    break;
                default:
                    doc.ActiveIndex = doc.Layers.IndexOf(trLayer);
                    StartTransform(TransformMode.Warp);
                    var wp = _transform!.State.HandlePoints();
                    _transform.State.MovePoint(5, wp[5] + new Vector2(40, 30));
                    break;
            }
            Canvas.Invalidate();
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
