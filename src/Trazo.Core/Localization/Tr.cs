using System.Globalization;
using System.Text.Json;

namespace Trazo.Core.Localization;

/// <summary>Idiomas de la interfaz (I18N-01).</summary>
public enum UiLanguage
{
    Automatic,
    Spanish,
    English,
}

/// <summary>
/// Traducción de la interfaz (I18N-01, docs/specs/I18N-01-english.md). Estilo gettext: la clave es el texto en español,
/// así que un texto sin traducir, o un nombre que puso el usuario, se muestra tal cual.
/// </summary>
public static class Tr
{
    private static Dictionary<string, string>? _catalog;

    /// <summary>Idioma en uso: "es" o "en".</summary>
    public static string Language { get; private set; } = "es";

    /// <summary>Elige el idioma. Automático: español si Windows está en español y, si no, inglés.</summary>
    public static void Use(UiLanguage language, CultureInfo? system = null)
    {
        var english = language switch
        {
            UiLanguage.English => true,
            UiLanguage.Spanish => false,
            _ => Resolve(system ?? CultureInfo.CurrentUICulture) == "en",
        };
        Language = english ? "en" : "es";
        _catalog = english ? LoadEnglish() : null;
    }

    /// <summary>Idioma automático para una cultura de Windows.</summary>
    public static string Resolve(CultureInfo system) => system.TwoLetterISOLanguageName == "es" ? "es" : "en";

    /// <summary>Texto fijo traducido; sin traducción, el original.</summary>
    public static string T(string spanish) =>
        _catalog is not null && _catalog.TryGetValue(spanish, out var t) ? t : spanish;

    /// <summary>
    /// Texto con valores: se traduce el patrón y los números se formatean con el formato regional de Windows, como el
    /// resto de la interfaz (un inglés con formato regional español ve «7,5 px»).
    /// </summary>
    public static string F(string spanish, params object?[] args) => string.Format(CultureInfo.CurrentCulture, T(spanish), args);

    /// <summary>El catálogo inglés incrustado (texto español → texto inglés).</summary>
    public static Dictionary<string, string> LoadEnglish()
    {
        using var stream = typeof(Tr).Assembly.GetManifestResourceStream("Trazo.Core.Localization.en.json")
            ?? throw new InvalidOperationException("Falta el catálogo en.json");
        using var doc = JsonDocument.Parse(stream);
        var o = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var p in doc.RootElement.EnumerateObject()) o[p.Name] = p.Value.GetString() ?? "";
        return o;
    }
}
