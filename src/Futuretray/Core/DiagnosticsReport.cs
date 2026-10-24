using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

namespace Futuretray.Core;

/// <summary>
/// «Собрать отчёт»: zip на рабочем столе с журналом ошибок, настройками групп и сведениями о системе.
/// Пользователь отправляет его разработчику, если что-то пошло не так.
/// </summary>
public static class DiagnosticsReport
{
    public static string Create(ConfigStore store, string? directory = null)
    {
        store.SaveNow();
        directory ??= Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        var path = Path.Combine(directory, $"Futuretray-отчёт-{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.zip");

        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        AddFile(zip, Log.FilePath, "futuretray.log");
        AddFile(zip, ConfigStore.ConfigPath, "config.json");
        AddText(zip, "system.txt", SystemInfo(store));
        return path;
    }

    private static void AddFile(ZipArchive zip, string source, string name)
    {
        try
        {
            if (!File.Exists(source)) return;
            // Журнал может дописываться прямо сейчас — открываем без блокировки.
            using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var output = zip.CreateEntry(name).Open();
            input.CopyTo(output);
        }
        catch (Exception ex)
        {
            AddText(zip, name + ".error.txt", ex.ToString());
        }
    }

    private static void AddText(ZipArchive zip, string name, string text)
    {
        using var writer = new StreamWriter(zip.CreateEntry(name).Open(), new UTF8Encoding(true));
        writer.Write(text);
    }

    private static string SystemInfo(ConfigStore store)
    {
        var sb = new StringBuilder();
        void Line(string label, object? value) => sb.Append(label).Append(": ").AppendLine(value?.ToString());
        void Safe(string label, Func<object?> value)
        {
            try { Line(label, value()); }
            catch (Exception ex) { Line(label, "ошибка: " + ex.Message); }
        }

        sb.AppendLine("== Futuretray ==");
        Line("Версия", Updater.CurrentVersionText);
        Line("Путь", ShortcutManager.ExePath);
        Line("Отчёт создан", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss zzz"));
        Line(".NET", RuntimeInformation.FrameworkDescription);

        sb.AppendLine().AppendLine("== Windows ==");
        // В реестре Windows 11 до сих пор называет себя «Windows 10» — поправляем по номеру сборки.
        Safe("Выпуск", () => WindowEffects.Build >= 22000
            ? NtValue("ProductName")?.ToString()?.Replace("Windows 10", "Windows 11")
            : NtValue("ProductName"));
        Safe("Версия", () => $"{NtValue("DisplayVersion")} (сборка {NtValue("CurrentBuild")}.{NtValue("UBR")})");
        Line("Язык", System.Globalization.CultureInfo.CurrentUICulture.Name);
        Line("Mica/Acrylic поддерживаются", WindowEffects.SupportsBackdrop);
        Safe("Тема окон / панели задач", () => $"{(ThemeManager.AppDark ? "тёмная" : "светлая")} / {(ThemeManager.PopupDark ? "тёмная" : "светлая")}");
        Safe("Цвет акцента", () => ColorUtil.ToHex(ThemeManager.AccentColor));
        Safe("Твикеры панели задач", ShellMods);

        sb.AppendLine().AppendLine("== Мониторы ==");
        try
        {
            foreach (var m in Monitors.All())
            {
                var center = new Native.POINT((m.Bounds.Left + m.Bounds.Right) / 2, (m.Bounds.Top + m.Bounds.Bottom) / 2);
                var placement = TaskbarInfo.Locate(center);
                sb.AppendLine($"{m.Device}{(m.IsPrimary ? " (основной)" : "")}: экран {R(m.Bounds)}, рабочая область {R(m.Work)}, " +
                              $"{m.Dpi * 100 / 96}%, панель задач: {placement.Edge}");
            }
        }
        catch (Exception ex)
        {
            sb.AppendLine("ошибка: " + ex.Message);
        }

        sb.AppendLine().AppendLine("== Настройки ==");
        var settings = store.Config.Settings;
        Line("Тема", settings.Theme);
        Line("Автозапуск", settings.RunAtStartup);
        Safe("Запись автозапуска", () => Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run")?.GetValue("Futuretray"));
        Line("Значок в трее", settings.ShowTrayIcon);
        Line("Проверка обновлений", settings.CheckUpdates);
        Line("Последняя проверка", settings.LastUpdateCheck);

        sb.AppendLine().AppendLine("== Группы ==");
        foreach (var g in store.Config.Groups)
        {
            bool pinned = false;
            try { pinned = App.Host.Shortcuts.IsPinned(g); } catch { }
            sb.AppendLine($"«{g.Name}» [{g.Id}]: {g.Items.Count} эл., закреплена: {pinned}, " +
                          $"трей: {(g.DockEnabled ? $"{g.DockEdge}/{g.DockAlign}, поверх={g.DockTopmost}, автоскрытие={g.DockAutoHide}, резерв={g.DockReserveSpace}" : "нет")}");
            foreach (var item in g.Items)
            {
                bool exists = item.Kind != ItemKind.Path ||
                              File.Exists(Environment.ExpandEnvironmentVariables(item.Target)) ||
                              Directory.Exists(Environment.ExpandEnvironmentVariables(item.Target));
                sb.AppendLine($"    {item.Kind}: {item.Target}{(exists ? "" : "  ← НЕ НАЙДЕНО")}");
            }
        }
        return sb.ToString();
    }

    private static string R(Native.RECT r) => $"{r.Left},{r.Top} {r.Width}×{r.Height}";

    private static object? NtValue(string name) =>
        Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion")?.GetValue(name);

    /// <summary>Программы, которые меняют панель задач (из-за них она может стоять сбоку в Windows 11).</summary>
    private static string ShellMods()
    {
        var found = new List<string>();
        var pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        if (Directory.Exists(Path.Combine(pf, "ExplorerPatcher"))) found.Add("ExplorerPatcher");
        if (Registry.CurrentUser.OpenSubKey(@"Software\StartIsBack") != null) found.Add("StartAllBack/StartIsBack");
        if (Directory.Exists(Path.Combine(pf, "Windhawk"))) found.Add("Windhawk");
        if (Directory.Exists(Path.Combine(pf, "Stardock", "Start11"))) found.Add("Start11");
        return found.Count == 0 ? "не найдены" : string.Join(", ", found);
    }
}
