using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Trazo.App.Services;
using Trazo.App.Views;
using Trazo.Core.IO;

namespace Trazo.App;

/// <summary>Aviso de sesión abandonada y escenarios de la prueba de cierre forzado (RC-01).</summary>
public sealed partial class MainWindow
{
    private static string SessionsFolder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Trazo", "sessions");

    /// <summary>Al arrancar: si una sesión anterior terminó de forma inesperada, se dice qué se conservó.</summary>
    public void CheckAbandonedSessionsWhenReady()
    {
        void Check(object sender, RoutedEventArgs e)
        {
            Gallery.Loaded -= Check;
            _ = CheckAbandonedSessionsAsync();
        }
        if (Gallery.IsLoaded) _ = CheckAbandonedSessionsAsync();
        else Gallery.Loaded += Check;
    }

    private async Task CheckAbandonedSessionsAsync()
    {
        List<SessionEntry> abandoned;
        try
        {
            abandoned = await Task.Run(() => SessionJournal.CollectAbandoned(SessionsFolder, null, out var discarded));
        }
        catch (Exception e)
        {
            Log.Error("No se pudieron revisar las sesiones anteriores", e);
            return;
        }
        foreach (var s in abandoned)
            Log.Info($"Sesión anterior terminada de forma inesperada: {s.Document} (último guardado: {s.LastSaved?.ToString("u") ?? "ninguno"})");
        // un primer guardado cortado a la mitad deja solo el temporal o la copia: el lector los recupera (FI-01.4)
        static bool Recoverable(string d) => File.Exists(d) || File.Exists(ProjectSaver.TempPath(d)) || File.Exists(ProjectSaver.BackupPath(d));
        var last = abandoned.Where(s => s.Document is { } d && Recoverable(d)).MaxBy(s => s.LastSaved ?? s.Opened);
        if (last?.Document is not { } path) return;
        var when = last.LastSaved is { } t ? $"está guardada hasta las {t.ToLocalTime():HH:mm:ss}" : "se cerró mientras se guardaba; se intentará recuperar lo último guardado";
        var text = new TextBlock
        {
            Text = $"Trazo se cerró inesperadamente la última vez. «{Path.GetFileNameWithoutExtension(path)}» {when}.",
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 420,
        };
        if (await Dialogs.ShowAsync(Content.XamlRoot, "Recuperación", text, "Abrir", "Cerrar") == ContentDialogResult.Primary)
            await OpenFileAsync(path);
    }

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
