using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;

namespace Futuretray.Core;

public static class ThemeManager
{
    private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private static ResourceDictionary? _appDictionary;
    private static readonly Dictionary<bool, ResourceDictionary> PopupDictionaries = new();
    private static AppSettings? _settings;

    public static Color AccentColor { get; private set; } = Color.FromRgb(0x00, 0x78, 0xD4);
    public static Color AccentLight { get; private set; } = Color.FromRgb(0x60, 0xCD, 0xFF);
    public static Color AccentDark { get; private set; } = Color.FromRgb(0x00, 0x5F, 0xB8);

    /// <summary>Тема окон Futuretray (редактор).</summary>
    public static bool AppDark { get; private set; }

    public static event Action? Changed;

    public static void Initialize(AppSettings settings)
    {
        _settings = settings;
        SystemEvents.UserPreferenceChanged += (_, e) =>
        {
            if (e.Category is UserPreferenceCategory.General or UserPreferenceCategory.Color or UserPreferenceCategory.VisualStyle)
                Application.Current?.Dispatcher.BeginInvoke(Apply);
        };
        settings.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(AppSettings.Theme)) Apply();
        };
        Apply();
    }

    public static void Apply()
    {
        var pref = _settings?.Theme ?? ThemePreference.System;
        AppDark = pref switch
        {
            ThemePreference.Dark => true,
            ThemePreference.Light => false,
            _ => ReadDword(PersonalizeKey, "AppsUseLightTheme", 1) == 0,
        };
        ReadAccent();
        PopupDictionaries.Clear();

        var merged = Application.Current.Resources.MergedDictionaries;
        var dict = CreateDictionary(AppDark);
        if (_appDictionary != null) merged.Remove(_appDictionary);
        else if (merged.Count > 0 && merged[0].Source?.OriginalString.Contains("Themes/Dark") == true) merged.RemoveAt(0);
        merged.Insert(0, dict);
        _appDictionary = dict;

        Changed?.Invoke();
    }

    /// <summary>Всплывающее окно — часть панели задач, поэтому по умолчанию следует теме системы (панели), а не приложений.</summary>
    public static bool PopupDark => (_settings?.Theme ?? ThemePreference.System) switch
    {
        ThemePreference.Dark => true,
        ThemePreference.Light => false,
        _ => ReadDword(PersonalizeKey, "SystemUsesLightTheme", 0) == 0,
    };

    public static ResourceDictionary PopupDictionary(bool dark)
    {
        if (!PopupDictionaries.TryGetValue(dark, out var dict))
            PopupDictionaries[dark] = dict = CreateDictionary(dark);
        return dict;
    }

    public static ResourceDictionary CreateDictionary(bool dark)
    {
        var dict = new ResourceDictionary
        {
            Source = new Uri($"pack://application:,,,/Futuretray;component/UI/Themes/{(dark ? "Dark" : "Light")}.xaml"),
        };
        var accent = dark ? AccentLight : AccentDark;
        dict["Color.Accent"] = accent;
        dict["Brush.Accent"] = ColorUtil.Brush(accent);
        dict["Brush.AccentHover"] = ColorUtil.Brush(ColorUtil.WithAlpha(accent, 0.9));
        dict["Brush.AccentPressed"] = ColorUtil.Brush(ColorUtil.WithAlpha(accent, 0.8));
        dict["Brush.AccentText"] = ColorUtil.Brush(dark ? Colors.Black : Colors.White);
        dict["Brush.AccentForeground"] = ColorUtil.Brush(dark ? AccentLight : AccentDark);
        dict["Brush.AccentSubtle"] = ColorUtil.Brush(ColorUtil.WithAlpha(accent, dark ? 0.16 : 0.12));
        return dict;
    }

    private static void ReadAccent()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Accent");
            if (key?.GetValue("AccentPalette") is byte[] p && p.Length >= 32)
            {
                Color At(int i) => Color.FromRgb(p[i * 4], p[i * 4 + 1], p[i * 4 + 2]);
                AccentLight = At(1);
                AccentColor = At(3);
                AccentDark = At(4);
                return;
            }
        }
        catch { }

        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\DWM");
            if (key?.GetValue("AccentColor") is int abgr)
            {
                AccentColor = Color.FromRgb((byte)abgr, (byte)(abgr >> 8), (byte)(abgr >> 16));
                AccentLight = ColorUtil.Lighten(AccentColor, 0.45);
                AccentDark = ColorUtil.Darken(AccentColor, 0.15);
            }
        }
        catch { }
    }

    private static int ReadDword(string path, string name, int fallback)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(path);
            return key?.GetValue(name) is int v ? v : fallback;
        }
        catch
        {
            return fallback;
        }
    }
}
