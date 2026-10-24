using System.Text;

namespace Futuretray.Core;

/// <summary>
/// Приложения из меню «Пуск» для поиска: перечисляются в фоне при запуске и обновляются,
/// когда окно поиска открывают после долгого перерыва. Деинсталляторы и справки отброшены.
/// </summary>
public static class AppIndex
{
    private static readonly TimeSpan MaxAge = TimeSpan.FromMinutes(5);
    private static DateTime _loadedAt = DateTime.MinValue;
    private static Task? _loading;

    public static IReadOnlyList<InstalledApp> Apps { get; private set; } = [];

    /// <summary>Список приложений обновился — открытый поиск пересчитывает выдачу.</summary>
    public static event Action? Updated;

    public static void RefreshIfStale()
    {
        if (_loading != null || DateTime.Now - _loadedAt < MaxAge) return;
        _ = RefreshAsync();
    }

    /// <summary>Перечитать список; если он уже читается — дождаться той же загрузки.</summary>
    public static Task RefreshAsync() => _loading ??= LoadAsync();

    private static async Task LoadAsync()
    {
        try
        {
            var apps = await ItemFactory.EnumerateInstalledAsync();
            Apps = apps.Where(a => !IsJunk(a.Name, a.FilePath)).ToList();
            _loadedAt = DateTime.Now;
            // Значки — заранее и в фоне, чтобы выдача не подтормаживала на первом наборе.
            IconService.Current?.Prewarm(Apps.Select(ItemFactory.FromInstalled));
            Updated?.Invoke();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "App index");
        }
        finally
        {
            _loading = null;
        }
    }

    private static readonly string[] JunkWords =
    [
        "uninstall", "деинсталл", "удалить", "удаление", "readme", "release notes", "license", "лицензи", "help", "справка",
    ];

    private static readonly HashSet<string> RunnableExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".msc", ".cpl", ".bat", ".cmd", ".lnk", ".appref-ms",
    };

    /// <summary>Не программа, а её деинсталлятор, справка или документ — в поиске не нужны.</summary>
    public static bool IsJunk(string name, string? filePath)
    {
        if (JunkWords.Any(w => name.Contains(w, StringComparison.CurrentCultureIgnoreCase))) return true;
        if (filePath == null) return false; // приложение Store
        var ext = Path.GetExtension(filePath);
        return ext.Length > 0 && !RunnableExtensions.Contains(ext);
    }

    public static string WindowTitle(IntPtr hwnd)
    {
        int length = Native.GetWindowTextLength(hwnd);
        if (length <= 0) return "";
        var sb = new StringBuilder(length + 1);
        Native.GetWindowText(hwnd, sb, sb.Capacity);
        return sb.ToString();
    }

    private static readonly Dictionary<string, string> AppNames = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Человеческое имя программы по exe: «Telegram Desktop», а не «Telegram.exe».</summary>
    public static string AppName(string? exePath)
    {
        if (string.IsNullOrEmpty(exePath)) return "";
        if (!AppNames.TryGetValue(exePath, out var name))
            AppNames[exePath] = name = ItemFactory.FriendlyExeName(exePath) ?? Path.GetFileNameWithoutExtension(exePath);
        return name;
    }
}
