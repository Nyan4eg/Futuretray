namespace Futuretray.Core;

/// <summary>
/// Вызывается деинсталлятором (Futuretray.exe --uninstall): убирает то, что программа
/// создала сама. Данные пользователя удаляет сам деинсталлятор, если пользователь согласится.
/// </summary>
public static class Uninstaller
{
    public static void Cleanup()
    {
        AutoStart.Sync(false);

        var programs = Environment.GetFolderPath(Environment.SpecialFolder.Programs);
        TryDeleteDirectory(Path.Combine(programs, "Futuretray"));

        // Ярлыки групп на рабочем столе ведут на удаляемый exe — без него они бесполезны.
        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        foreach (var file in SafeFiles(desktop))
        {
            var info = ShellLink.Read(file);
            if (info != null && info.Arguments.Contains("--group", StringComparison.OrdinalIgnoreCase) &&
                Path.GetFileName(info.Target).Equals(Path.GetFileName(ShortcutManager.ExePath), StringComparison.OrdinalIgnoreCase))
                TryDelete(file);
        }
    }

    private static IEnumerable<string> SafeFiles(string dir)
    {
        try { return Directory.Exists(dir) ? Directory.GetFiles(dir, "*.lnk") : Array.Empty<string>(); }
        catch { return Array.Empty<string>(); }
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); } catch { }
    }

    private static void TryDeleteDirectory(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, true); } catch { }
    }
}
