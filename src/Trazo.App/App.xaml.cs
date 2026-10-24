using Microsoft.UI.Xaml;

namespace Trazo.App;

public partial class App : Application
{
    public static MainWindow? Window { get; private set; }

    public App()
    {
        InitializeComponent();
        UnhandledException += (_, e) =>
        {
            Services.Log.Error("Excepción no controlada", e.Exception);
            e.Handled = true;
        };
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        Window = new MainWindow();
        Window.Activate();
        var argv = Environment.GetCommandLineArgs().Skip(1).ToArray();
        var selfTest = Array.IndexOf(argv, "--selftest");
        if (selfTest >= 0)
        {
            var folder = selfTest + 1 < argv.Length ? argv[selfTest + 1] : Path.Combine(Path.GetTempPath(), "trazo-selftest");
            _ = Window.RunSelfTestAsync(folder);
            return;
        }
        // Archivo .trazo pasado por línea de comandos (doble clic en el Explorador)
        var cmd = argv.FirstOrDefault(a => a.EndsWith(".trazo", StringComparison.OrdinalIgnoreCase));
        if (cmd is not null && File.Exists(cmd)) _ = Window.OpenFileAsync(cmd);
    }
}
