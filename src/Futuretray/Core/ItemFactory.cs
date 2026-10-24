using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Windows.Media;

namespace Futuretray.Core;

/// <summary>Установленное приложение из shell:AppsFolder (всё, что есть в меню «Пуск», включая Store).</summary>
public sealed class InstalledApp : Observable
{
    private ImageSource? _icon;
    private bool _isSelected;

    public InstalledApp(string name, string parsingName, string? filePath)
    {
        Name = name;
        ParsingName = parsingName;
        FilePath = filePath;
    }

    public string Name { get; }
    public string ParsingName { get; }
    public string? FilePath { get; }
    public string Subtitle => FilePath ?? Loc.T("Приложение Microsoft Store");
    public ImageSource? Icon { get => _icon; set => Set(ref _icon, value); }
    public bool IsSelected { get => _isSelected; set => Set(ref _isSelected, value); }
}

public static partial class ItemFactory
{
    public static LaunchItem FromPath(string path)
    {
        path = path.Trim().Trim('"');
        var ext = Path.GetExtension(path).ToLowerInvariant();

        if (ext == ".url" && File.Exists(path) && ReadInternetShortcut(path) is { } url)
            return new LaunchItem { Kind = ItemKind.Url, Target = url, Name = Path.GetFileNameWithoutExtension(path) };

        string name;
        if (Directory.Exists(path))
        {
            var root = Path.GetPathRoot(path);
            name = string.Equals(root?.TrimEnd('\\'), path.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)
                ? DriveName(path)
                : Path.GetFileName(path.TrimEnd('\\'));
        }
        else if (ext == ".exe")
        {
            name = FriendlyExeName(path) ?? Path.GetFileNameWithoutExtension(path);
        }
        else
        {
            name = Path.GetFileNameWithoutExtension(path);
        }

        return new LaunchItem { Kind = ItemKind.Path, Target = path, Name = string.IsNullOrWhiteSpace(name) ? path : name };
    }

    public static LaunchItem FromUrl(string url, string? name = null)
    {
        url = url.Trim();
        if (!Regex.IsMatch(url, @"^[a-z][a-z0-9+.\-]*:", RegexOptions.IgnoreCase)) url = "https://" + url;
        if (string.IsNullOrWhiteSpace(name))
        {
            name = Uri.TryCreate(url, UriKind.Absolute, out var uri) && !string.IsNullOrEmpty(uri.Host)
                ? Capitalize(uri.Host.StartsWith("www.") ? uri.Host[4..] : uri.Host)
                : url;
        }
        return new LaunchItem { Kind = ItemKind.Url, Target = url, Name = name.Trim() };
    }

    public static LaunchItem FromInstalled(InstalledApp app) => app.FilePath != null
        ? new LaunchItem { Kind = ItemKind.Path, Target = app.FilePath, Name = app.Name }
        : new LaunchItem { Kind = ItemKind.ShellApp, Target = app.ParsingName, Name = app.Name };

    public static bool LooksLikeUrl(string text) =>
        Regex.IsMatch(text.Trim(), @"^(https?|ftp|mailto|steam|discord|tg|spotify):", RegexOptions.IgnoreCase);

    /// <summary>Перечисляет приложения из shell:AppsFolder в отдельном STA-потоке.</summary>
    public static Task<List<InstalledApp>> EnumerateInstalledAsync()
    {
        var tcs = new TaskCompletionSource<List<InstalledApp>>();
        var thread = new Thread(() =>
        {
            try { tcs.SetResult(EnumerateInstalled()); }
            catch (Exception ex) { tcs.SetException(ex); }
        })
        { IsBackground = true, Name = "Apps enumeration" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return tcs.Task;
    }

    private static List<InstalledApp> EnumerateInstalled()
    {
        var result = new List<InstalledApp>();
        var type = Type.GetTypeFromProgID("Shell.Application");
        if (type == null) return result;
        dynamic shell = Activator.CreateInstance(type)!;
        dynamic folder = shell.NameSpace("shell:AppsFolder");
        if (folder == null) return result;

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (dynamic item in folder.Items())
        {
            try
            {
                string name = item.Name;
                string parsing = item.Path;
                if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(parsing)) continue;
                if (parsing.StartsWith("http", StringComparison.OrdinalIgnoreCase)) continue;
                if (!seen.Add(name + "|" + parsing)) continue;
                result.Add(new InstalledApp(name, parsing, ResolveFilePath(parsing)));
            }
            catch
            {
                // отдельные «кривые» элементы пропускаем
            }
        }
        return result.OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    /// <summary>"{GUID}\folder\app.exe" → реальный путь, если это обычная программа.</summary>
    private static string? ResolveFilePath(string parsing)
    {
        var match = Regex.Match(parsing, @"^\{([0-9A-Fa-f\-]{36})\}(\\.*)?$");
        string candidate = parsing;
        if (match.Success)
        {
            var root = Native.GetKnownFolderPath(Guid.Parse(match.Groups[1].Value));
            if (root == null) return null;
            candidate = root + match.Groups[2].Value;
        }
        if (!Path.IsPathRooted(candidate)) return null;
        return File.Exists(candidate) ? candidate : null;
    }

    private static string? FriendlyExeName(string path)
    {
        try
        {
            var info = FileVersionInfo.GetVersionInfo(path);
            var description = info.FileDescription?.Trim();
            if (!string.IsNullOrWhiteSpace(description) && description.Length <= 40 &&
                !description.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                return description;
            var product = info.ProductName?.Trim();
            if (!string.IsNullOrWhiteSpace(product) && product.Length <= 30) return product;
        }
        catch { }
        return null;
    }

    private static string DriveName(string path)
    {
        try
        {
            var drive = new DriveInfo(path);
            var letter = drive.Name.TrimEnd('\\');
            return string.IsNullOrWhiteSpace(drive.VolumeLabel) ? Loc.F("Диск {0}", letter) : $"{drive.VolumeLabel} ({letter})";
        }
        catch
        {
            return path;
        }
    }

    private static string? ReadInternetShortcut(string path)
    {
        try
        {
            return File.ReadLines(path)
                .FirstOrDefault(l => l.StartsWith("URL=", StringComparison.OrdinalIgnoreCase))?[4..].Trim();
        }
        catch
        {
            return null;
        }
    }

    private static string Capitalize(string s) => s.Length == 0 ? s : char.ToUpper(s[0]) + s[1..];
}
