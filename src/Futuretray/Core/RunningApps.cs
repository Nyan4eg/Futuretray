using System.Windows.Threading;

namespace Futuretray.Core;

public sealed record AppWindow(IntPtr Handle, uint ProcessId, string? Path, string? AppId, string? ProcessAppId);

/// <summary>
/// Какие программы из групп сейчас открыты. Список окон обновляется по системным событиям
/// (окно появилось/закрылось/сменился активный) и раз в несколько секунд для надёжности.
/// </summary>
public sealed class RunningApps
{
    internal sealed record MatchSpec(string? ExePath, string? ExeName, string? ExeDir, string? StubName, string? AppId);

    private readonly List<Native.WinEventProc> _callbacks = new(); // держим ссылки, иначе GC соберёт делегаты
    private readonly List<IntPtr> _hooks = new();
    private readonly DispatcherTimer _debounce = new() { Interval = TimeSpan.FromMilliseconds(200) };
    private readonly DispatcherTimer _periodic = new() { Interval = TimeSpan.FromSeconds(3) };
    private readonly Dictionary<uint, (string? Path, string? AppId)> _processes = new();
    private readonly Dictionary<string, MatchSpec?> _specs = new();
    private readonly uint _ownPid = (uint)Environment.ProcessId;
    private List<AppWindow> _windows = new();
    private string _signature = "";

    public RunningApps()
    {
        Current = this;
        _debounce.Tick += (_, _) =>
        {
            _debounce.Stop();
            Refresh();
        };
        _periodic.Tick += (_, _) => Refresh();

        Hook(Native.EVENT_SYSTEM_FOREGROUND, Native.EVENT_SYSTEM_FOREGROUND);
        Hook(Native.EVENT_OBJECT_DESTROY, Native.EVENT_OBJECT_HIDE);
        Hook(Native.EVENT_OBJECT_CLOAKED, Native.EVENT_OBJECT_UNCLOAKED);

        var foreground = Native.GetForegroundWindow();
        if (OwnerPid(foreground) != _ownPid) LastExternalForeground = foreground;
        Refresh();
        _periodic.Start();
    }

    public static RunningApps? Current { get; private set; }

    /// <summary>Последнее активное окно чужой программы (до того, как открыли окно группы).</summary>
    public IntPtr LastExternalForeground { get; private set; }

    public IReadOnlyList<AppWindow> Windows => _windows;

    /// <summary>Набор окон или активное окно изменились.</summary>
    public event Action? Changed;

    public IReadOnlyList<IntPtr> WindowsFor(LaunchItem item)
    {
        var spec = SpecFor(item);
        if (spec == null) return Array.Empty<IntPtr>();
        return _windows.Where(w => Matches(w, spec)).Select(w => w.Handle).ToList();
    }

    public bool IsRunning(LaunchItem item) => WindowsFor(item).Count > 0;

    /// <summary>Узнаём ли мы окна этого элемента вообще (у ссылок, папок и документов — нет).</summary>
    public bool CanTrack(LaunchItem item) => SpecFor(item) != null;

    public bool IsActive(LaunchItem item) =>
        LastExternalForeground != IntPtr.Zero && WindowsFor(item).Contains(LastExternalForeground);

    /// <summary>
    /// Как клик по значку на панели задач: показать окно программы, свернуть его, если оно уже активно,
    /// или перебрать окна по кругу, если их несколько.
    /// </summary>
    /// <returns>false — программа не запущена.</returns>
    public bool Switch(LaunchItem item)
    {
        var windows = WindowsFor(item);
        if (windows.Count == 0) return false;

        var foreground = LastExternalForeground;
        if (windows.Count == 1)
        {
            var window = windows[0];
            if (window == foreground && !Native.IsIconic(window)) Native.ShowWindowAsync(window, Native.SW_MINIMIZE);
            else Activate(window);
        }
        else if (!windows.Contains(foreground))
        {
            Activate(windows[0]); // верхнее по Z — с которым работали последним
        }
        else
        {
            // По кругу в постоянном порядке: порядок Z меняется при каждом переключении,
            // и по нему мы бы бегали только между двумя верхними окнами.
            var ordered = windows.OrderBy(w => w.ToInt64()).ToList();
            Activate(ordered[(ordered.IndexOf(foreground) + 1) % ordered.Count]);
        }
        _debounce.Stop();
        _debounce.Start();
        return true;
    }

    public void CloseWindows(LaunchItem item)
    {
        foreach (var window in WindowsFor(item))
            Native.PostMessage(window, Native.WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
    }

    public static void Activate(IntPtr window)
    {
        // Асинхронно: зависшая программа не должна подвесить Futuretray.
        if (Native.IsIconic(window)) Native.ShowWindowAsync(window, Native.SW_RESTORE);
        if (Native.IsHungAppWindow(window)) return;
        if (Native.SetForegroundWindow(window)) return;

        // Обход защиты от «кражи фокуса»: временно подключаемся к вводу активного окна.
        var foreground = Native.GetForegroundWindow();
        uint foregroundThread = Native.GetWindowThreadProcessId(foreground, out _);
        uint currentThread = Native.GetCurrentThreadId();
        if (foregroundThread != currentThread) Native.AttachThreadInput(currentThread, foregroundThread, true);
        Native.BringWindowToTop(window);
        Native.SetForegroundWindow(window);
        if (foregroundThread != currentThread) Native.AttachThreadInput(currentThread, foregroundThread, false);
    }

    // ================= Список окон =================

    public void Refresh()
    {
        var windows = new List<AppWindow>();
        var alive = new HashSet<uint>();
        Native.EnumWindows((hwnd, _) =>
        {
            if (IsAppWindow(hwnd, out var pid))
            {
                alive.Add(pid);
                if (!_processes.TryGetValue(pid, out var process)) _processes[pid] = process = Native.ProcessInfo(pid);
                windows.Add(new AppWindow(hwnd, pid, process.Path, PropertyStore.WindowAppId(hwnd), process.AppId));
            }
            return true;
        }, IntPtr.Zero);

        foreach (var pid in _processes.Keys.Where(p => !alive.Contains(p)).ToList()) _processes.Remove(pid);
        _windows = windows;

        var signature = string.Join(",", windows.Select(w => w.Handle)) + "|" + LastExternalForeground;
        if (signature == _signature) return;
        _signature = signature;
        Changed?.Invoke();
    }

    private bool IsAppWindow(IntPtr hwnd, out uint pid)
    {
        pid = 0;
        if (!Native.IsWindowVisible(hwnd)) return false;
        Native.GetWindowThreadProcessId(hwnd, out pid);
        if (pid == _ownPid) return false;
        var ex = (long)Native.GetWindowLongPtr(hwnd, Native.GWL_EXSTYLE);
        // Окно с владельцем на панели задач не видно — кроме тех, что сами просят кнопку (WS_EX_APPWINDOW):
        // так устроены главные окна некоторых программ на Delphi, Java и Qt.
        if (Native.GetWindow(hwnd, Native.GW_OWNER) != IntPtr.Zero && (ex & Native.WS_EX_APPWINDOW) == 0) return false;
        if ((ex & Native.WS_EX_TOOLWINDOW) != 0) return false;
        if (Native.GetWindowTextLength(hwnd) == 0) return false;

        // Скрытые DWM окна (например, «заснувшие» приложения Store) не считаем открытыми.
        // Значение 2 — окно на другом виртуальном рабочем столе: программа всё же открыта.
        if (Native.DwmGetWindowAttribute(hwnd, Native.DWMWA_CLOAKED, out int cloaked, sizeof(int)) == 0 &&
            cloaked != 0 && cloaked != 2) return false;

        // PseudoConsoleWindow — невидимая «заглушка» консольной программы, запущенной в Windows Terminal:
        // она висит свёрнутой за краем экрана, двигать её бессмысленно.
        return Native.ClassName(hwnd) is not ("Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd" or "PseudoConsoleWindow");
    }

    private static uint OwnerPid(IntPtr hwnd)
    {
        Native.GetWindowThreadProcessId(hwnd, out var pid);
        return pid;
    }

    private void Hook(uint min, uint max)
    {
        Native.WinEventProc callback = OnWinEvent;
        _callbacks.Add(callback);
        var hook = Native.SetWinEventHook(min, max, IntPtr.Zero, callback, 0, 0,
            Native.WINEVENT_OUTOFCONTEXT | Native.WINEVENT_SKIPOWNPROCESS);
        if (hook != IntPtr.Zero) _hooks.Add(hook);
    }

    private void OnWinEvent(IntPtr hook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
    {
        if (hwnd == IntPtr.Zero || idObject != 0 || idChild != 0) return;
        if (eventType == Native.EVENT_SYSTEM_FOREGROUND) LastExternalForeground = hwnd;
        _debounce.Stop();
        _debounce.Start();
    }

    // ================= Сопоставление окна и элемента группы =================

    internal static bool Matches(AppWindow w, MatchSpec s)
    {
        if (s.AppId != null && (string.Equals(w.AppId, s.AppId, StringComparison.OrdinalIgnoreCase) ||
                                string.Equals(w.ProcessAppId, s.AppId, StringComparison.OrdinalIgnoreCase))) return true;
        if (w.Path == null) return false;
        if (s.ExePath != null && string.Equals(w.Path, s.ExePath, StringComparison.OrdinalIgnoreCase)) return true;

        var name = Path.GetFileName(w.Path);
        // Squirrel (Discord, Slack…): Update.exe --processStart Discord.exe → app-1.2.3\Discord.exe
        if (s.ExeName != null && s.ExeDir != null && string.Equals(name, s.ExeName, StringComparison.OrdinalIgnoreCase) &&
            w.Path.StartsWith(s.ExeDir + "\\", StringComparison.OrdinalIgnoreCase)) return true;
        // Заглушки Windows 11: System32\notepad.exe открывает Store-версию из WindowsApps.
        return s.StubName != null && string.Equals(name, s.StubName, StringComparison.OrdinalIgnoreCase) &&
               w.Path.Contains(@"\WindowsApps\", StringComparison.OrdinalIgnoreCase);
    }

    private MatchSpec? SpecFor(LaunchItem item)
    {
        var key = $"{item.Kind}|{item.Target}|{item.Arguments}";
        if (_specs.TryGetValue(key, out var cached)) return cached;
        if (_specs.Count > 500) _specs.Clear();
        return _specs[key] = BuildSpec(item);
    }

    internal static MatchSpec? BuildSpec(LaunchItem item)
    {
        try
        {
            if (item.Kind == ItemKind.ShellApp) return ForAppId(item.Target);
            if (item.Kind != ItemKind.Path) return null;

            var target = Full(item.Target);
            var ext = Path.GetExtension(target).ToLowerInvariant();
            if (ext == ".exe") return ForExe(target, item.Arguments, null);
            if (ext != ".lnk" || ShellLink.Read(target) is not { } link) return null;

            var linkTarget = Full(link.Target);
            return linkTarget.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                ? ForExe(linkTarget, link.Arguments, link.AppUserModelId)
                : link.AppUserModelId != null ? ForAppId(link.AppUserModelId) : null;
        }
        catch
        {
            return null;
        }
    }

    private static MatchSpec ForExe(string exe, string? arguments, string? appId)
    {
        var name = Path.GetFileName(exe);
        if (name.Equals("Update.exe", StringComparison.OrdinalIgnoreCase) && arguments != null)
        {
            var parts = arguments.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            int i = Array.FindIndex(parts, p => p.Equals("--processStart", StringComparison.OrdinalIgnoreCase));
            if (i >= 0 && i + 1 < parts.Length)
                return new MatchSpec(null, parts[i + 1].Trim('"'), Path.GetDirectoryName(exe), null, appId);
        }
        // Окно Steam рисует steamwebhelper.exe, зато оно помечено AppUserModelID клиента.
        if (name.Equals("steam.exe", StringComparison.OrdinalIgnoreCase)) appId ??= "Valve.Steam.Client";

        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var stub = exe.StartsWith(windows + "\\", StringComparison.OrdinalIgnoreCase) ? name : null;
        // Squirrel нового типа (Discord, Element): ярлык ведёт на заглушку Discord.exe,
        // а работает app-1.0.xxxx\Discord.exe — то же имя в подпапке.
        var dir = stub == null ? Path.GetDirectoryName(exe) : null;
        return new MatchSpec(exe, dir != null ? name : null, dir, stub, appId);
    }

    private static MatchSpec ForAppId(string appId) =>
        // Ярлык «Проводник» несёт только AppUserModelID, а у окон Проводника его нет.
        string.Equals(appId, "Microsoft.Windows.Explorer", StringComparison.OrdinalIgnoreCase)
            ? new MatchSpec(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe"), null, null, null, appId)
            : new MatchSpec(null, null, null, null, appId);

    private static string Full(string path)
    {
        var expanded = Environment.ExpandEnvironmentVariables(path.Trim().Trim('"'));
        try { return Path.GetFullPath(expanded); }
        catch { return expanded; }
    }
}
