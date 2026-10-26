using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Trazo.Core.Brushes;
using Trazo.Core.Documents;
using Trazo.Core.Localization;
using Trazo.Core.Strokes;

namespace Trazo.Core.Tests;

/// <summary>Las pruebas de idioma cambian el idioma global: no corren a la vez que las demás.</summary>
[CollectionDefinition("Idioma", DisableParallelization = true)]
public sealed class LanguageCollection;

/// <summary>I18N-01 — Interfaz en inglés (docs/specs/I18N-01-english.md).</summary>
[Collection("Idioma")]
public sealed partial class I18N01_LocalizationTests : IDisposable
{
    public void Dispose() => Tr.Use(UiLanguage.Spanish);

    // ---------- Fuente ----------

    private static string Src()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent)
            if (File.Exists(Path.Combine(d.FullName, "src", "Trazo.Core", "Localization", "en.json"))) return Path.Combine(d.FullName, "src");
        throw new InvalidOperationException("No se encontró src/");
    }

    private static IEnumerable<string> Files(string pattern) =>
        Directory.EnumerateFiles(Src(), pattern, SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"));

    /// <summary>Herramientas de prueba dentro de la app: sus textos son internos.</summary>
    private static bool IsTestTooling(string file) => Path.GetFileName(file) is var n && (n.Contains("SelfTest") || n.Contains("CrashTest"));

    [GeneratedRegex(@"(?<![\w.])[TF]\(\s*""((?:[^""\\]|\\.)*)""")]
    private static partial Regex CodeKey();

    [GeneratedRegex(@"\{t:Loc S='((?:[^'\\]|\\.)*)'\}")]
    private static partial Regex XamlKey();

    private static string Unescape(string s) => Regex.Unescape(s);

    /// <summary>Todos los textos marcados en el código y en el XAML.</summary>
    internal static SortedSet<string> MarkedKeys()
    {
        var keys = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var f in Files("*.cs"))
            foreach (Match m in CodeKey().Matches(File.ReadAllText(f))) keys.Add(Unescape(m.Groups[1].Value));
        foreach (var f in Files("*.xaml"))
            foreach (Match m in XamlKey().Matches(File.ReadAllText(f))) keys.Add(m.Groups[1].Value.Replace("\\'", "'"));
        return keys;
    }

    // ---------- I18N-01.1 a .3 ----------

    [Fact]
    public void EveryMarkedStringHasEnglish()
    {
        var en = Tr.LoadEnglish();
        var keys = MarkedKeys();
        Assert.True(keys.Count > 400, $"solo {keys.Count} textos marcados: ¿falla la extracción?");
        var missing = keys.Where(k => !en.TryGetValue(k, out var v) || string.IsNullOrWhiteSpace(v)).ToList();
        Assert.True(missing.Count == 0, "Sin traducción:\n" + string.Join("\n", missing));
    }

    [Fact]
    public void CatalogHasNoStaleEntries()
    {
        var keys = MarkedKeys();
        var stale = Tr.LoadEnglish().Keys.Where(k => !keys.Contains(k)).ToList();
        Assert.True(stale.Count == 0, "Entradas que ya no se usan:\n" + string.Join("\n", stale));
    }

    [GeneratedRegex(@"(?<!\{)\{(\d+)(?:[,:][^}]*)?\}")]
    private static partial Regex Placeholder();

    [Fact]
    public void PlaceholdersMatch()
    {
        static string Holes(string s) => string.Join(",", Placeholder().Matches(s).Select(m => m.Value).Order());
        foreach (var (es, en) in Tr.LoadEnglish())
        {
            Assert.True(Holes(es) == Holes(en), $"«{es}» → «{en}»");
            // y los textos que empiezan o acaban con espacio (se concatenan) lo conservan
            Assert.True(es.StartsWith(' ') == en.StartsWith(' ') && es.EndsWith(' ') == en.EndsWith(' '), $"espacios: «{es}» → «{en}»");
        }
    }

    // ---------- I18N-01.4 ----------

    [GeneratedRegex(@"[áéíóúñÁÉÍÓÚÑ¿¡«»]|\b(de|del|la|las|el|los|una?|que|con|para|por|sin|capas?|se|no|al|en|hay)\b", RegexOptions.IgnoreCase)]
    private static partial Regex SpanishSignal();

    /// <summary>Textos que se muestran tal cual en cualquier idioma.</summary>
    private static readonly HashSet<string> Untranslated = ["Español", "English", "es", "en", "Trazo", "QuickShape", "StreamLine"];

    [Fact]
    public void NoUnmarkedSpanishInUi()
    {
        var problems = new List<string>();
        foreach (var f in Files("*.cs").Where(f => !IsTestTooling(f) && !f.EndsWith("Tr.cs")))
        {
            var lines = File.ReadAllLines(f);
            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                if (line.Contains("// i18n: interno")) continue; // excepción explícita y visible en el código
                var code = line.Split("//")[0];
                if (code.TrimStart().StartsWith("///") || code.Contains("Log.") || code.Contains("Exception(") && !code.Contains("InvalidDataException(")) continue;
                foreach (var lit in Literals(code))
                {
                    if (lit.Marked || Untranslated.Contains(lit.Text)) continue;
                    if (SpanishSignal().IsMatch(lit.Text)) problems.Add($"{Path.GetFileName(f)}:{i + 1} {lit.Text}");
                }
            }
        }
        foreach (var f in Files("*.xaml"))
            foreach (Match m in Regex.Matches(File.ReadAllText(f), @"(Text|Content|Header|ToolTipService\.ToolTip|PlaceholderText|Label|Title)=""([^""{]+)"""))
                if (!Untranslated.Contains(m.Groups[2].Value) && SpanishSignal().IsMatch(m.Groups[2].Value)) problems.Add($"{Path.GetFileName(f)} {m.Value}");
        Assert.True(problems.Count == 0, "Texto en español sin marcar:\n" + string.Join("\n", problems));
    }

    /// <summary>Literales de una línea de C# (normales e interpolados) y si van dentro de T( o F(.</summary>
    private static IEnumerable<(string Text, bool Marked)> Literals(string code)
    {
        for (var i = 0; i < code.Length; i++)
        {
            if (code[i] == '\'' && i + 2 < code.Length) { i += code[i + 1] == '\\' ? 3 : 2; continue; }
            if (code[i] != '"') continue;
            var start = i;
            var interp = i > 0 && code[i - 1] == '$';
            var sb = new StringBuilder();
            var depth = 0;
            for (i++; i < code.Length; i++)
            {
                var c = code[i];
                if (depth == 0 && c == '\\') { sb.Append(c).Append(i + 1 < code.Length ? code[++i] : ' '); continue; }
                if (interp && c == '{') { if (depth == 0 && i + 1 < code.Length && code[i + 1] == '{') { i++; continue; } depth++; continue; }
                if (interp && c == '}' && depth > 0) { depth--; continue; }
                if (depth > 0) continue;
                if (c == '"') break;
                sb.Append(c);
            }
            var before = code[..(interp ? start - 1 : start)].TrimEnd();
            var marked = Regex.IsMatch(before, @"(?<![\w.])[TF]\($");
            yield return (sb.ToString(), marked);
        }
    }

    // ---------- I18N-01.5 ----------

    [Fact]
    public void TranslationFallsBackAndFormats()
    {
        Tr.Use(UiLanguage.Spanish);
        Assert.Equal("es", Tr.Language);
        Assert.Equal("Nueva capa", Tr.T("Nueva capa"));
        var dec = CultureInfo.CurrentCulture.NumberFormat.NumberDecimalSeparator;
        Assert.Equal($"1{dec}5 px", Tr.F("{0:0.0} px", 1.5));
        Assert.Equal("50 %", Tr.F("{0:0} %", 50));

        Tr.Use(UiLanguage.English);
        Assert.Equal("en", Tr.Language);
        Assert.Equal("New layer", Tr.T("Nueva capa"));
        Assert.Equal($"1{dec}5 px", Tr.F("{0:0.0} px", 1.5)); // números con el formato regional de Windows
        Assert.Equal("50%", Tr.F("{0:0} %", 50));
        Assert.Equal("Layer 3", Tr.F("Capa {0}", 3));
        // un nombre que puso el usuario, o un texto sin traducción, pasa tal cual
        Assert.Equal("Mi boceto ñ", Tr.T("Mi boceto ñ"));
        Assert.Equal("Layer 1", CanvasLimits.NextLayerName(["Capa 1"]));
    }

    [Fact]
    public void AutomaticFollowsWindows()
    {
        Assert.Equal("es", Tr.Resolve(CultureInfo.GetCultureInfo("es-AR")));
        Assert.Equal("es", Tr.Resolve(CultureInfo.GetCultureInfo("es-ES")));
        Assert.Equal("en", Tr.Resolve(CultureInfo.GetCultureInfo("en-US")));
        Assert.Equal("en", Tr.Resolve(CultureInfo.GetCultureInfo("pt-BR")));
        Assert.Equal("en", Tr.Resolve(CultureInfo.GetCultureInfo("ja-JP")));
        Tr.Use(UiLanguage.Automatic, CultureInfo.GetCultureInfo("es-MX"));
        Assert.Equal("es", Tr.Language);
        Tr.Use(UiLanguage.Automatic, CultureInfo.GetCultureInfo("de-DE"));
        Assert.Equal("en", Tr.Language);
    }

    // ---------- I18N-01.6 ----------

    [Fact]
    public void LibraryNamesAreTranslated()
    {
        Tr.Use(UiLanguage.English);
        var en = Tr.LoadEnglish();
        var values = en.Values.ToHashSet();
        foreach (var m in Enum.GetValues<BlendMode>())
        {
            Assert.True(values.Contains(m.DisplayName()), $"modo {m}: {m.DisplayName()}");
            Assert.DoesNotMatch(SpanishSignal(), m.DisplayName());
        }
        foreach (var b in new BrushLibrary().All)
            Assert.True(values.Contains(b.Name), $"pincel {b.Id}: {b.Name}");
        foreach (var p in CanvasLimits.Presets) Assert.DoesNotMatch(SpanishSignal(), p.Name);
        Assert.Equal("Circle", Tr.T("Círculo"));
    }
}
