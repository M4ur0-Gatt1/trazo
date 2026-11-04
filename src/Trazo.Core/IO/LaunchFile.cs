namespace Trazo.Core.IO;

/// <summary>Qué hace Trazo con un archivo que recibe al abrirse (FA-01).</summary>
public enum LaunchKind
{
    Unsupported,
    Project,
    Psd,
    Image,
}

/// <summary>
/// FA-01: archivo abierto desde el Explorador (doble clic, «Abrir con», arrastrar sobre el icono). Decide qué archivo
/// de la línea de comandos se abre y cómo.
/// </summary>
public static class LaunchFile
{
    /// <summary>Las mismas que acepta Importar.</summary>
    public static readonly IReadOnlyList<string> ImageExtensions = [".png", ".jpg", ".jpeg", ".bmp", ".gif", ".tif", ".tiff", ".webp"];

    public static LaunchKind Classify(string path)
    {
        var ext = Path.GetExtension(path);
        if (ext.Equals(TrazoFile.Extension, StringComparison.OrdinalIgnoreCase)) return LaunchKind.Project;
        if (ext.Equals(".psd", StringComparison.OrdinalIgnoreCase)) return LaunchKind.Psd;
        return ImageExtensions.Any(e => e.Equals(ext, StringComparison.OrdinalIgnoreCase)) ? LaunchKind.Image : LaunchKind.Unsupported;
    }

    /// <summary>
    /// El primer argumento que es un archivo existente (las opciones «--algo» no cuentan). Se prefiere uno que Trazo
    /// sepa abrir; si no hay ninguno, se devuelve el primero para poder avisar de que no se admite.
    /// </summary>
    public static string? Pick(IEnumerable<string> args, Func<string, bool> exists)
    {
        var files = args.Where(a => !a.StartsWith("--", StringComparison.Ordinal) && exists(a)).ToList();
        return files.FirstOrDefault(f => Classify(f) != LaunchKind.Unsupported) ?? files.FirstOrDefault();
    }
}
