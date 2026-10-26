using System.Text.Json;
using System.Text.Json.Serialization;
using Trazo.Core.Brushes;
using Trazo.Core.IO;
using Trazo.Core.Strokes;
using Trazo.Core.Localization;
using static Trazo.Core.Localization.Tr;

namespace Trazo.App.Services;

/// <summary>Registro en %LOCALAPPDATA%\Trazo\logs.</summary>
public static class Log
{
    private static readonly object Gate = new();
    public static string Folder { get; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Trazo", "logs");

    public static void Info(string message) => Write("INFO", message, null);
    public static void Error(string message, Exception? e = null) => Write("ERROR", message, e);

    private static void Write(string level, string message, Exception? e)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(Folder);
                File.AppendAllText(Path.Combine(Folder, $"trazo-{DateTime.Now:yyyy-MM-dd}.log"),
                    $"{DateTime.Now:HH:mm:ss.fff} [{level}] {message}{(e is null ? "" : Environment.NewLine + e)}{Environment.NewLine}");
            }
        }
        catch (IOException)
        {
            // el registro nunca debe tumbar la app
        }
    }
}

public sealed class ToolSettings
{
    public string Brush { get; set; } = "studiopen";
    public float Size { get; set; } = 7;
    public float Opacity { get; set; } = 1;
}

/// <summary>Preferencias persistentes en %LOCALAPPDATA%\Trazo\settings.json.</summary>
public sealed class Settings
{
    private static readonly string FilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Trazo", "settings.json");
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };

    public static Settings Current { get; } = Load();

    /// <summary>Idioma de la interfaz (I18N-01).</summary>
    public UiLanguage Language { get; set; }
    public bool? PenOnly { get; set; }
    public bool LeftHanded { get; set; }
    public bool RotateGesture { get; set; } = true;
    public bool QuickShape { get; set; } = true;
    public bool BrushCursor { get; set; } = true;
    /// <summary>TL-01: grabar el time-lapse mientras se pinta.</summary>
    public bool RecordTimelapse { get; set; } = true;
    public float PressureGamma { get; set; } = 1;
    public int FillTolerance { get; set; } = 46;
    public bool SmoothTransform { get; set; } = true;
    public bool MagneticTransform { get; set; } = true;
    public string LiquifyMode { get; set; } = "Push";
    public float LiquifySize { get; set; } = 200;
    public float LiquifyStrength { get; set; } = 0.6f;
    public string ProjectsFolder { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Trazo");

    public string Tool { get; set; } = "Brush";
    public ToolSettings BrushTool { get; set; } = new();
    public ToolSettings SmudgeTool { get; set; } = new() { Brush = "roundbrush", Size = 40, Opacity = 0.6f };
    public ToolSettings EraserTool { get; set; } = new() { Brush = "softair", Size = 80 };
    public string Color { get; set; } = "#1f1f24";
    public List<string> RecentColors { get; set; } = [];
    public List<string> Palette { get; set; } = [];
    public SymmetryMode Symmetry { get; set; }
    public Dictionary<string, BrushOverride> BrushOverrides { get; set; } = [];

    private static Settings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath), Options) ?? new Settings();
        }
        catch (Exception e) when (e is IOException or JsonException)
        {
            Log.Error("No se pudieron leer las preferencias", e);
        }
        return new Settings();
    }

    private CancellationTokenSource? _pending;

    /// <summary>Guarda con un pequeño retardo para agrupar cambios seguidos.</summary>
    public void Save()
    {
        _pending?.Cancel();
        var cts = _pending = new CancellationTokenSource();
        _ = Task.Delay(400, cts.Token).ContinueWith(t =>
        {
            if (t.IsCanceled) return;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                File.WriteAllText(FilePath, JsonSerializer.Serialize(this, Options));
            }
            catch (IOException e)
            {
                Log.Error("No se pudieron guardar las preferencias", e);
            }
        }, TaskScheduler.Default);
    }

    public void ApplyBrushOverrides(BrushLibrary lib)
    {
        foreach (var (id, o) in BrushOverrides)
            if (lib.TryGet(id, out var b)) o.ApplyTo(b);
    }
}

public sealed class BrushOverride
{
    public float StreamLine { get; set; }
    public float Spacing { get; set; }
    public float Flow { get; set; }
    public float Jitter { get; set; }
    public float Scatter { get; set; }
    public float MinSizePressure { get; set; }
    public float FlowPressure { get; set; }
    // null en preferencias guardadas por versiones anteriores: se conserva el valor del pincel
    public GrainSettings? Grain { get; set; }
    public TaperSettings? PressureTaper { get; set; }
    public TaperSettings? TouchTaper { get; set; }
    public DynamicsCurve? SizeCurve { get; set; }
    public DynamicsCurve? FlowCurve { get; set; }
    public float? OpacityPressure { get; set; }
    public DynamicsCurve? OpacityCurve { get; set; }
    public WetMixSettings? Wet { get; set; }

    public static BrushOverride From(BrushPreset b) => new()
    {
        SizeCurve = b.SizeCurve, FlowCurve = b.FlowCurve, OpacityPressure = b.OpacityPressure, OpacityCurve = b.OpacityCurve, Wet = b.Wet,
        StreamLine = b.StreamLine, Spacing = b.Spacing, Flow = b.Flow, Jitter = b.Jitter,
        Scatter = b.Scatter, MinSizePressure = b.MinSizePressure, FlowPressure = b.FlowPressure,
        Grain = b.Grain, PressureTaper = b.PressureTaper, TouchTaper = b.TouchTaper,
    };

    public void ApplyTo(BrushPreset b)
    {
        b.StreamLine = StreamLine; b.Spacing = Spacing; b.Flow = Flow; b.Jitter = Jitter;
        b.Scatter = Scatter; b.MinSizePressure = MinSizePressure; b.FlowPressure = FlowPressure;
        if (Grain is not null) b.Grain = Grain;
        if (PressureTaper is not null) b.PressureTaper = PressureTaper;
        if (TouchTaper is not null) b.TouchTaper = TouchTaper;
        if (SizeCurve is not null && DynamicsCurve.IsValid(SizeCurve.Points)) b.SizeCurve = SizeCurve;
        if (FlowCurve is not null && DynamicsCurve.IsValid(FlowCurve.Points)) b.FlowCurve = FlowCurve;
        if (OpacityPressure is { } op) b.OpacityPressure = Math.Clamp(op, 0, 1);
        if (OpacityCurve is not null && DynamicsCurve.IsValid(OpacityCurve.Points)) b.OpacityCurve = OpacityCurve;
        if (Wet is not null) b.Wet = Wet;
    }
}

public sealed record ProjectEntry(string Path, string Name, DateTime Modified, byte[]? Thumbnail);

/// <summary>Proyectos guardados como archivos .trazo en Documentos\Trazo.</summary>
public static class ProjectStore
{
    public static string Folder
    {
        get
        {
            var f = Settings.Current.ProjectsFolder;
            Directory.CreateDirectory(f);
            return f;
        }
    }

    public static Task<List<ProjectEntry>> ListAsync() => Task.Run(() =>
    {
        var list = new List<ProjectEntry>();
        foreach (var file in Directory.EnumerateFiles(Folder, "*" + TrazoFile.Extension))
        {
            try
            {
                using var fs = File.OpenRead(file);
                list.Add(new ProjectEntry(file, System.IO.Path.GetFileNameWithoutExtension(file), File.GetLastWriteTime(file), TrazoFile.ReadThumbnail(fs)));
            }
            catch (IOException e)
            {
                Log.Error($"No se pudo leer {file}", e);
            }
        }
        return list.OrderByDescending(p => p.Modified).ToList();
    });

    /// <summary>Ruta libre en la carpeta de proyectos para un nombre dado.</summary>
    public static string NewPath(string name)
    {
        var safe = string.Concat(name.Select(c => System.IO.Path.GetInvalidFileNameChars().Contains(c) ? '_' : c)).Trim();
        if (safe.Length == 0) safe = T("Sin título");
        var path = System.IO.Path.Combine(Folder, safe + TrazoFile.Extension);
        for (var i = 2; File.Exists(path); i++) path = System.IO.Path.Combine(Folder, $"{safe} {i}{TrazoFile.Extension}");
        return path;
    }
}
