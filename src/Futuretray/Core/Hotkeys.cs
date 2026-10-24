using System.Runtime.InteropServices;
using System.Windows.Input;
using System.Windows.Interop;

namespace Futuretray.Core;

/// <summary>Сочетание клавиш: хранится строкой вида "Ctrl+Alt+Space".</summary>
public readonly record struct Hotkey(ModifierKeys Modifiers, Key Key)
{
    public static bool TryParse(string? text, out Hotkey hotkey)
    {
        hotkey = default;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var modifiers = ModifierKeys.None;
        Key key = Key.None;
        foreach (var part in text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            switch (part.ToLowerInvariant())
            {
                case "ctrl" or "control": modifiers |= ModifierKeys.Control; break;
                case "alt": modifiers |= ModifierKeys.Alt; break;
                case "shift": modifiers |= ModifierKeys.Shift; break;
                case "win" or "windows": modifiers |= ModifierKeys.Windows; break;
                default:
                    if (!Enum.TryParse(part, true, out key)) return false;
                    break;
            }
        }
        if (key == Key.None) return false;
        hotkey = new Hotkey(modifiers, key);
        return true;
    }

    public override string ToString()
    {
        // Порядок как в подсказках Windows: Win + Ctrl + Alt + Shift.
        var parts = new List<string>();
        if (Modifiers.HasFlag(ModifierKeys.Windows)) parts.Add("Win");
        if (Modifiers.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (Modifiers.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        if (Modifiers.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        parts.Add(Key.ToString());
        return string.Join("+", parts);
    }

    /// <summary>Для показа пользователю: «Ctrl + Alt + Пробел».</summary>
    public string Display
    {
        get
        {
            var parts = ToString().Split('+').ToList();
            parts[^1] = KeyName(Key);
            return string.Join(" + ", parts);
        }
    }

    public static string KeyName(Key key) => key switch
    {
        >= Key.D0 and <= Key.D9 => ((int)(key - Key.D0)).ToString(),
        >= Key.NumPad0 and <= Key.NumPad9 => "Num " + (int)(key - Key.NumPad0),
        Key.Space => Loc.T("Пробел"),
        Key.Return => "Enter",
        Key.Escape => "Esc",
        Key.Back => "Backspace",
        Key.OemTilde => "`",
        Key.OemMinus => "-",
        Key.OemPlus => "=",
        Key.OemComma => ",",
        Key.OemPeriod => ".",
        Key.Oem2 => "/",
        Key.Oem5 => "\\",
        Key.OemOpenBrackets => "[",
        Key.Oem6 => "]",
        Key.Oem1 => ";",
        Key.OemQuotes => "'",
        _ => key.ToString(),
    };

    public static bool IsModifierKey(Key key) => key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
        or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin;
}

/// <summary>Глобальные сочетания клавиш (RegisterHotKey) для групп и поиска.</summary>
public sealed class HotkeyManager : IDisposable
{
    private const int WM_HOTKEY = 0x0312;
    private const uint MOD_NOREPEAT = 0x4000;

    private readonly HwndSource _window;
    private readonly Dictionary<int, Action> _actions = new();
    private List<(string Owner, string? Text, Action Action)> _bindings = new();
    private bool _suspended;

    public HotkeyManager()
    {
        _window = new HwndSource(new HwndSourceParameters("FuturetrayHotkeys") { Width = 0, Height = 0, WindowStyle = 0 });
        _window.AddHook(WndProc);
    }

    /// <summary>Чьи сочетания не удалось занять: владелец ("search" или id группы) → причина.</summary>
    public IReadOnlyDictionary<string, string> Failures { get; private set; } = new Dictionary<string, string>();

    public event Action? FailuresChanged;

    public void Apply(IEnumerable<(string Owner, string? Text, Action Action)> bindings)
    {
        _bindings = bindings.ToList();
        Register();
    }

    /// <summary>Пока пользователь записывает новое сочетание, свои глобальные не перехватываем.</summary>
    public bool Suspended
    {
        get => _suspended;
        set
        {
            if (_suspended == value) return;
            _suspended = value;
            if (value) UnregisterAll();
            else Register();
        }
    }

    private void Register()
    {
        UnregisterAll();
        if (_suspended) return;

        var failures = new Dictionary<string, string>();
        var used = new Dictionary<Hotkey, string>();
        int id = 1;
        foreach (var (owner, text, action) in _bindings)
        {
            if (text == null) continue;
            if (!Hotkey.TryParse(text, out var hotkey))
            {
                failures[owner] = Loc.T("Не удалось разобрать сочетание");
                continue;
            }
            if (used.ContainsKey(hotkey))
            {
                failures[owner] = Loc.T("Это сочетание уже назначено другой группе");
                continue;
            }

            uint mods = MOD_NOREPEAT;
            if (hotkey.Modifiers.HasFlag(ModifierKeys.Alt)) mods |= 0x1;
            if (hotkey.Modifiers.HasFlag(ModifierKeys.Control)) mods |= 0x2;
            if (hotkey.Modifiers.HasFlag(ModifierKeys.Shift)) mods |= 0x4;
            if (hotkey.Modifiers.HasFlag(ModifierKeys.Windows)) mods |= 0x8;
            uint vk = (uint)KeyInterop.VirtualKeyFromKey(hotkey.Key);

            if (RegisterHotKey(_window.Handle, id, mods, vk))
            {
                _actions[id] = action;
                used[hotkey] = owner;
            }
            else
            {
                failures[owner] = Loc.T("Сочетание уже занято Windows или другой программой — выберите другое");
            }
            id++;
        }

        Failures = failures;
        foreach (var (owner, reason) in failures) Log.Info($"Hotkey {owner}: {reason}");
        FailuresChanged?.Invoke();
    }

    private void UnregisterAll()
    {
        foreach (var id in _actions.Keys) UnregisterHotKey(_window.Handle, id);
        _actions.Clear();
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY && _actions.TryGetValue((int)wParam, out var action))
        {
            handled = true;
            try { action(); }
            catch (Exception ex) { Log.Error(ex, "Hotkey"); }
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        UnregisterAll();
        _window.Dispose();
    }

    [DllImport("user32.dll")] private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint modifiers, uint vk);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}
