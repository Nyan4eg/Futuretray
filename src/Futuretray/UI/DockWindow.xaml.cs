using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shell;
using System.Windows.Threading;
using Futuretray.Core;

namespace Futuretray.UI;

/// <summary>
/// Трей группы на рабочем столе: панель у края экрана (или в свободном месте),
/// которая не забирает фокус, умеет быть поверх окон, прятаться за край и резервировать место как панель задач.
/// </summary>
public partial class DockWindow : Window
{
    private const int AppBarCallback = 0x8000 + 40;
    private const uint MoveFlags = Native.SWP_NOSIZE | Native.SWP_NOZORDER | Native.SWP_NOACTIVATE;

    private IntPtr _hwnd;
    private ResourceDictionary? _theme;
    private bool _appBarRegistered;
    private bool _appBarReserved;
    private bool _fullscreen;
    private bool _menuOpen;
    private bool _closingByCode;

    // автоскрытие
    private DispatcherTimer? _hoverTimer;
    private DispatcherTimer? _slideTimer;
    private bool _hidden;
    private int _edgeHits;
    private long _lastInside;
    private Native.RECT _shown;
    private Native.RECT _hiddenRect;
    private Native.RECT _area;

    // перетаскивание в свободном режиме
    private bool _dragging;
    private Native.POINT _dragCursor;
    private Native.RECT _dragRect;

    public DockWindow(Group group)
    {
        Group = group;
        InitializeComponent();

        // Стеклянная рамка нужна, чтобы сквозь прозрачное окно было видно размытие.
        WindowChrome.SetWindowChrome(this, new WindowChrome
        {
            CaptionHeight = 0,
            ResizeBorderThickness = new Thickness(0),
            GlassFrameThickness = new Thickness(-1),
            UseAeroCaptionButtons = false,
            CornerRadius = new CornerRadius(0),
        });

        Tiles.TilesFocusable = false;
        Tiles.AcceptFileDrop = true;
        Tiles.FilesDropped += (item, files) =>
        {
            if (Launcher.OpenWith(item, files) is { } error) ShowError(error);
        };
        Tiles.Bind(group);
        Tiles.ItemInvoked += (item, _, newInstance) => Launch(item, newInstance);
        Tiles.ItemMenuRequested += OnItemMenu;
        MouseRightButtonUp += OnBackgroundMenu;
        SourceInitialized += OnSourceInitialized;
        Closed += (_, _) =>
        {
            _hoverTimer?.Stop();
            _slideTimer?.Stop();
            Tiles.Unbind();
            RemoveAppBar();
        };
    }

    public Group Group { get; }

    /// <summary>Окно закрыли мы сами (а не, например, перезапуск Проводника).</summary>
    public bool ClosedByCode => _closingByCode;

    public void CloseDock()
    {
        _closingByCode = true;
        Close();
    }

    private bool Vertical => Group.DockEdge is DockEdge.Left or DockEdge.Right;
    private bool Free => Group.DockEdge == DockEdge.Free;
    private bool AutoHide => Group.DockAutoHide && !Free;
    private bool Reserve => Group.DockReserveSpace && !Free;

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        _hwnd = new WindowInteropHelper(this).Handle;
        WindowEffects.MakeToolWindow(this);

        var ex = (long)Native.GetWindowLongPtr(_hwnd, Native.GWL_EXSTYLE);
        Native.SetWindowLongPtr(_hwnd, Native.GWL_EXSTYLE, new IntPtr(ex | Native.WS_EX_NOACTIVATE));

        // Владелец — рабочий стол: так трей не пропадает по Win+D.
        var desktop = Native.FindWindow("Progman", null);
        if (desktop != IntPtr.Zero) Native.SetWindowLongPtr(_hwnd, Native.GWLP_HWNDPARENT, desktop);

        HwndSource.FromHwnd(_hwnd)?.AddHook(WndProc);
    }

    /// <summary>Применяет настройки группы: вид, размер, положение, поведение.</summary>
    public void Apply()
    {
        if (!IsVisible)
        {
            Show();
            if (_hwnd == IntPtr.Zero) _hwnd = new WindowInteropHelper(this).Handle;
        }

        ApplyAppearance();
        ApplyLayout();
        EnsureAppBar();
        Reposition();
        ApplyZOrder();
        UpdateAutoHideTimer();
    }

    private void ApplyAppearance()
    {
        bool dark = ThemeManager.PopupDark;
        var dict = ThemeManager.PopupDictionary(dark);
        if (!ReferenceEquals(_theme, dict))
        {
            if (_theme != null) Resources.MergedDictionaries.Remove(_theme);
            Resources.MergedDictionaries.Add(dict);
            _theme = dict;
        }

        int darkMode = dark ? 1 : 0;
        Native.DwmSetWindowAttribute(_hwnd, Native.DWMWA_USE_IMMERSIVE_DARK_MODE, ref darkMode, sizeof(int));
        if (WindowEffects.SupportsRoundCorners)
        {
            int corner = 2;
            Native.DwmSetWindowAttribute(_hwnd, Native.DWMWA_WINDOW_CORNER_PREFERENCE, ref corner, sizeof(int));
        }
        if (WindowEffects.SupportsBackdrop)
        {
            // Системный акрил в неактивном окне становится серым, поэтому используем размытие композиции.
            int none = 1;
            Native.DwmSetWindowAttribute(_hwnd, Native.DWMWA_SYSTEMBACKDROP_TYPE, ref none, sizeof(int));
        }

        bool solid = Group.Backdrop == BackdropKind.Solid;
        var target = HwndSource.FromHwnd(_hwnd)?.CompositionTarget;
        if (solid)
        {
            Native.SetAccent(_hwnd, 0, 0);
            if (target != null) target.BackgroundColor = dark ? Color.FromRgb(0x2B, 0x2B, 0x2B) : Color.FromRgb(0xF5, 0xF5, 0xF5);
        }
        else
        {
            uint rgb = dark ? 0x202020u : 0xF3F3F3u;
            uint alpha = Group.Backdrop == BackdropKind.Mica ? 0xE0u : 0xA8u;
            Native.SetAccent(_hwnd, 4, (alpha << 24) | rgb);
            if (target != null) target.BackgroundColor = Colors.Transparent;
        }
        SolidLayer.Visibility = solid ? Visibility.Visible : Visibility.Collapsed;
        TintLayer.Background = ColorUtil.Brush(ColorUtil.WithAlpha(ColorUtil.Resolve(Group.Color), Group.TintOpacity));
    }

    private void ApplyLayout()
    {
        bool vertical = Vertical;
        Body.Orientation = vertical ? Orientation.Vertical : Orientation.Horizontal;
        Tiles.Line = vertical ? Orientation.Vertical : Orientation.Horizontal;
        Tiles.IndicatorSide = Group.DockEdge switch
        {
            DockEdge.Left => Dock.Left,
            DockEdge.Right => Dock.Right,
            DockEdge.Top => Dock.Top,
            _ => Dock.Bottom,
        };
        Tiles.Rebuild();

        Grip.Visibility = Free ? Visibility.Visible : Visibility.Collapsed;
        GripGlyph.Text = vertical ? "" : "";
        if (vertical)
        {
            Grip.Width = double.NaN;
            Grip.Height = 14;
        }
        else
        {
            Grip.Width = 14;
            Grip.Height = double.NaN;
        }

        Body.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var size = Body.DesiredSize;
        Width = Math.Ceiling(Math.Max(size.Width, 40));
        Height = Math.Ceiling(Math.Max(size.Height, 40));
    }

    // ================= Положение =================

    private void Reposition()
    {
        if (_hwnd == IntPtr.Zero) return;

        MonitorInfo monitor;
        if (Free && Group.DockX is { } fx && Group.DockY is { } fy)
            monitor = Monitors.FromPoint(new Native.POINT(fx + 10, fy + 10));
        else
            monitor = Monitors.Find(Group.DockMonitor);

        // Переносим окно на нужный монитор, чтобы WPF применил его масштаб.
        Native.GetWindowRect(_hwnd, out var current);
        if (Monitors.FromPoint(new Native.POINT(current.Left + current.Width / 2, current.Top + current.Height / 2)).Handle != monitor.Handle)
            Native.SetWindowPos(_hwnd, IntPtr.Zero, monitor.Work.Left, monitor.Work.Top, 0, 0, MoveFlags);

        double scale = monitor.Dpi / 96.0;
        int w = (int)Math.Round(Width * scale);
        int h = (int)Math.Round(Height * scale);
        int gap = (int)Math.Round(8 * scale);

        if (Free)
        {
            var work = monitor.Work;
            int x = Group.DockX ?? (work.Left + work.Right - w) / 2;
            int y = Group.DockY ?? (work.Top + work.Bottom - h) / 2;
            x = Clamp(x, work.Left, work.Right - w);
            y = Clamp(y, work.Top, work.Bottom - h);
            _area = work;
            _shown = _hiddenRect = Rect(x, y, w, h);
            Move(_shown, w, h);
            return;
        }

        Native.RECT area;
        if (Reserve)
        {
            int thickness = (Vertical ? w : h) + 2 * gap;
            area = ReserveSpace(monitor, thickness);
        }
        else
        {
            ReleaseSpace();
            area = Monitors.Find(monitor.Device).Work;
        }
        _area = area;

        int along0 = Vertical ? area.Top : area.Left;
        int along1 = Vertical ? area.Bottom : area.Right;
        int length = Vertical ? h : w;
        int pos = Group.DockAlign switch
        {
            DockAlign.Start => along0 + gap,
            DockAlign.End => along1 - gap - length,
            _ => (along0 + along1 - length) / 2,
        };
        pos = Clamp(pos, along0, along1 - length);

        _shown = Group.DockEdge switch
        {
            DockEdge.Left => Rect(area.Left + gap, pos, w, h),
            DockEdge.Right => Rect(area.Right - gap - w, pos, w, h),
            DockEdge.Top => Rect(pos, area.Top + gap, w, h),
            _ => Rect(pos, area.Bottom - gap - h, w, h),
        };

        int peek = Math.Max(2, (int)Math.Round(2 * scale));
        _hiddenRect = Group.DockEdge switch
        {
            DockEdge.Left => Rect(area.Left - w + peek, pos, w, h),
            DockEdge.Right => Rect(area.Right - peek, pos, w, h),
            DockEdge.Top => Rect(pos, area.Top - h + peek, w, h),
            _ => Rect(pos, area.Bottom - peek, w, h),
        };

        if (!AutoHide) _hidden = false;
        Move(_hidden ? _hiddenRect : _shown, w, h);
    }

    private void Move(Native.RECT r, int w, int h) =>
        Native.SetWindowPos(_hwnd, IntPtr.Zero, r.Left, r.Top, w, h, Native.SWP_NOZORDER | Native.SWP_NOACTIVATE);

    private static Native.RECT Rect(int x, int y, int w, int h) => new() { Left = x, Top = y, Right = x + w, Bottom = y + h };

    private static int Clamp(int v, int min, int max) => max < min ? min : Math.Clamp(v, min, max);

    private void ApplyZOrder()
    {
        bool topmost = (Group.DockTopmost || AutoHide) && !_fullscreen;
        Native.SetWindowPos(_hwnd, topmost ? Native.HWND_TOPMOST : Native.HWND_NOTOPMOST, 0, 0, 0, 0,
            Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
        if (_fullscreen)
            Native.SetWindowPos(_hwnd, Native.HWND_BOTTOM, 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
    }

    // ================= Панель (appbar) =================

    private Native.APPBARDATA AppBarData() => new()
    {
        cbSize = Marshal.SizeOf<Native.APPBARDATA>(),
        hWnd = _hwnd,
        uCallbackMessage = AppBarCallback,
    };

    /// <summary>Регистрируемся всегда — так Windows сообщает о полноэкранных приложениях.</summary>
    private void EnsureAppBar()
    {
        if (_appBarRegistered || _hwnd == IntPtr.Zero) return;
        var data = AppBarData();
        _appBarRegistered = Native.SHAppBarMessage(Native.ABM_NEW, ref data) != 0;
    }

    private Native.RECT ReserveSpace(MonitorInfo monitor, int thickness)
    {
        EnsureAppBar();
        var data = AppBarData();
        data.uEdge = Group.DockEdge switch
        {
            DockEdge.Left => Native.ABE_LEFT,
            DockEdge.Top => Native.ABE_TOP,
            DockEdge.Right => Native.ABE_RIGHT,
            _ => Native.ABE_BOTTOM,
        };
        data.rc = monitor.Bounds;
        Fit(ref data.rc, thickness);
        Native.SHAppBarMessage(Native.ABM_QUERYPOS, ref data);
        Fit(ref data.rc, thickness);
        Native.SHAppBarMessage(Native.ABM_SETPOS, ref data);
        _appBarReserved = true;
        return data.rc;
    }

    private void Fit(ref Native.RECT rc, int thickness)
    {
        switch (Group.DockEdge)
        {
            case DockEdge.Left: rc.Right = rc.Left + thickness; break;
            case DockEdge.Right: rc.Left = rc.Right - thickness; break;
            case DockEdge.Top: rc.Bottom = rc.Top + thickness; break;
            default: rc.Top = rc.Bottom - thickness; break;
        }
    }

    /// <summary>Отдаём зарезервированное место обратно, оставаясь подписанными на уведомления.</summary>
    private void ReleaseSpace()
    {
        if (!_appBarReserved) return;
        RemoveAppBar();
        EnsureAppBar();
    }

    private void RemoveAppBar()
    {
        if (!_appBarRegistered) return;
        var data = AppBarData();
        Native.SHAppBarMessage(Native.ABM_REMOVE, ref data);
        _appBarRegistered = false;
        _appBarReserved = false;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        switch (msg)
        {
            case Native.WM_MOUSEACTIVATE:
                handled = true;
                return new IntPtr(Native.MA_NOACTIVATE);
            case AppBarCallback:
                handled = true;
                switch ((int)wParam)
                {
                    case Native.ABN_POSCHANGED when Reserve:
                        Dispatcher.BeginInvoke(Reposition, DispatcherPriority.Background);
                        break;
                    case Native.ABN_FULLSCREENAPP:
                        OnFullscreen(lParam != IntPtr.Zero);
                        break;
                }
                break;
            case 0x007E: // WM_DISPLAYCHANGE
            case 0x02E0: // WM_DPICHANGED
                Dispatcher.BeginInvoke(() => { ApplyLayout(); Reposition(); }, DispatcherPriority.Background);
                break;
        }
        return IntPtr.Zero;
    }

    private void OnFullscreen(bool on)
    {
        // «Показать рабочий стол» на некоторых сборках тоже присылает это уведомление.
        if (on && Native.ClassName(Native.GetForegroundWindow()) is "Progman" or "WorkerW") return;
        if (_fullscreen == on) return;
        _fullscreen = on;
        ApplyZOrder();
    }

    // ================= Автоскрытие =================

    private void UpdateAutoHideTimer()
    {
        if (!AutoHide)
        {
            _hoverTimer?.Stop();
            if (_hidden)
            {
                _hidden = false;
                Reposition();
            }
            return;
        }
        if (_hoverTimer == null)
        {
            _hoverTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(80) };
            _hoverTimer.Tick += (_, _) => OnHoverTick();
            // После включения даём полюбоваться треем, потом он спрячется.
            _lastInside = Environment.TickCount64 + 1200;
        }
        _hoverTimer.Start();
    }

    private void OnHoverTick()
    {
        if (!Native.GetCursorPos(out var c)) return;
        long now = Environment.TickCount64;

        if (_hidden)
        {
            bool atEdge = Group.DockEdge switch
            {
                DockEdge.Left => c.X <= _area.Left + 2 && c.Y >= _shown.Top && c.Y < _shown.Bottom,
                DockEdge.Right => c.X >= _area.Right - 3 && c.Y >= _shown.Top && c.Y < _shown.Bottom,
                DockEdge.Top => c.Y <= _area.Top + 2 && c.X >= _shown.Left && c.X < _shown.Right,
                _ => c.Y >= _area.Bottom - 3 && c.X >= _shown.Left && c.X < _shown.Right,
            };
            _edgeHits = atEdge ? _edgeHits + 1 : 0;
            if (_edgeHits >= 2 && !_fullscreen)
            {
                _edgeHits = 0;
                _hidden = false;
                _lastInside = now;
                SlideTo(_shown);
            }
            return;
        }

        var zone = _shown.Inflate(14);
        if (zone.Contains(c) || _menuOpen || _dragging)
        {
            _lastInside = Math.Max(_lastInside, now);
            return;
        }
        if (now - _lastInside > 450)
        {
            _hidden = true;
            SlideTo(_hiddenRect);
        }
    }

    private void SlideTo(Native.RECT target)
    {
        _slideTimer?.Stop();
        Native.GetWindowRect(_hwnd, out var from);
        var start = Environment.TickCount64;
        const double duration = 170;
        _slideTimer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(10) };
        _slideTimer.Tick += (_, _) =>
        {
            double t = Math.Min(1, (Environment.TickCount64 - start) / duration);
            double k = 1 - Math.Pow(1 - t, 3);
            int x = (int)Math.Round(from.Left + (target.Left - from.Left) * k);
            int y = (int)Math.Round(from.Top + (target.Top - from.Top) * k);
            Native.SetWindowPos(_hwnd, IntPtr.Zero, x, y, 0, 0, MoveFlags);
            if (t >= 1) _slideTimer?.Stop();
        };
        _slideTimer.Start();
    }

    // ================= Перетаскивание (свободный режим) =================

    private void Grip_MouseDown(object sender, MouseButtonEventArgs e)
    {
        _dragging = true;
        Native.GetCursorPos(out _dragCursor);
        Native.GetWindowRect(_hwnd, out _dragRect);
        Grip.CaptureMouse();
        e.Handled = true;
    }

    private void Grip_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_dragging || !Native.GetCursorPos(out var c)) return;
        Native.SetWindowPos(_hwnd, IntPtr.Zero, _dragRect.Left + c.X - _dragCursor.X, _dragRect.Top + c.Y - _dragCursor.Y, 0, 0, MoveFlags);
    }

    private void Grip_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!_dragging) return;
        _dragging = false;
        Grip.ReleaseMouseCapture();
        e.Handled = true;

        Native.GetWindowRect(_hwnd, out var r);
        var monitor = Monitors.FromPoint(new Native.POINT(r.Left + r.Width / 2, r.Top + r.Height / 2));
        var work = monitor.Work;
        int snap = (int)Math.Round(24 * monitor.Dpi / 96.0);

        DockEdge? edge = null;
        if (r.Left - work.Left < snap) edge = DockEdge.Left;
        else if (work.Right - r.Right < snap) edge = DockEdge.Right;
        else if (r.Top - work.Top < snap) edge = DockEdge.Top;
        else if (work.Bottom - r.Bottom < snap) edge = DockEdge.Bottom;

        Group.DockMonitor = monitor.Device;
        if (edge is { } stuck)
        {
            // Выравнивание — по тому, в какой трети края отпустили трей.
            bool vertical = stuck is DockEdge.Left or DockEdge.Right;
            double center = vertical ? r.Top + r.Height / 2.0 : r.Left + r.Width / 2.0;
            double a0 = vertical ? work.Top : work.Left, a1 = vertical ? work.Bottom : work.Right;
            double third = (a1 - a0) / 3;
            Group.DockAlign = center < a0 + third ? DockAlign.Start : center > a1 - third ? DockAlign.End : DockAlign.Center;
            Group.DockEdge = stuck;
        }
        else
        {
            Group.DockX = r.Left;
            Group.DockY = r.Top;
        }
    }

    // ================= Запуск и меню =================

    private void Launch(LaunchItem item, bool newInstance = false)
    {
        if (Launcher.Open(item, newInstance) is { } error) ShowError(error);
    }

    private void ShowError(string error)
    {
        var tip = new ToolTip { Content = error, PlacementTarget = Root, Placement = PlacementMode.Mouse, StaysOpen = false };
        if (_theme != null) tip.Resources.MergedDictionaries.Add(_theme);
        tip.IsOpen = true;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
        timer.Tick += (_, _) => { timer.Stop(); tip.IsOpen = false; };
        timer.Start();
    }

    private ContextMenu NewMenu(UIElement target)
    {
        var menu = new ContextMenu { PlacementTarget = target, Placement = PlacementMode.MousePoint };
        if (_theme != null) menu.Resources.MergedDictionaries.Add(_theme);
        menu.Opened += (_, _) => _menuOpen = true;
        menu.Closed += (_, _) =>
        {
            _menuOpen = false;
            _lastInside = Environment.TickCount64;
        };
        return menu;
    }

    private void OnItemMenu(LaunchItem item, Button tile)
    {
        var menu = NewMenu(tile);
        PopupWindow.AddRunningItems(menu, item);
        menu.Items.Add(App.MenuItem("", Loc.T("Открыть"), () => Launch(item)));
        if (item.Kind == ItemKind.Path)
            menu.Items.Add(App.MenuItem("", Loc.T("Запустить от имени администратора"), () => Launcher.Launch(item, forceAdmin: true)));
        if (item.Kind != ItemKind.Url)
            menu.Items.Add(App.MenuItem("", Loc.T("Открыть расположение"), () => Launcher.OpenLocation(item)));
        menu.Items.Add(new Separator());
        AddDockItems(menu);
        menu.IsOpen = true;
    }

    private void OnBackgroundMenu(object sender, MouseButtonEventArgs e)
    {
        if (e.Handled) return;
        e.Handled = true;
        var menu = NewMenu(Root);
        menu.Items.Add(App.MenuItem("", Loc.T("Открыть все"), () =>
        {
            foreach (var item in Group.Items.ToList()) Launcher.Launch(item);
        }));
        menu.Items.Add(new Separator());
        AddDockItems(menu);
        menu.IsOpen = true;
    }

    private void AddDockItems(ContextMenu menu)
    {
        const string check = "";
        menu.Items.Add(App.MenuItem("", Loc.T("Изменить группу…"), () => App.Host.ShowEditor(Group.Id)));
        menu.Items.Add(App.MenuItem(Group.DockTopmost ? check : null, Loc.T("Поверх всех окон"), () => Group.DockTopmost = !Group.DockTopmost));
        if (!Free)
            menu.Items.Add(App.MenuItem(Group.DockAutoHide ? check : null, Loc.T("Прятать за край экрана"), () => Group.DockAutoHide = !Group.DockAutoHide));
        menu.Items.Add(new Separator());
        foreach (var (edge, title) in new[]
                 {
                     (DockEdge.Left, Loc.T("Слева")), (DockEdge.Top, Loc.T("Сверху")), (DockEdge.Right, Loc.T("Справа")),
                     (DockEdge.Bottom, Loc.T("Снизу")), (DockEdge.Free, Loc.T("Свободно (перетаскивать)")),
                 })
        {
            var captured = edge;
            menu.Items.Add(App.MenuItem(Group.DockEdge == edge ? check : null, title, () => Group.DockEdge = captured));
        }
        menu.Items.Add(new Separator());
        menu.Items.Add(App.MenuItem("", Loc.T("Убрать с рабочего стола"), () => Group.DockEnabled = false));
    }
}
