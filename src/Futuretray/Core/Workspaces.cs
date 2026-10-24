using System.Windows.Threading;

namespace Futuretray.Core;

/// <summary>
/// Рабочие пространства: запомнить, где стоят окна программ группы, и потом разложить их обратно —
/// запустив недостающие программы и дождавшись их окон.
/// </summary>
public static class Workspaces
{
    private static readonly TimeSpan WaitForWindows = TimeSpan.FromSeconds(25);
    private static readonly TimeSpan SecondPass = TimeSpan.FromMilliseconds(1500);
    private static readonly TimeSpan WatchSplash = TimeSpan.FromSeconds(12);
    private static readonly Dictionary<string, ArrangeSession> Sessions = new();

    public sealed record CaptureResult(int Saved, IReadOnlyList<LaunchItem> NotRunning, IReadOnlyList<LaunchItem> Minimized);

    public sealed record ArrangeResult(int Placed, IReadOnlyList<LaunchItem> NotFound, IReadOnlyList<LaunchItem> Denied);

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
        var text = Loc.F("Разложено окон: {0}", r.Placed);
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
        int saved = 0;
        var notRunning = new List<LaunchItem>();
        var minimized = new List<LaunchItem>();
        var claimed = new HashSet<IntPtr>();

        foreach (var item in group.Items)
        {
            // Верхнее по Z — то, с которым работали последним. Два элемента одной программы
            // (например, две папки Проводника) получают разные окна; свёрнутые — только если других нет.
            var windows = running.WindowsFor(item).Where(w => !claimed.Contains(w)).ToList();
            if (windows.Count == 0)
            {
                notRunning.Add(item);
                continue;
            }
            var window = windows.FirstOrDefault(w => !Native.IsIconic(w));
            if (window == IntPtr.Zero)
            {
                minimized.Add(item);
                continue;
            }

            claimed.Add(window);
            var handle = Native.MonitorFromWindow(window, Native.MONITOR_DEFAULTTONEAREST);
            var monitor = monitors.FirstOrDefault(m => m.Handle == handle) ?? Monitors.Find(null);
            item.Layout = ToLayout(Native.VisibleBounds(window), monitor.Work,
                monitor.IsPrimary ? null : monitor.Device, Native.IsZoomed(window));
            saved++;
        }
        return new CaptureResult(saved, notRunning, minimized);
    }

    public static void Forget(Group group)
    {
        foreach (var item in group.Items) item.Layout = null;
    }

    // ================= Разложить =================

    /// <summary>
    /// Ставит окна на места; недостающие программы запускает и ждёт их окон.
    /// Итог приходит в <paramref name="done"/>, когда разложено всё или вышло время.
    /// </summary>
    public static void Arrange(Group group, Action<ArrangeResult>? done = null)
    {
        if (Sessions.Remove(group.Id, out var previous)) previous.Stop();
        ArrangeSession? session = null;
        session = new ArrangeSession(group, done, () =>
        {
            if (Sessions.TryGetValue(group.Id, out var current) && current == session) Sessions.Remove(group.Id);
        });
        Sessions[group.Id] = session;
        session.Start();
    }

    /// <returns>false — Windows не дала подвинуть окно (обычно программа запущена от администратора).</returns>
    public static bool Apply(IntPtr window, WindowLayout layout)
    {
        var work = Monitors.Find(layout.Monitor).Work;
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

    private sealed class ArrangeSession
    {
        private readonly Group _group;
        private readonly Action<ArrangeResult>? _done;
        private readonly Action _stopped;
        private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(300) };
        private readonly Dictionary<LaunchItem, (IntPtr Window, DateTime At)> _placed = new();
        private readonly HashSet<LaunchItem> _finished = new();
        private readonly HashSet<LaunchItem> _denied = new();
        private readonly HashSet<LaunchItem> _launched = new();
        private DateTime _deadline, _watchUntil;
        private bool _reported;

        public ArrangeSession(Group group, Action<ArrangeResult>? done, Action stopped)
        {
            _group = group;
            _done = done;
            _stopped = stopped;
            _timer.Tick += (_, _) => Tick();
        }

        private IEnumerable<LaunchItem> Items => _group.Items.Where(i => i.Layout != null);

        public void Start()
        {
            var running = RunningApps.Current!;
            running.Refresh();
            foreach (var item in Items)
                if (running.WindowsFor(item).Count == 0 && Launcher.Launch(item) == null) _launched.Add(item);
            _deadline = DateTime.Now + WaitForWindows;
            _watchUntil = _launched.Count > 0 ? DateTime.Now + WatchSplash : DateTime.Now;
            Tick();
            _timer.Start();
        }

        public void Stop() => _timer.Stop();

        private void Tick()
        {
            var running = RunningApps.Current!;
            running.Refresh();
            var now = DateTime.Now;

            // Запущенная нами программа закрыла заставку — ждём и ставим её настоящее окно.
            foreach (var item in _launched)
                if (_finished.Contains(item) && !Native.IsWindow(_placed[item].Window))
                {
                    _finished.Remove(item);
                    _placed.Remove(item);
                }

            // Окна, уже отданные другим элементам: две копии одной программы не делят одно окно.
            var claimed = _placed.Values.Select(p => p.Window).ToHashSet();
            foreach (var item in Items.Where(i => !_finished.Contains(i)))
            {
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
                    if (Apply(window, item.Layout!)) _denied.Remove(item);
                    else _denied.Add(item);
                    _placed[item] = (window, now);
                }
                else if (now - placed.At >= SecondPass)
                {
                    // Второй проход: программа могла сама восстановить свою позицию после показа.
                    if (!_denied.Contains(item)) Apply(window, item.Layout!);
                    _finished.Add(item);
                }
            }

            bool allDone = Items.All(i => _finished.Contains(i));
            if (allDone && !_reported) Report();
            if (now >= _deadline || (allDone && now >= _watchUntil))
            {
                _timer.Stop();
                if (!_reported) Report();
                _stopped();
            }
        }

        private void Report()
        {
            _reported = true;
            var notFound = Items.Where(i => !_placed.ContainsKey(i)).ToList();
            var denied = _denied.ToList();
            _done?.Invoke(new ArrangeResult(_placed.Count - denied.Count, notFound, denied));
        }
    }
}
