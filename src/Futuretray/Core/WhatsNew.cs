using System.Reflection;
using System.Text.RegularExpressions;

namespace Futuretray.Core;

/// <summary>
/// «Что нового» после обновления: разделы CHANGELOG (вшит в программу на русском и английском)
/// между последней версией, которую пользователь видел, и текущей.
/// </summary>
public static partial class WhatsNew
{
    private const int MaxSections = 4;

    public sealed record Section(Version Version, IReadOnlyList<string> Bullets);

    [GeneratedRegex(@"^##\s+(\d+\.\d+\.\d+)\s*$", RegexOptions.Multiline)]
    private static partial Regex Heading();

    /// <summary>Разбирает CHANGELOG: «## 1.2.3», под ним пункты «- …» (продолжение — с отступом).</summary>
    public static List<Section> Parse(string markdown)
    {
        var result = new List<Section>();
        var headings = Heading().Matches(markdown);
        for (int i = 0; i < headings.Count; i++)
        {
            int start = headings[i].Index + headings[i].Length;
            int end = i + 1 < headings.Count ? headings[i + 1].Index : markdown.Length;
            var bullets = new List<string>();
            foreach (var raw in markdown[start..end].Split('\n'))
            {
                var line = raw.TrimEnd('\r');
                if (line.StartsWith("- ")) bullets.Add(line[2..].Trim());
                else if (line.StartsWith("  ") && bullets.Count > 0 && line.Trim().Length > 0) bullets[^1] += " " + line.Trim();
            }
            result.Add(new Section(Version.Parse(headings[i].Groups[1].Value), bullets));
        }
        return result;
    }

    /// <summary>Разделы новее <paramref name="seen"/> и не новее <paramref name="current"/>, свежие первыми.</summary>
    public static List<Section> Between(IEnumerable<Section> sections, Version? seen, Version current) => sections
        .Where(s => (seen == null || s.Version > seen) && s.Version <= current)
        .OrderByDescending(s => s.Version)
        .Take(MaxSections)
        .ToList();

    public static List<Section> Load() => Parse(ReadChangelog(Loc.IsEnglish ? "CHANGELOG.en.md" : "CHANGELOG.md"));

    private static string ReadChangelog(string name)
    {
        try
        {
            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name);
            if (stream == null) return "";
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Changelog");
            return "";
        }
    }

    /// <summary>
    /// Что показать при этом запуске. Свежая установка — ничего; обновление с версии, которая ещё не
    /// запоминала увиденное, — только текущая версия.
    /// </summary>
    public static List<Section> Pending(string? lastSeen, bool freshInstall, Version current, IEnumerable<Section> sections)
    {
        if (lastSeen == null)
            return freshInstall ? [] : sections.Where(s => s.Version == current).ToList();
        return Version.TryParse(lastSeen, out var seen) ? Between(sections, seen, current) : [];
    }

    /// <summary>Разбивка «**жирного**» и «`кода`» для показа: (текст, жирный).</summary>
    public static IEnumerable<(string Text, bool Bold)> Runs(string text)
    {
        var parts = Regex.Split(text, @"(\*\*[^*]+\*\*)");
        foreach (var part in parts)
        {
            if (part.Length == 0) continue;
            if (part.StartsWith("**") && part.EndsWith("**")) yield return (part[2..^2], true);
            else yield return (part.Replace("`", ""), false);
        }
    }
}
