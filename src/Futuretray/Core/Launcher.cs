using System.ComponentModel;
using System.Diagnostics;

namespace Futuretray.Core;

public static class Launcher
{
    /// <returns>null при успехе, иначе текст ошибки для пользователя.</returns>
    public static string? Launch(LaunchItem item, bool forceAdmin = false)
    {
        try
        {
            // Запуск может идти из неактивного окна (трея) — разрешаем новой программе выйти на передний план.
            Native.AllowSetForegroundWindow(Native.ASFW_ANY);
            var psi = new ProcessStartInfo { UseShellExecute = true };
            switch (item.Kind)
            {
                case ItemKind.Url:
                    psi.FileName = item.Target;
                    break;
                case ItemKind.ShellApp:
                    psi.FileName = "explorer.exe";
                    psi.Arguments = @"shell:AppsFolder\" + item.Target;
                    break;
                default:
                    var target = Environment.ExpandEnvironmentVariables(item.Target);
                    if (!File.Exists(target) && !Directory.Exists(target))
                        return $"Не найдено: {target}";
                    psi.FileName = target;
                    psi.Arguments = item.Arguments ?? "";
                    psi.WorkingDirectory = item.WorkingDirectory is { } wd
                        ? Environment.ExpandEnvironmentVariables(wd)
                        : File.Exists(target) ? Path.GetDirectoryName(target) ?? "" : "";
                    if (forceAdmin || item.RunAsAdmin) psi.Verb = "runas";
                    break;
            }

            if (psi.Verb == "runas")
            {
                // UAC-запрос блокирует вызов — не держим интерфейс.
                var thread = new Thread(() => StartQuietly(psi)) { IsBackground = true };
                thread.SetApartmentState(ApartmentState.STA);
                thread.Start();
                return null;
            }

            Process.Start(psi)?.Dispose();
            return null;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            return null; // пользователь отменил UAC
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Launch " + item.Target);
            return ex.Message;
        }
    }

    private static void StartQuietly(ProcessStartInfo psi)
    {
        try { Process.Start(psi)?.Dispose(); }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223) { }
        catch (Exception ex) { Log.Error(ex, "Launch (admin) " + psi.FileName); }
    }

    public static void OpenLocation(LaunchItem item)
    {
        try
        {
            if (item.Kind == ItemKind.ShellApp)
            {
                Process.Start(new ProcessStartInfo("explorer.exe", @"shell:AppsFolder") { UseShellExecute = true });
                return;
            }
            if (item.Kind == ItemKind.Url) return;

            var path = Environment.ExpandEnvironmentVariables(item.Target);
            if (path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) && ShellLink.Read(path) is { } info &&
                (File.Exists(info.Target) || Directory.Exists(info.Target)))
                path = info.Target;

            if (File.Exists(path) || Directory.Exists(path))
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
            else if (Path.GetDirectoryName(path) is { } dir && Directory.Exists(dir))
                Process.Start(new ProcessStartInfo("explorer.exe", $"\"{dir}\"") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Open location");
        }
    }

    public static void OpenFolder(string path)
    {
        try
        {
            Directory.CreateDirectory(path);
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Open folder");
        }
    }
}
