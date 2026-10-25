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
            WindowItems.Clear(); // сопоставления окон с «Пуском» могли устареть
            _loadedAt = DateTime.Now;
            // Значки здесь не грузим: все сразу — это ~75 МБ памяти навсегда. Поиск достаёт их по мере показа.
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

    /// <summary>Список уже прочитан — иначе прочитать (для разовых действий вроде «группы из окон»).</summary>
    public static Task EnsureLoadedAsync() => _loadedAt == DateTime.MinValue ? RefreshAsync() : Task.CompletedTask;

    private static readonly Dictionary<string, LaunchItem?> WindowItems = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Программа окна как элемент группы. Лучше всего — та же запись, что в меню «Пуск»: у неё человеческое
    /// название и ярлык, который не сломается после обновления программы (у Discord exe лежит в папке
    /// с номером версии). Запись ищем по тем же правилам, по которым узнаём запущенные программы.
    /// Нет в «Пуске» — обычная программа по exe, приложение Store и окна ApplicationFrameHost — по AppUserModelID.
    /// </summary>
    public static LaunchItem? ItemForWindow(AppWindow w, RunningApps running)
    {
        var appId = w.AppId ?? w.ProcessAppId;
        bool hosted = w.Path == null || w.Path.Contains(@"\WindowsApps\", StringComparison.OrdinalIgnoreCase) ||
                      w.Path.EndsWith(@"\ApplicationFrameHost.exe", StringComparison.OrdinalIgnoreCase);
        if (hosted && appId == null) return null;
        var key = hosted ? "app|" + appId : "exe|" + w.Path + "|" + appId;
        if (WindowItems.TryGetValue(key, out var cached)) return cached;

        LaunchItem? item = null;
        if (!hosted)
            item = SquirrelItem(w.Path!) ?? Apps.Where(a => string.Equals(a.FilePath, w.Path, StringComparison.OrdinalIgnoreCase))
                .Select(ItemFactory.FromInstalled).FirstOrDefault();
        item ??= Apps.Select(ItemFactory.FromInstalled).FirstOrDefault(i => running.WindowsFor(i).Contains(w.Handle));
        item ??= hosted
            ? new LaunchItem { Kind = ItemKind.ShellApp, Target = appId!, Name = AppName(w.Path) }
            : ItemFactory.FromPath(w.Path!);
        return WindowItems[key] = item;
    }

    /// <summary>
    /// Программы на Squirrel (Discord, Element, Slack) работают из папки app-&lt;версия&gt;, которая меняется
    /// с каждым обновлением. Запускаем их так же, как их собственный ярлык: Update.exe --processStart.
    /// </summary>
    public static LaunchItem? SquirrelItem(string exePath)
    {
        var versionDir = Path.GetDirectoryName(exePath);
        var root = versionDir == null ? null : Path.GetDirectoryName(versionDir);
        if (root == null || !Path.GetFileName(versionDir)!.StartsWith("app-", StringComparison.OrdinalIgnoreCase)) return null;
        var update = Path.Combine(root, "Update.exe");
        if (!File.Exists(update)) return null;
        return new LaunchItem
        {
            Kind = ItemKind.Path,
            Target = update,
            Arguments = $"--processStart \"{Path.GetFileName(exePath)}\"",
            Name = AppName(exePath),
        };
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
