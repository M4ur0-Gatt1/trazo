using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Trazo.App.Services;
using Trazo.App.Views;
using Trazo.Core.IO;
using static Trazo.Core.Localization.Tr;

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
        var when = last.LastSaved is { } t ? F("está guardada hasta las {0:HH:mm:ss}", t.ToLocalTime()) : T("se cerró mientras se guardaba; se intentará recuperar lo último guardado");
        var text = new TextBlock
        {
            Text = F("Trazo se cerró inesperadamente la última vez. «{0}» {1}.", Path.GetFileNameWithoutExtension(path), when),
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 420,
        };
        if (await Dialogs.ShowAsync(Content.XamlRoot, T("Recuperación"), text, T("Abrir"), T("Cerrar")) == ContentDialogResult.Primary)
            await OpenFileAsync(path);
    }
}
