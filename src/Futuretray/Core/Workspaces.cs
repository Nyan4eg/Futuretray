using System.Windows.Threading;

namespace Futuretray.Core;

/// <summary>
/// Рабочие пространства: запомнить, где стоят окна программ группы, и потом разложить их обратно —
/// запустив недостающие программы по порядку (с паузами) и дождавшись их окон.
/// Расположение хранится отдельно для каждого набора мониторов: дома с монитором и в дороге — своё.
/// </summary>
public static class Workspaces
{
    private static readonly TimeSpan WaitForWindows = TimeSpan.FromSeconds(25);
    private static readonly TimeSpan SecondPass = TimeSpan.FromMilliseconds(1500);
    private static readonly TimeSpan WatchSplash = TimeSpan.FromSeconds(12);
    private static readonly Dictionary<string, OpenAllSession> Sessions = new();

    public sealed record CaptureResult(int Saved, IReadOnlyList<LaunchItem> NotRunning, IReadOnlyList<LaunchItem> Minimized);

    /// <param name="Profile">Набор мониторов, чьё расположение применили; null — расположения нет, только запуск.</param>
    /// <param name="ExactProfile">false — для текущих мониторов ничего не запомнено, взяли ближайшее.</param>
    public sealed record ArrangeResult(int Launched, int Placed, IReadOnlyList<LaunchItem> NotFound,
        IReadOnlyList<LaunchItem> Denied, string? Profile = null, bool ExactProfile = true);

    // ================= Геометрия (чистые функции — их проверяют тесты) =================

    /// <summary>Видимый прямоугольник окна → доли рабочей области монитора.</summary>
    public static WindowLayout ToLayout(Native.RECT frame, Native.RECT work, string? monitor, bool maximized) => new()
    {
        Monitor = monitor,
        X = (frame.Left - work.Left) / (double)Math.Max(1, work.Width),
        Y = (frame.Top - work.Top) / (double)Math.Max(1, work.Height),
        Width = frame.Width / (double)Math.Max(1, work.Width),
        Height = frame.Height / (double)Math.Max(1, work.Height),
        Maximized = maximized,
    };

    /// <summary>Доли → прямоугольник на мониторе. Окно всегда остаётся видимым хотя бы частично.</summary>
    public static Native.RECT ToRect(WindowLayout layout, Native.RECT work)
    {
        double w = Math.Clamp(layout.Width, 0.05, 1.5), h = Math.Clamp(layout.Height, 0.05, 1.5);
        double x = Math.Clamp(layout.X, -w + 0.05, 0.95), y = Math.Clamp(layout.Y, 0, 0.95);
        int left = work.Left + (int)Math.Round(x * work.Width);
        int top = work.Top + (int)Math.Round(y * work.Height);
        return new Native.RECT
        {
            Left = left,
            Top = top,
            Right = left + (int)Math.Round(w * work.Width),
            Bottom = top + (int)Math.Round(h * work.Height),
        };
    }

    // ================= Наборы мониторов =================

    private static string Bounds(Native.RECT r) => $"{r.Left},{r.Top},{r.Width},{r.Height}";

    /// <summary>
    /// Ключ набора мониторов: размеры и положение каждого, слева направо, основной помечен «*».
    /// Например «1920x1080@0,0*|2560x1440@1920,0».
    /// </summary>
    public static string ProfileKey(IEnumerable<MonitorInfo> monitors) => string.Join("|",
        monitors.OrderBy(m => m.Bounds.Left).ThenBy(m => m.Bounds.Top)
            .Select(m => $"{m.Bounds.Width}x{m.Bounds.Height}@{m.Bounds.Left},{m.Bounds.Top}{(m.IsPrimary ? "*" : "")}"));

    public static string CurrentProfile => ProfileKey(Monitors.All());

    /// <summary>Ключ → «1920×1080 + 2560×1440» для подписи в редакторе.</summary>
    public static string ScreensText(string key) => string.Join(" + ",
        key.Split('|', StringSplitOptions.RemoveEmptyEntries).Select(m => m.Split('@')[0].Replace('x', '×')));

    private static int MonitorCount(string key) => key.Split('|', StringSplitOptions.RemoveEmptyEntries).Length;

    /// <summary>Наборы мониторов, для которых у группы что-то запомнено, и когда запоминали в последний раз.</summary>
    public static Dictionary<string, DateTime> Profiles(Group group)
    {
        var result = new Dictionary<string, DateTime>();
        foreach (var (key, layout) in group.Items.Where(i => i.Layouts != null).SelectMany(i => i.Layouts!))
            if (!result.TryGetValue(key, out var at) || layout.Captured > at) result[key] = layout.Captured;
        return result;
    }

    /// <summary>
    /// Какое расположение применить: для текущих мониторов, иначе последнее запомненное
    /// с тем же числом мониторов, иначе просто последнее. Доли экрана переносятся на любые мониторы.
    /// </summary>
    public static string? PickProfile(IReadOnlyDictionary<string, DateTime> profiles, string current)
    {
        if (profiles.Count == 0) return null;
        if (profiles.ContainsKey(current)) return current;
        int count = MonitorCount(current);
        return profiles.OrderByDescending(p => MonitorCount(p.Key) == count).ThenByDescending(p => p.Value).First().Key;
    }

    /// <summary>Монитор места: по границам, потом по имени устройства, иначе основной.</summary>
    public static MonitorInfo FindMonitor(WindowLayout layout, IReadOnlyList<MonitorInfo> monitors) =>
        monitors.FirstOrDefault(m => layout.Screen != null && Bounds(m.Bounds) == layout.Screen)
        ?? monitors.FirstOrDefault(m => layout.Monitor != null && string.Equals(m.Device, layout.Monitor, StringComparison.OrdinalIgnoreCase))
        ?? monitors.FirstOrDefault(m => m.IsPrimary)
        ?? monitors.First();

    /// <summary>Места из 1.6.0 (без набора мониторов) относим к текущему набору. true — что-то перенесли.</summary>
    public static bool MigrateLegacy(IEnumerable<Group> groups, string currentProfile)
    {
        bool changed = false;
        foreach (var item in groups.SelectMany(g => g.Items).Where(i => i.LegacyLayout != null))
        {
            if (item.Layouts?.ContainsKey(currentProfile) != true)
                item.Layouts = new Dictionary<string, WindowLayout>(item.Layouts ?? new()) { [currentProfile] = item.LegacyLayout! };
            item.LegacyLayout = null;
            changed = true;
        }
        return changed;
    }

    // ================= Итоги для пользователя =================

    private static string Names(IEnumerable<LaunchItem> items) => string.Join(", ", items.Select(i => i.Name));

    public static string Describe(CaptureResult r)
    {
        if (r.Saved == 0)
            return Loc.T("Ни одна программа группы не открыта — откройте их, расставьте окна и нажмите ещё раз");
        var text = Loc.F("Запомнено окон: {0}", r.Saved);
        if (r.NotRunning.Count > 0) text += Loc.F(" · не открыты: {0}", Names(r.NotRunning));
        if (r.Minimized.Count > 0) text += Loc.F(" · свёрнуты, пропущены: {0}", Names(r.Minimized));
        return text;
    }

    public static string Describe(ArrangeResult r)
    {
        if (r.Profile == null) return Loc.F("Запущено программ: {0}", r.Launched);
        var text = Loc.F("Разложено окон: {0}", r.Placed);
        if (!r.ExactProfile) text += Loc.F(" · для этих мониторов места не запомнены — взяты с {0}", ScreensText(r.Profile));
        if (r.NotFound.Count > 0) text += Loc.F(" · не дождался окна: {0}", Names(r.NotFound));
        if (r.Denied.Count > 0) text += Loc.F(" · Windows не дала подвинуть (запущены от администратора): {0}", Names(r.Denied));
        return text;
    }

    // ================= Запомнить =================

    public static CaptureResult Capture(Group group)
    {
        if (RunningApps.Current is not { } running) return new CaptureResult(0, group.Items.ToList(), []);
        running.Refresh();
        var monitors = Monitors.All();
        var profile = ProfileKey(monitors);
        var now = DateTime.Now;
        var notRunning = new List<LaunchItem>();
        var minimized = new List<LaunchItem>();
        var claimed = new HashSet<IntPtr>();
        var found = new Dictionary<LaunchItem, WindowLayout>();

        foreach (var item in group.Items)
        {
            // Верхнее по Z — то, с которым работали последним. Два элемента одной программы
            // (например, две папки Проводника) получают разные окна; свёрнутые — только если других нет.
            var windows = running.WindowsFor(item).Where(w => !claimed.Contains(w)).ToList();
            var window = windows.FirstOrDefault(w => !Native.IsIconic(w));
            if (windows.Count == 0) notRunning.Add(item);
            else if (window == IntPtr.Zero) minimized.Add(item);
            else
            {
                claimed.Add(window);
                var handle = Native.MonitorFromWindow(window, Native.MONITOR_DEFAULTTONEAREST);
                var monitor = monitors.FirstOrDefault(m => m.Handle == handle) ?? Monitors.Find(null);
                var layout = ToLayout(Native.VisibleBounds(window), monitor.Work, monitor.IsPrimary ? null : monitor.Device, Native.IsZoomed(window));
                layout.Screen = Bounds(monitor.Bounds);
                layout.Captured = now;
                found[item] = layout;
            }
        }

        // Ничего не открыто — не трогаем то, что было запомнено раньше.
        if (found.Count == 0) return new CaptureResult(0, notRunning, minimized);

        // Запоминание — снимок «как сейчас» для этих мониторов: закрытые программы из него выпадают.
        foreach (var item in group.Items)
        {
            var layouts = new Dictionary<string, WindowLayout>(item.Layouts ?? new());
            if (found.TryGetValue(item, out var layout)) layouts[profile] = layout;
            else if (!layouts.Remove(profile)) continue;
            item.Layouts = layouts;
        }
        return new CaptureResult(found.Count, notRunning, minimized);
    }

    /// <summary>Забыть расположение для набора мониторов; null — для всех.</summary>
    public static void Forget(Group group, string? profile = null)
    {
        foreach (var item in group.Items.Where(i => i.Layouts != null))
            item.Layouts = profile == null ? null : item.Layouts!.Where(p => p.Key != profile).ToDictionary(p => p.Key, p => p.Value);
    }

    // ================= Открыть все и разложить =================

    /// <summary>
    /// «Открыть все»: запускает недостающие программы по порядку списка, выдерживая паузы,
    /// и, если расположение запомнено, ставит окна на места. Итог приходит в <paramref name="done"/>.
    /// </summary>
    public static void OpenAll(Group group, Action<ArrangeResult>? done = null)
    {
        if (Sessions.Remove(group.Id, out var previous)) previous.Stop();
        OpenAllSession? session = null;
        session = new OpenAllSession(group, done, () =>
        {
            if (Sessions.TryGetValue(group.Id, out var current) && current == session) Sessions.Remove(group.Id);
        });
        Sessions[group.Id] = session;
        session.Start();
    }

    /// <returns>false — Windows не дала подвинуть окно (обычно программа запущена от администратора).</returns>
    public static bool Apply(IntPtr window, WindowLayout layout, IReadOnlyList<MonitorInfo>? monitors = null)
    {
        var work = FindMonitor(layout, monitors ?? Monitors.All()).Work;
        var target = ToRect(layout, work);
        // Пустой SetWindowPos — проба прав: окна программ от администратора не трогаем вовсе,
        // иначе развернули бы свёрнутое, так и не сумев его подвинуть.
        if (!Native.SetWindowPos(window, IntPtr.Zero, 0, 0, 0, 0,
                Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOZORDER | Native.SWP_NOACTIVATE)) return false;
        if (Native.IsIconic(window) || Native.IsZoomed(window)) Native.ShowWindow(window, Native.SW_RESTORE);

        // Видимая граница и прямоугольник окна различаются на невидимые рамки — учитываем их.
        Native.GetWindowRect(window, out var outer);
        var visible = Native.VisibleBounds(window);
        int left = visible.Left - outer.Left, top = visible.Top - outer.Top;
        int right = outer.Right - visible.Right, bottom = outer.Bottom - visible.Bottom;

        bool ok = Native.SetWindowPos(window, IntPtr.Zero,
            target.Left - left, target.Top - top,
            target.Width + left + right, target.Height + top + bottom,
            Native.SWP_NOZORDER | Native.SWP_NOACTIVATE);
        if (ok && layout.Maximized) Native.ShowWindow(window, Native.SW_MAXIMIZE);
        return ok;
    }

    private sealed class OpenAllSession
    {
        private readonly Group _group;
        private readonly Action<ArrangeResult>? _done;
        private readonly Action _stopped;
        private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(300) };
        private readonly Queue<(LaunchItem Item, DateTime Due)> _queue = new();
        private readonly Dictionary<LaunchItem, WindowLayout> _layouts = new();
        private readonly HashSet<LaunchItem> _ready = new(); // уже открыты или запущены — их окна можно ставить
        private readonly Dictionary<LaunchItem, (IntPtr Window, DateTime At)> _placed = new();
        private readonly HashSet<LaunchItem> _finished = new();
        private readonly HashSet<LaunchItem> _denied = new();
        private readonly HashSet<LaunchItem> _launched = new();
        private IReadOnlyList<MonitorInfo> _monitors = [];
        private string? _profile;
        private bool _exact;
        private DateTime _lastLaunch;
        private bool _reported, _over;

        public OpenAllSession(Group group, Action<ArrangeResult>? done, Action stopped)
        {
            _group = group;
            _done = done;
            _stopped = stopped;
            _timer.Tick += (_, _) => Tick();
        }

        public void Start()
        {
            var running = RunningApps.Current;
            running?.Refresh();
            _monitors = Monitors.All();
            var current = ProfileKey(_monitors);
            _profile = PickProfile(Profiles(_group), current);
            _exact = _profile == current;
            if (_profile != null)
                foreach (var item in _group.Items)
                    if (item.Layouts?.TryGetValue(_profile, out var layout) == true) _layouts[item] = layout;

            // Очередь запуска — в порядке списка; пауза отсчитывается от предыдущей запущенной программы.
            // Уже открытые программы не запускаем второй раз и их паузу не ждём.
            var due = _lastLaunch = DateTime.Now;
            foreach (var item in _group.Items.ToList())
            {
                if (running?.IsRunning(item) == true)
                {
                    _ready.Add(item);
                    continue;
                }
                due += TimeSpan.FromSeconds(item.StartDelay);
                _queue.Enqueue((item, due));
            }
            Tick();
            if (!_over) _timer.Start();
        }

        public void Stop() => _timer.Stop();

        private void Tick()
        {
            var now = DateTime.Now;
            while (_queue.Count > 0 && _queue.Peek().Due <= now)
            {
                var item = _queue.Dequeue().Item;
                if (Launcher.Launch(item) == null)
                {
                    _launched.Add(item);
                    _ready.Add(item);
                }
                _lastLaunch = now;
            }

            if (_layouts.Count > 0) PlaceWindows(now);

            bool allPlaced = _layouts.Keys.All(i => _finished.Contains(i) || (!_ready.Contains(i) && _queue.All(q => q.Item != i)));
            bool launchedAll = _queue.Count == 0;
            bool allDone = launchedAll && allPlaced;
            var deadline = _lastLaunch + WaitForWindows;
            var watchUntil = _launched.Count > 0 && _layouts.Count > 0 ? _lastLaunch + WatchSplash : now;

            if (allDone && !_reported) Report();
            if ((launchedAll && now >= deadline) || (allDone && now >= watchUntil))
            {
                _timer.Stop();
                _over = true;
                if (!_reported) Report();
                _stopped();
            }
        }

        private void PlaceWindows(DateTime now)
        {
            var running = RunningApps.Current;
            if (running == null) return;
            running.Refresh();

            // Запущенная нами программа закрыла заставку — ждём и ставим её настоящее окно.
            foreach (var item in _launched)
                if (_finished.Contains(item) && !Native.IsWindow(_placed[item].Window))
                {
                    _finished.Remove(item);
                    _placed.Remove(item);
                }

            // Окна, уже отданные другим элементам: две копии одной программы не делят одно окно.
            var claimed = _placed.Values.Select(p => p.Window).ToHashSet();
            foreach (var (item, layout) in _layouts)
            {
                if (_finished.Contains(item) || !_ready.Contains(item)) continue;
                bool had = _placed.TryGetValue(item, out var placed);
                if (had) claimed.Remove(placed.Window);
                var windows = running.WindowsFor(item);
                var window = had && windows.Contains(placed.Window)
                    ? placed.Window
                    : windows.FirstOrDefault(w => !claimed.Contains(w));
                if (window == IntPtr.Zero)
                {
                    if (had) claimed.Add(placed.Window);
                    continue;
                }
                claimed.Add(window);

                if (!had || window != placed.Window)
                {
                    // Первое окно — или программа заменила заставку настоящим окном: ставим заново.
                    if (Apply(window, layout, _monitors)) _denied.Remove(item);
                    else _denied.Add(item);
                    _placed[item] = (window, now);
                }
                else if (now - placed.At >= SecondPass)
                {
                    // Второй проход: программа могла сама восстановить свою позицию после показа.
                    if (!_denied.Contains(item)) Apply(window, layout, _monitors);
                    _finished.Add(item);
                }
            }
        }

        private void Report()
        {
            _reported = true;
            var notFound = _layouts.Keys.Where(i => !_placed.ContainsKey(i)).ToList();
            var denied = _denied.ToList();
            _done?.Invoke(new ArrangeResult(_launched.Count, _placed.Count - denied.Count, notFound, denied, _profile, _exact));
        }
    }
}
