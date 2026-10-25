// Mediciones de rendimiento de Trazo (PF-01, docs/specs/PF-01-performance.md).
//
// Uso:
//   dotnet run --project tools/Trazo.Benchmarks -c Release -p:Platform=x64 -- [--warp] [--quick] [--out carpeta]
//   --warp   usa Direct3D por software (CI); las cifras no son comparables con una GPU y no se aplican los presupuestos
//   --quick  lienzo de 1920 × 1080 y menos repeticiones
// Sale con código 1 si alguna medición en GPU supera su presupuesto.
using System.Diagnostics;
using System.Numerics;
using System.Text;
using System.Text.Json;
using Microsoft.Graphics.Canvas;
using Trazo.Core.Brushes;
using Trazo.Core.Geometry;
using Trazo.Core.Imaging;
using Trazo.Core.IO;
using Trazo.Core.Strokes;
using Trazo.Rendering;
using Windows.UI;

var warp = args.Contains("--warp");
var quick = args.Contains("--quick");
var outIndex = Array.IndexOf(args, "--out");
var outDir = outIndex >= 0 && outIndex + 1 < args.Length ? args[outIndex + 1] : Path.Combine(AppContext.BaseDirectory, "resultados");
Directory.CreateDirectory(outDir);

var (w, h) = quick ? (1920, 1080) : (3840, 2160);
var iterations = quick ? 15 : 60;
// varias rondas y la mediana de sus p95: una ronda con el equipo ocupado no decide el resultado
var rounds = quick ? 1 : 3;
const int Layers = 20;
var device = warp ? new CanvasDevice(forceSoftwareRenderer: true) : CanvasDevice.GetSharedDevice();
var results = new List<Measurement>();
Console.WriteLine($"Trazo · rendimiento · {(warp ? "WARP (software)" : "GPU")} · lienzo {w} × {h} · {Layers} capas · {rounds} rondas de {iterations} repeticiones");

// Vista de 2560 × 1440 donde se compone cada fotograma, como la ventana de la app
using var view = new CanvasRenderTarget(device, 2560, 1440, 96);
var fit = Math.Min(2560f / w, 1440f / h);
// la vista compone como la app: con la caché de la parte fija (--sin-cache para medir sin ella)
var composition = args.Contains("--sin-cache") ? null : new CompositionCache();

// Espera a que la GPU termine: leer un píxel obliga a sincronizar
void Fence(CanvasRenderTarget t) => t.GetPixelBytes(0, 0, 1, 1);

void Frame(PaintDocument doc, IReadOnlyDictionary<object, ICanvasImage>? overrides = null)
{
    var owned = new List<IDisposable>();
    using (var ds = view.CreateDrawingSession())
    {
        ds.Clear(Color.FromArgb(255, 30, 30, 30));
        ds.Transform = Matrix3x2.CreateScale(fit);
        var image = composition is null ? doc.Compose(overrides, owned) : composition.Compose(doc, overrides, owned);
        ds.DrawImage(image, Vector2.Zero, doc.Bounds, 1, CanvasImageInterpolation.Linear);
    }
    owned.ForEach(o => o.Dispose());
    Fence(view);
}

// budgetMs: presupuesto mínimo (decide); targetMs: objetivo (informativo)
Measurement Measure(string id, string name, double budgetMs, int n, Action body, int warmup = 3, double? targetMs = null, int? roundsOverride = null)
{
    for (var i = 0; i < warmup; i++) body();
    var p50s = new List<double>();
    var p95s = new List<double>();
    var max = 0.0;
    for (var r = 0; r < (roundsOverride ?? rounds); r++)
    {
        var times = new List<double>();
        for (var i = 0; i < n; i++)
        {
            var sw = Stopwatch.StartNew();
            body();
            times.Add(sw.Elapsed.TotalMilliseconds);
        }
        times.Sort();
        p50s.Add(times[times.Count / 2]);
        p95s.Add(times[(int)Math.Ceiling(times.Count * 0.95) - 1]);
        max = Math.Max(max, times[^1]);
    }
    double Median(List<double> v) => v.Order().ElementAt(v.Count / 2);
    var m = new Measurement(id, name, budgetMs, targetMs, Median(p50s), Median(p95s), max, !warp);
    results.Add(m);
    var target = targetMs is { } t ? $"objetivo {t,5:0.0} ms {(m.P95 <= t ? "sí" : "no")}" : "";
    Console.WriteLine($"  {m.Id,-6} {m.Name,-52} p50 {m.P50,8:0.0} ms   p95 {m.P95,8:0.0} ms   mínimo {m.Budget,6:0.0} ms   {(m.Gated ? (m.Passed ? "OK" : "SUPERA") : "-")}   {target}");
    return m;
}

// ---- Documento de trabajo: 20 capas con contenido, modos de fusión, un grupo y una máscara ----
var lib = new BrushLibrary();
var doc = PaintDocument.CreateBlank(device, w, h, "Rendimiento");
var ops = new LayerOperations(doc);
var engine = new BrushEngine(doc, ops, new TipCache(device));
var rng = new Random(7);
for (var l = 0; l < Layers; l++)
{
    if (l > 0) ops.Add();
    var brush = lib.All.ElementAt(l % lib.All.Count());
    engine.Begin(new InputPoint(w * 0.1f, h * (0.1f + 0.04f * l)), ToolKind.Brush, brush, Color.FromArgb(255, (byte)rng.Next(256), (byte)rng.Next(256), (byte)rng.Next(256)), Math.Min(80, brush.MaxSize), 1, SymmetryMode.None, 1, seed: l);
    for (var k = 1; k <= 60; k++) engine.Add(new InputPoint(w * (0.1f + 0.013f * k), h * (0.1f + 0.04f * l) + MathF.Sin(k * 0.3f) * h * 0.05f, 0.6f));
    engine.End();
    if (l % 4 == 1) ops.Set(doc.ActiveLayer, "blend", Trazo.Core.Documents.BlendMode.Multiply, x => x.Blend, (x, v) => x.Blend = v, "fusión");
}
ops.AddMask(doc.Layers[5]);
doc.Layers[5].EditingMask = false;
ops.GroupWithBelow(10);
doc.ActiveIndex = Layers - 1;
doc.History.Clear();

// PF-01.1 · Trazo: dos muestras del lápiz (240 Hz) y el fotograma de 120 Hz con la vista previa del trazo
foreach (var (id, brushId, size) in new[] { ("PF-1a", "monoline", 12f), ("PF-1b", "gouache", 120f), ("PF-1c", "softair", 400f) })
{
    var brush = lib[brushId];
    var x = 0f;
    engine.Begin(new InputPoint(200, h / 2f, 0.7f), ToolKind.Brush, brush, Color.FromArgb(255, 20, 60, 200), size, 1, SymmetryMode.None, 1, seed: 1);
    Measure(id, $"Trazo con «{brush.Name}» ({size:0} px): 2 muestras + fotograma", 16.7, iterations * 2, targetMs: 8.3, body: () =>
    {
        for (var s = 0; s < 2; s++)
        {
            x += 4;
            engine.Add(new InputPoint(200 + x, h / 2f + MathF.Sin(x * 0.01f) * 200, 0.7f));
        }
        var owned = new List<IDisposable>();
        var overrides = new Dictionary<object, ICanvasImage>();
        if (engine.Preview(owned) is { } stroke && engine.PreviewKey is { } key) overrides[key] = stroke;
        Frame(doc, overrides);
        owned.ForEach(o => o.Dispose());
    });
    engine.Cancel();
}

// PF-01.2 · Composición del documento completo en la vista
Measure("PF-2", $"Componer {Layers} capas (grupo, máscara, multiplicar) en la vista", 16.7, iterations, () => Frame(doc), targetMs: 8.3);

// PF-01.3 · Licuar: un toque de 300 px y su fotograma
using (var liquify = LiquifySession.Start(doc, doc.ActiveLayer))
{
    liquify.Mode = LiquifyMode.TwirlRight;
    liquify.Size = 300;
    var p = 0f;
    Measure("PF-3", "Licuar: toque de 300 px + fotograma", 16.7, iterations, targetMs: 8.3, body: () =>
    {
        p += 10;
        liquify.DabAt(new Vector2(w / 2f + p % 400, h / 2f), 1);
        var owned = new List<IDisposable>();
        Frame(doc, new Dictionary<object, ICanvasImage> { [liquify.TargetKey] = liquify.Preview(owned) });
        owned.ForEach(o => o.Dispose());
    });
}

// PF-01.4 · Transformar en Deformar: fotograma con la malla
using (var t = TransformSession.Start(doc, doc.ActiveLayer)!)
{
    t.SetMode(TransformMode.Warp);
    var pts = t.State.HandlePoints();
    var k = 0;
    Measure("PF-4", "Deformar: mover un punto + fotograma con la malla", 16.7, iterations, targetMs: 8.3, body: () =>
    {
        t.State.MovePoint(5, pts[5] + new Vector2(k++ % 40, 10));
        var owned = new List<IDisposable>();
        Frame(doc, new Dictionary<object, ICanvasImage> { [t.TargetKey] = t.Preview(owned) });
        owned.ForEach(o => o.Dispose());
    });
}

// PF-01.5 · Guardar y abrir (el autoguardado no debe bloquear: se mide cuánto ocupa el hilo que lo llama)
var file = Path.Combine(outDir, "rendimiento.trazo");
ProjectData? data = null;
var saveRuns = quick ? 3 : 5;
Measure("PF-5a", $"Primer guardado: codificar las {Layers} capas", 10000, 1, () => data = doc.ToProjectDataAsync().GetAwaiter().GetResult(), warmup: 0, roundsOverride: 1);
var editY = 0f;
Measure("PF-5d", "Autoguardado tras pintar una capa (solo se codifica esa)", 1500, saveRuns, () =>
{
    editY += 7;
    engine.Begin(new InputPoint(300, 300 + editY), ToolKind.Brush, lib["roundbrush"], Color.FromArgb(255, 0, 0, 0), 30, 1, SymmetryMode.None, 1, seed: 3);
    engine.Add(new InputPoint(600, 300 + editY));
    engine.End();
    data = doc.ToProjectDataAsync().GetAwaiter().GetResult();
}, warmup: 0, roundsOverride: 1);
Measure("PF-5b", "Autoguardado: escribir y verificar el .trazo (en segundo plano)", 5000, saveRuns, () => ProjectSaver.Save(file, data!), warmup: 1, roundsOverride: 1);
Measure("PF-5c", "Abrir el .trazo", 5000, saveRuns, () =>
{
    var loaded = ProjectSaver.Load(file).Project;
    using var reopened = PaintDocument.FromProjectDataAsync(device, loaded, file).GetAwaiter().GetResult();
}, warmup: 1, roundsOverride: 1);

// PF-01.6 · Memoria (informativo): textura por capa y memoria privada del proceso
var layerMb = (double)w * h * 4 / (1024 * 1024);
using var me = Process.GetCurrentProcess();
me.Refresh();
var privateMb = me.PrivateMemorySize64 / (1024.0 * 1024);
Console.WriteLine($"  PF-6   Memoria: {layerMb:0} MB de GPU por capa · {layerMb * Layers:0} MB para {Layers} capas · memoria privada del proceso {privateMb:0} MB");

// ---- Informe ----
var failed = results.Where(r => r.Gated && !r.Passed).ToList();
var md = new StringBuilder();
md.AppendLine($"| Medición | p50 | p95 | Máx. | Mínimo (p95) | Objetivo (p95) | Resultado |");
md.AppendLine("|---|---|---|---|---|---|---|");
foreach (var r in results)
{
    var target = r.Target is { } t ? $"{t:0.0} ms {(r.P95 <= t ? "✅" : "—")}" : "—";
    md.AppendLine($"| {r.Id} · {r.Name} | {r.P50:0.0} ms | {r.P95:0.0} ms | {r.Max:0.0} ms | {r.Budget:0.0} ms | {target} | {(r.Gated ? (r.Passed ? "✅" : "❌") : "—")} |");
}
md.AppendLine($"| PF-6 · Memoria de GPU por capa / {Layers} capas / privada del proceso | {layerMb:0} MB | {layerMb * Layers:0} MB | {privateMb:0} MB | informativo | — | — |");
File.WriteAllText(Path.Combine(outDir, "rendimiento.md"), md.ToString());
File.WriteAllText(Path.Combine(outDir, "rendimiento.json"), JsonSerializer.Serialize(new { warp, width = w, height = h, layers = Layers, iterations, results }, new JsonSerializerOptions { WriteIndented = true }));
File.Delete(file);
Console.WriteLine(failed.Count == 0 ? "RESULTADO: dentro de los presupuestos" + (warp ? " (WARP: sin presupuestos)" : "") : $"RESULTADO: {failed.Count} mediciones superan su presupuesto");
doc.Dispose();
return failed.Count == 0 ? 0 : 1;

internal sealed record Measurement(string Id, string Name, double Budget, double? Target, double P50, double P95, double Max, bool Gated)
{
    public bool Passed => P95 <= Budget;
}
