using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Futuretray.Core;

namespace Futuretray.UI;

/// <summary>Всплывающее окно группы — то, что открывается по клику на значок на панели задач.</summary>
public partial class PopupWindow : Window
{
    private string _filter = "";
    private Placement _placement;
    private Native.POINT _cursor;
    private ResourceDictionary? _theme;
    private bool _menuOpen;
    private DispatcherTimer? _statusTimer;

    public PopupWindow(Group group)
    {
        Group = group;
        InitializeComponent();
        WindowEffects.InstallChrome(this, 0, 0);

        Tiles.Bind(group);
        Tiles.ItemInvoked += OnItemInvoked;
        Closed += (_, _) => Tiles.Unbind();
        Tiles.ItemMenuRequested += OnItemMenu;

        SourceInitialized += (_, _) => WindowEffects.MakeToolWindow(this);
        Deactivated += (_, _) =>
        {
            if (IsVisible && !_menuOpen) HidePopup();
        };
        PreviewKeyDown += OnPreviewKeyDown;
        PreviewTextInput += OnPreviewTextInput;
        MouseRightButtonUp += OnBackgroundMenu;
        group.Changed += (_, _) =>
        {
            if (IsVisible) Dispatcher.BeginInvoke(() => { ApplyAppearance(); UpdateSize(); Place(); }, DispatcherPriority.Loaded);
        };
    }

    public Group Group { get; }

    public event Action<PopupWindow>? Hidden;

    private IntPtr Hwnd => new WindowInteropHelper(this).EnsureHandle();

    public void ShowAt(Native.POINT cursor)
    {
        _cursor = cursor;
        _placement = TaskbarInfo.Locate(cursor);

        bool alongVertical = _placement.FromTaskbar && _placement.Edge is TaskbarEdge.Left or TaskbarEdge.Right;
        if (Tiles.AlongVerticalTaskbar != alongVertical)
        {
            Tiles.AlongVerticalTaskbar = alongVertical;
            Tiles.Rebuild();
        }

        // Сначала переносим окно на нужный монитор, чтобы WPF применил его масштаб.
        Native.SetWindowPos(Hwnd, IntPtr.Zero, _placement.Available.Left, _placement.Available.Top, 0, 0,
            Native.SWP_NOSIZE | Native.SWP_NOZORDER | Native.SWP_NOACTIVATE);

        ApplyAppearance();
        SetFilter("", focus: false);
        UpdateSize();
        Place();

        Body.Opacity = 0;
        Show();
        WindowEffects.ForceForeground(this);
        Tiles.FocusFirst();
        Animate();

        Dispatcher.BeginInvoke(() => { UpdateSize(); Place(); }, DispatcherPriority.Loaded);
    }

    public void HidePopup()
    {
        if (!IsVisible) return;
        Hide();
        SetFilter("", focus: false);
        Hidden?.Invoke(this);
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

        bool backdrop = WindowEffects.Apply(this, Group.Backdrop, dark);
        SolidLayer.Visibility = backdrop ? Visibility.Collapsed : Visibility.Visible;
        TintLayer.Background = ColorUtil.Brush(ColorUtil.WithAlpha(ColorUtil.Resolve(Group.Color), Group.TintOpacity));
    }

    internal void UpdateSize()
    {
        Body.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var size = Body.DesiredSize;
        Width = Math.Ceiling(Math.Max(size.Width, 120));
        Height = Math.Ceiling(Math.Max(size.Height, 54));
    }

    private void Place()
    {
        double scale = Native.GetDpiForWindow(Hwnd) / 96.0;
        if (scale <= 0) scale = Native.GetMonitorDpi(_placement.Monitor) / 96.0;
        int w = (int)Math.Round(Width * scale);
        int h = (int)Math.Round(Height * scale);
        var pt = _placement.Place(w, h, _cursor, (int)Math.Round(12 * scale));
        Native.SetWindowPos(Hwnd, Native.HWND_TOPMOST, pt.X, pt.Y, w, h, Native.SWP_NOACTIVATE);
    }

    private void Animate()
    {
        const double offset = 16;
        var (fromX, fromY) = (_placement.FromTaskbar ? _placement.Edge : TaskbarEdge.None) switch
        {
            TaskbarEdge.Top => (0.0, -offset),
            TaskbarEdge.Left => (-offset, 0.0),
            TaskbarEdge.Right => (offset, 0.0),
            _ => (0.0, offset),
        };
        var ease = new ExponentialEase { EasingMode = EasingMode.EaseOut, Exponent = 6 };
        var duration = TimeSpan.FromMilliseconds(260);
        var transform = (TranslateTransform)Body.RenderTransform;
        transform.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(fromX, 0, duration) { EasingFunction = ease });
        transform.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(fromY, 0, duration) { EasingFunction = ease });
        Body.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(140)));
    }

    private void OnItemInvoked(LaunchItem item, bool keepOpen, bool newInstance = false)
    {
        var error = Launcher.Open(item, newInstance);
        if (error != null)
        {
            ShowStatus("", error, TimeSpan.FromSeconds(4));
            return;
        }
        if (!keepOpen && Group.CloseAfterLaunch) HidePopup();
    }

    // ---------- Меню ----------

    private ContextMenu NewMenu(UIElement target)
    {
        var menu = new ContextMenu { PlacementTarget = target, Placement = PlacementMode.MousePoint };
        if (_theme != null) menu.Resources.MergedDictionaries.Add(_theme);
        menu.Opened += (_, _) => _menuOpen = true;
        menu.Closed += (_, _) =>
        {
            _menuOpen = false;
            Dispatcher.BeginInvoke(() =>
            {
                if (IsVisible && !IsActive) HidePopup();
            }, DispatcherPriority.Input);
        };
        return menu;
    }

    /// <summary>Пункты для уже открытой программы: ещё одна копия и закрытие её окон.</summary>
    public static void AddRunningItems(ContextMenu menu, LaunchItem item)
    {
        if (RunningApps.Current is not { } running || !running.IsRunning(item)) return;
        menu.Items.Add(App.MenuItem("", Loc.T("Новое окно"), () => Launcher.Launch(item)));
        int count = running.WindowsFor(item).Count;
        menu.Items.Add(App.MenuItem("", count > 1 ? Loc.F("Закрыть все окна ({0})", count) : Loc.T("Закрыть окно"), () => running.CloseWindows(item)));
        menu.Items.Add(new Separator());
    }

    private void OnItemMenu(LaunchItem item, Button tile)
    {
        var menu = NewMenu(tile);
        AddRunningItems(menu, item);
        menu.Items.Add(App.MenuItem("", Loc.T("Открыть"), () => OnItemInvoked(item, false)));
        if (item.Kind == ItemKind.Path)
            menu.Items.Add(App.MenuItem("", Loc.T("Запустить от имени администратора"), () =>
            {
                Launcher.Launch(item, forceAdmin: true);
                HidePopup();
            }));
        if (item.Kind != ItemKind.Url)
            menu.Items.Add(App.MenuItem("", Loc.T("Открыть расположение"), () =>
            {
                HidePopup();
                Launcher.OpenLocation(item);
            }));
        menu.Items.Add(new Separator());
        menu.Items.Add(App.MenuItem("", Loc.T("Изменить группу…"), () =>
        {
            HidePopup();
            App.Host.ShowEditor(Group.Id);
        }));
        menu.IsOpen = true;
    }

    private void OnBackgroundMenu(object sender, MouseButtonEventArgs e)
    {
        if (e.Handled) return;
        e.Handled = true;
        var menu = NewMenu(Root);
        var openAll = App.MenuItem("", Group.HasLayout ? Loc.T("Открыть все и разложить окна") : Loc.T("Открыть все"), OpenAll);
        openAll.InputGestureText = "Ctrl+Enter";
        menu.Items.Add(openAll);
        App.AddFinishItems(menu, Group, HidePopup);
        menu.Items.Add(new Separator());
        menu.Items.Add(App.MenuItem("", Loc.T("Изменить группу…"), () =>
        {
            HidePopup();
            App.Host.ShowEditor(Group.Id);
        }));
        menu.IsOpen = true;
    }

    private void OpenAll()
    {
        HidePopup();
        App.Host.OpenAll(Group);
    }

    // ---------- Клавиатура ----------

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        bool ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        bool shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        switch (e.Key)
        {
            case Key.Escape:
                if (_filter.Length > 0) SetFilter("");
                else HidePopup();
                e.Handled = true;
                break;
            case Key.Back when _filter.Length > 0:
                SetFilter(_filter[..^1]);
                e.Handled = true;
                break;
            case Key.Enter when ctrl && !shift && _filter.Length == 0:
                OpenAll();
                e.Handled = true;
                break;
            case Key.M when ctrl:
                HidePopup();
                Workspaces.MinimizeOthers(Group);
                e.Handled = true;
                break;
            case Key.W when ctrl && shift:
                HidePopup();
                Workspaces.CloseAll(Group);
                e.Handled = true;
                break;
            case Key.Enter when _filter.Length > 0:
                if (Tiles.ApplyFilter(_filter)?.Tag is LaunchItem match) OnItemInvoked(match, ctrl, shift);
                e.Handled = true;
                break;
            default:
                if (_filter.Length == 0 && DigitIndex(e.Key) is { } index)
                {
                    if (index < Group.Items.Count) OnItemInvoked(Group.Items[index], ctrl, shift);
                    e.Handled = true;
                }
                break;
        }
    }

    private void OnPreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        if (string.IsNullOrEmpty(e.Text) || char.IsControl(e.Text[0])) return;
        if (_filter.Length == 0 && (char.IsDigit(e.Text[0]) || char.IsWhiteSpace(e.Text[0]))) return;
        SetFilter(_filter + e.Text);
        e.Handled = true;
    }

    private static int? DigitIndex(Key key) => key switch
    {
        >= Key.D1 and <= Key.D9 => key - Key.D1,
        >= Key.NumPad1 and <= Key.NumPad9 => key - Key.NumPad1,
        _ => null,
    };

    private void SetFilter(string filter, bool focus = true)
    {
        _filter = filter;
        var first = Tiles.ApplyFilter(filter);
        if (filter.Length == 0)
        {
            StatusPill.Visibility = Visibility.Collapsed;
            if (focus) Tiles.FocusFirst();
            return;
        }
        ShowStatus("", first == null ? Loc.F("{0} — нет совпадений", filter) : filter, null);
        if (focus) first?.Focus();
    }

    private void ShowStatus(string glyph, string text, TimeSpan? duration)
    {
        StatusGlyph.Text = glyph;
        StatusText.Text = text;
        StatusPill.Visibility = Visibility.Visible;
        _statusTimer?.Stop();
        if (duration == null) return;
        _statusTimer = new DispatcherTimer { Interval = duration.Value };
        _statusTimer.Tick += (_, _) =>
        {
            _statusTimer.Stop();
            if (_filter.Length == 0) StatusPill.Visibility = Visibility.Collapsed;
        };
        _statusTimer.Start();
    }
}
