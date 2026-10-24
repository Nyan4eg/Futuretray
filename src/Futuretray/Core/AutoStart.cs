using Microsoft.Win32;

namespace Futuretray.Core;

public static class AutoStart
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "Futuretray";

    public static void Sync(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            if (enabled) key.SetValue(ValueName, $"\"{ShortcutManager.ExePath}\" --startup");
            else if (key.GetValue(ValueName) != null) key.DeleteValue(ValueName, false);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Autostart");
        }
    }
}
