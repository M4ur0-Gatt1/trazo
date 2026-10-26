using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Trazo.App.Services;
using Trazo.App.Views;
using Trazo.Core.IO;

namespace Trazo.App;

// Herramienta de pruebas: sus textos son internos y no se traducen (I18N-01)
public sealed partial class MainWindow
{
    // ---------- Prueba de cierre forzado (tools/crash-test.ps1) ----------

    /// <summary>Crea un documento, pinta, espera al autoguardado y avisa con «listo.txt»; el script mata el proceso.</summary>
    public async Task RunCrashScenarioAsync(string folder)
    {
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, "cierre-forzado" + TrazoFile.Extension);
        foreach (var f in new[] { path, ProjectSaver.BackupPath(path) }) if (File.Exists(f)) File.Delete(f);
        var doc = Rendering.PaintDocument.CreateBlank(Device, 800, 600, "Cierre forzado");
        doc.FilePath = path;
        ShowStudio(doc);
        await Task.Delay(500);
        Studio.PaintTestStroke(100);
        // sin guardar a mano: tiene que hacerlo el autoguardado (FI-01.8, 3 s). Se espera a que el guardado TERMINE:
        // el documento deja de estar «sucio» al empezar a guardar, no al acabar
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (Studio.LastSaved is null && DateTime.UtcNow < deadline) await Task.Delay(100);
        await File.WriteAllTextAsync(Path.Combine(folder, "listo.txt"), Studio.LastSaved is null ? "sin guardar" : "guardado");
    }

    /// <summary>Tras matar el proceso: comprueba el aviso de sesión abandonada y que el trazo esté en el archivo.</summary>
    public async Task RunRecoverScenarioAsync(string folder)
    {
        var path = Path.Combine(folder, "cierre-forzado" + TrazoFile.Extension);
        var report = new List<string>();
        void Check(string name, bool ok) => report.Add($"{(ok ? "OK  " : "FAIL")} {name}");
        try
        {
            var abandoned = SessionJournal.CollectAbandoned(SessionsFolder, null, out _);
            var mine = abandoned.FirstOrDefault(s => string.Equals(s.Document, path, StringComparison.OrdinalIgnoreCase));
            Check("se detecta la sesión abandonada", mine is not null);
            Check("el diario tiene la hora del último guardado", mine?.LastSaved is not null);
            await OpenFileAsync(path);
            await Task.Delay(500);
            Check("el trazo pintado antes del cierre está en el archivo", Studio.TestStrokePresent(100));
        }
        catch (Exception e)
        {
            report.Add("ERROR " + e);
        }
        var ok = report.All(l => l.StartsWith("OK"));
        report.Add(ok ? "RESULTADO: todo correcto" : "RESULTADO: con fallos");
        await File.WriteAllLinesAsync(Path.Combine(folder, "recuperacion.txt"), report);
        App.Journal.End(); // esta comprobación termina bien: no debe dejar un diario abandonado
        await Task.Delay(1500);
        Environment.Exit(ok ? 0 : 1);
    }
}
