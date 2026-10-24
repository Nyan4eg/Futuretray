using System.Text.RegularExpressions;
using Futuretray.Core;

namespace Futuretray.Tests;

/// <summary>Английский интерфейс: ни одной непереведённой или «забытой» строки.</summary>
public partial class LocalizationTests
{
    private static readonly string SourceRoot = FindSourceRoot();

    private static string FindSourceRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "src", "Futuretray", "Futuretray.csproj")))
            dir = dir.Parent;
        return Path.Combine(dir?.FullName ?? throw new DirectoryNotFoundException("src/Futuretray not found"), "src", "Futuretray");
    }

    private static IEnumerable<string> SourceFiles(string pattern) =>
        Directory.EnumerateFiles(SourceRoot, pattern, SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") &&
                        !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"));

    // Отчёт для разработчика, отладочные снимки и сам словарь переводить не нужно.
    private static readonly string[] NotUiFiles = { "DiagnosticsReport.cs", "DebugSnapshot.cs", "English.cs", "Loc.cs" };

    [GeneratedRegex(@"\{ui:T '((?:[^'\\]|\\.)*)'\}")] private static partial Regex XamlKey();
    [GeneratedRegex(@"Loc\.(?:T|F)\(\s*""((?:[^""\\]|\\.)*)""")] private static partial Regex CodeKey();
    [GeneratedRegex(@"Loc\.Plural\([^,]+,\s*""([^""]*)"",\s*""([^""]*)"",\s*""([^""]*)""\)")] private static partial Regex PluralCall();
    [GeneratedRegex(@"[А-Яа-яЁё]")] private static partial Regex Cyrillic();
    [GeneratedRegex(@"\s[\w:.]+=""([^""]*)""")] private static partial Regex XamlAttribute();
    [GeneratedRegex(@"\$?@?""(?:[^""\\]|\\.)*""")] private static partial Regex CodeLiteral();
    [GeneratedRegex(@"\{(\d+)(:[^}]*)?\}")] private static partial Regex Placeholder();

    private static string Unescape(string s) => s.Replace("\\n", "\n").Replace("\\\"", "\"").Replace("\\\\", "\\");

    public static IEnumerable<string> UsedKeys()
    {
        var keys = new HashSet<string>();
        foreach (var file in SourceFiles("*.xaml"))
            foreach (Match m in XamlKey().Matches(File.ReadAllText(file)))
                keys.Add(m.Groups[1].Value.Replace("\\'", "'").Replace("&#x0a;", "\n"));
        foreach (var file in SourceFiles("*.cs").Where(f => !NotUiFiles.Contains(Path.GetFileName(f))))
        {
            var text = File.ReadAllText(file);
            foreach (Match m in CodeKey().Matches(text)) keys.Add(Unescape(m.Groups[1].Value));
            foreach (Match m in PluralCall().Matches(text))
            {
                keys.Add(m.Groups[1].Value);
                keys.Add(m.Groups[3].Value);
            }
        }
        return keys;
    }

    [Fact]
    public void EveryUiString_HasEnglishTranslation()
    {
        var missing = UsedKeys().Where(k => !English.Strings.ContainsKey(k)).ToList();
        Assert.True(missing.Count == 0, "Нет перевода для:\n" + string.Join("\n", missing));
    }

    [Fact]
    public void NoRussianText_BypassesLocalization_InXaml()
    {
        var allowed = new HashSet<string> { "Русский" }; // название языка пишется на самом языке
        var raw = SourceFiles("*.xaml")
            .SelectMany(f => XamlAttribute().Matches(File.ReadAllText(f)).Select(m => (File: Path.GetFileName(f), Value: m.Groups[1].Value)))
            .Where(a => Cyrillic().IsMatch(a.Value) && !a.Value.StartsWith("{ui:T") && !allowed.Contains(a.Value))
            .Select(a => $"{a.File}: {a.Value}")
            .ToList();
        Assert.True(raw.Count == 0, "Текст без {ui:T}:\n" + string.Join("\n", raw));
    }

    [Fact]
    public void NoRussianText_BypassesLocalization_InCode()
    {
        var raw = new List<string>();
        foreach (var file in SourceFiles("*.cs").Where(f => !NotUiFiles.Contains(Path.GetFileName(f))))
        {
            foreach (var line in File.ReadLines(file))
            {
                var code = line.Trim();
                if (code.StartsWith("//") || code.StartsWith("[InlineData") || code.Contains("Log.Error(") || code.Contains("Log.Info(")) continue;
                code = PluralCall().Replace(code, "");
                code = CodeKey().Replace(code, "");
                if (CodeLiteral().Matches(code).Any(m => Cyrillic().IsMatch(m.Value)))
                    raw.Add($"{Path.GetFileName(file)}: {line.Trim()}");
            }
        }
        Assert.True(raw.Count == 0, "Строки без Loc.T/Loc.F:\n" + string.Join("\n", raw));
    }

    [Fact]
    public void Translations_KeepTheSamePlaceholders()
    {
        static string Shape(string s) => string.Join(",", Placeholder().Matches(s).Select(m => m.Value).OrderBy(v => v));
        var broken = English.Strings.Where(p => Shape(p.Key) != Shape(p.Value)).Select(p => p.Key).ToList();
        Assert.True(broken.Count == 0, "Не совпадают {0}:\n" + string.Join("\n", broken));
    }

    [Fact]
    public void EveryString_FormatsWithoutErrors()
    {
        foreach (var (ru, en) in English.Strings)
        {
            int count = Placeholder().Matches(ru).Select(m => int.Parse(m.Groups[1].Value) + 1).DefaultIfEmpty(0).Max();
            var args = Enumerable.Repeat<object>(0.5, count).ToArray();
            _ = string.Format(ru, args);
            _ = string.Format(en, args);
        }
    }

    [Fact]
    public void Plural_UsesEnglishFormsInEnglish()
    {
        try
        {
            Loc.Init(UiLanguage.English);
            Assert.Equal("app", Loc.Plural(1, "приложение", "приложения", "приложений"));
            Assert.Equal("apps", Loc.Plural(3, "приложение", "приложения", "приложений"));
            Assert.Equal("Groups", Loc.T("Группы"));
            Assert.Equal("Selected: 2", Loc.F("Выбрано: {0}", 2));
            Assert.Equal("нет такого ключа", Loc.T("нет такого ключа"));
        }
        finally
        {
            Loc.Init(UiLanguage.Russian);
        }
        Assert.Equal("Группы", Loc.T("Группы"));
        Assert.Equal("приложения", Loc.Plural(3, "приложение", "приложения", "приложений"));
    }
}
