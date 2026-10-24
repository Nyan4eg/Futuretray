using System.Diagnostics;
using System.Windows.Threading;

namespace Futuretray.Core;

/// <summary>
/// Ярлыки групп. Ярлык запускает Futuretray.exe --group &lt;id&gt; и несёт уникальный
/// AppUserModelID, поэтому каждая группа закрепляется на панели задач отдельно.
/// После любых правок группы обновляются и уже закреплённые копии — перезакреплять не нужно.
/// </summary>
public sealed class ShortcutManager
{
    private readonly Dictionary<string, DispatcherTimer> _pending = new();

    public static string ShortcutsDir => Path.Combine(ConfigStore.DataDir, "Shortcuts");
    public static string IconsDir => Path.Combine(ConfigStore.DataDir, "Icons");
    public static string ExePath => AppPaths.ExePath;

    /// <summary>
    /// Что запускают значки групп и пункты списка переходов: крошечный нативный FuturetrayLauncher.exe
    /// рядом с программой (передаёт команду работающему Futuretray втрое быстрее), а без него — сам Futuretray.
    /// </summary>
    public static string LauncherPath =>
        Path.Combine(AppPaths.ExeDir, "FuturetrayLauncher.exe") is var launcher && File.Exists(launcher) ? launcher : ExePath;

    private static string PinnedDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        @"Microsoft\Internet Explorer\Quick Launch\User Pinned\TaskBar");
    private static string StartMenuDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "Futuretray");
    private static string DesktopDir => Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);

    public static string GroupArguments(Group g) => $"--group {g.Id}";
    public static string AppUserModelId(Group g) => $"Futuretray.Group.{g.Id}";

    public string ShortcutPath(Group g) => Path.Combine(ShortcutsDir, FileName(g));

    public void Schedule(Group g)
    {
        if (!_pending.TryGetValue(g.Id, out var timer))
        {
            timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(700) };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                _pending.Remove(g.Id);
                if (App.Host.Store.Find(g.Id) != null) Update(g);
            };
            _pending[g.Id] = timer;
        }
        timer.Stop();
        timer.Start();
    }

    /// <summary>Создаёт/обновляет ярлык группы и все его копии. Возвращает путь к ярлыку.</summary>
    public string Update(Group g)
    {
        var path = ShortcutPath(g);
        try
        {
            Directory.CreateDirectory(ShortcutsDir);
            var icon = EnsureIcon(g);

            foreach (var file in SafeEnumerate(ShortcutsDir))
                if (!string.Equals(file, path, StringComparison.OrdinalIgnoreCase) && BelongsTo(file, g.Id))
                    TryDelete(file);

            Write(path, g, icon);
            JumpLists.Update(g, AppUserModelId(g));

            // Копии в «Пуске» и на рабочем столе — переписываем целиком (с переименованием).
            foreach (var dir in new[] { StartMenuDir, DesktopDir })
            {
                foreach (var file in SafeEnumerate(dir).Where(f => BelongsTo(f, g.Id)).ToList())
                {
                    var wanted = Path.Combine(dir, FileName(g));
                    if (!string.Equals(file, wanted, StringComparison.OrdinalIgnoreCase)) TryDelete(file);
                    Write(wanted, g, icon);
                    Native.NotifyPathUpdated(wanted);
                }
            }

            // Закреплённые на панели задач копии — обновляем на месте, иначе слетит закрепление.
            bool pinnedChanged = false;
            foreach (var file in SafeEnumerate(PinnedDir))
            {
                var info = ShellLink.Read(file);
                if (info == null || !IsGroupArgs(info.Arguments, g.Id)) continue;
                if (string.Equals(info.IconPath, icon, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(info.Target, LauncherPath, StringComparison.OrdinalIgnoreCase)) continue;
                if (ShellLink.Update(file, LauncherPath, AppPaths.ExeDir, icon, AppUserModelId(g)))
                {
                    Native.NotifyPathUpdated(file);
                    pinnedChanged = true;
                }
            }
            if (pinnedChanged) Native.NotifyAssociationsChanged();

            CleanupIcons(g, icon);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Shortcut update " + g.Name);
        }
        return path;
    }

    public void Remove(Group g)
    {
        JumpLists.Delete(AppUserModelId(g));
        foreach (var dir in new[] { ShortcutsDir, StartMenuDir, DesktopDir })
            foreach (var file in SafeEnumerate(dir).Where(f => BelongsTo(f, g.Id)).ToList())
                TryDelete(file);
    }

    public string AddToStartMenu(Group g)
    {
        Directory.CreateDirectory(StartMenuDir);
        var path = Path.Combine(StartMenuDir, FileName(g));
        Write(path, g, EnsureIcon(g));
        return path;
    }

    public string AddToDesktop(Group g)
    {
        var path = Path.Combine(DesktopDir, FileName(g));
        Write(path, g, EnsureIcon(g));
        return path;
    }

    public bool IsPinned(Group g) =>
        SafeEnumerate(PinnedDir).Any(f => BelongsTo(f, g.Id));

    public void Reveal(Group g)
    {
        var path = Update(g);
        Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
    }

    /// <summary>При старте: чиним ярлыки (например, если exe перенесли) и убираем ярлыки удалённых групп.</summary>
    public void RepairAll(IReadOnlyCollection<Group> groups)
    {
        var ids = groups.Select(g => g.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var file in SafeEnumerate(ShortcutsDir))
        {
            var info = ShellLink.Read(file);
            var id = info == null ? null : GroupIdFromArgs(info.Arguments);
            if (id == null || !ids.Contains(id)) TryDelete(file);
        }
        foreach (var g in groups) Update(g);
        JumpLists.CleanupIcons(groups);
    }

    /// <summary>Ярлык самого Futuretray в меню «Пуск», чтобы программу было легко найти.</summary>
    public void EnsureAppShortcut()
    {
        // Ярлык «Futuretray» в «Пуске» — у установленной версии; портативная копия его не перехватывает.
        if (AppPaths.Portable) return;
        try
        {
            var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "Futuretray.lnk");
            var info = File.Exists(path) ? ShellLink.Read(path) : null;
            if (info != null && string.Equals(info.Target, ExePath, StringComparison.OrdinalIgnoreCase)) return;
            ShellLink.Save(path, ExePath, "", Path.GetDirectoryName(ExePath), ExePath,
                Loc.T("Группы приложений на панели задач"), "Futuretray.App");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "App shortcut");
        }
    }

    private void Write(string path, Group g, string icon) =>
        ShellLink.Save(path, LauncherPath, GroupArguments(g), AppPaths.ExeDir, icon,
            $"Futuretray: {g.Name}", AppUserModelId(g));

    private string EnsureIcon(Group g)
    {
        var path = Path.Combine(IconsDir, $"{g.Id}-{GroupIconRenderer.Hash(g)}.ico");
        if (!File.Exists(path)) GroupIconRenderer.WriteIco(g, path);
        return path;
    }

    private static void CleanupIcons(Group g, string current)
    {
        foreach (var file in SafeEnumerate(IconsDir, $"{g.Id}-*.ico"))
            if (!string.Equals(file, current, StringComparison.OrdinalIgnoreCase))
                TryDelete(file);
    }

    private string FileName(Group g)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var name = new string((g.Name ?? "").Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim().TrimEnd('.');
        if (string.IsNullOrWhiteSpace(name)) name = Loc.T("Группа");

        // Одинаковые имена у разных групп не должны перетирать ярлыки друг друга.
        var groups = App.Host.Store.Config.Groups;
        var sameName = groups.Where(x => string.Equals(x.Name?.Trim(), g.Name?.Trim(), StringComparison.OrdinalIgnoreCase)).ToList();
        int index = sameName.IndexOf(g);
        if (index > 0) name += $" ({index + 1})";
        return name + ".lnk";
    }

    private static bool BelongsTo(string lnkPath, string groupId)
    {
        if (!lnkPath.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase)) return false;
        var info = ShellLink.Read(lnkPath);
        return info != null && IsGroupArgs(info.Arguments, groupId);
    }

    private static bool IsGroupArgs(string args, string groupId) =>
        string.Equals(GroupIdFromArgs(args), groupId, StringComparison.OrdinalIgnoreCase);

    private static string? GroupIdFromArgs(string args)
    {
        var parts = args.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var i = Array.FindIndex(parts, p => p.Equals("--group", StringComparison.OrdinalIgnoreCase));
        return i >= 0 && i + 1 < parts.Length ? parts[i + 1].Trim('"') : null;
    }

    private static IEnumerable<string> SafeEnumerate(string dir, string pattern = "*.lnk")
    {
        try
        {
            return Directory.Exists(dir) ? Directory.GetFiles(dir, pattern) : Array.Empty<string>();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); } catch { }
    }
}
