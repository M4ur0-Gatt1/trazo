using Microsoft.UI.Xaml;
using Trazo.Core.IO;

namespace Trazo.App;

public partial class App : Application
{
    public static MainWindow? Window { get; private set; }

    /// <summary>Diario de esta sesión (RC-01): si la app se cierra de forma inesperada, el próximo arranque lo detecta.</summary>
    public static SessionJournal Journal { get; } =
        new(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Trazo", "sessions"));

    public App()
    {
        InitializeComponent();
        UnhandledException += (_, e) =>
        {
            Services.Log.Error("Excepción no controlada", e.Exception);
            e.Handled = true;
            var studio = Window?.StudioPage;
            if (studio is null) return;
            // perder la GPU se recupera desde el último guardado; cualquier otro error deja una copia de emergencia
            if (studio.IsDeviceLost(e.Exception)) studio.ReportDeviceLost();
            else _ = studio.EmergencySaveAsync();
        };
        // otros hilos: no se puede evitar el cierre, pero queda registrado (y el diario avisará al volver a abrir)
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            Services.Log.Error("Excepción fatal fuera de la interfaz", e.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Services.Log.Error("Excepción no observada en una tarea", e.Exception);
            e.SetObserved();
        };
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        Window = new MainWindow();
        Window.Activate();
        var argv = Environment.GetCommandLineArgs().Skip(1).ToArray();
        string? Folder(string flag)
        {
            var i = Array.IndexOf(argv, flag);
            if (i < 0) return null;
            return i + 1 < argv.Length ? argv[i + 1] : Path.Combine(Path.GetTempPath(), "trazo-selftest");
        }
        if (Folder("--selftest") is { } selfTest)
        {
            _ = Window.RunSelfTestAsync(selfTest);
            return;
        }
        // prueba de cierre forzado (tools/crash-test.ps1): pintar y esperar a que maten el proceso; luego comprobar
        if (Folder("--selftest-crash") is { } crash)
        {
            _ = Window.RunCrashScenarioAsync(crash);
            return;
        }
        if (Folder("--selftest-recover") is { } recover)
        {
            _ = Window.RunRecoverScenarioAsync(recover);
            return;
        }
        Window.CheckAbandonedSessionsWhenReady();
        // Archivo .trazo pasado por línea de comandos (doble clic en el Explorador)
        var cmd = argv.FirstOrDefault(a => a.EndsWith(".trazo", StringComparison.OrdinalIgnoreCase));
        if (cmd is not null && File.Exists(cmd)) _ = Window.OpenFileAsync(cmd);
    }
}
