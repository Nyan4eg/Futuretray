using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Futuretray.Core;

namespace Futuretray.UI;

public enum SearchKind { GroupItem, Window, App }

/// <summary>Строка выдачи: программа из группы, открытое окно или приложение из меню «Пуск».</summary>
public sealed class SearchResult : Observable
{
    private ImageSource? _icon;

    public SearchResult(SearchKind kind, LaunchItem item, string name, string subtitle, int score, Group? group = null, IntPtr window = default)
    {
        Kind = kind;
        Item = item;
        Name = name;
        Subtitle = subtitle;
        Score = score;
        Group = group;
        Window = window;
    }

    public SearchKind Kind { get; }
    /// <summary>Что запускать и что добавлять в группу (для окна — его программа).</summary>
    public LaunchItem Item { get; }
    public string Name { get; }
    public string Subtitle { get; }
    public int Score { get; }
    public Group? Group { get; }
    public IntPtr Window { get; }

    public string? GroupName => Group?.Name;

    /// <summary>У программ из групп иконка уже в кэше; окна и приложения «Пуска» получают её в фоне.</summary>
    public ImageSource? Icon => Kind == SearchKind.GroupItem ? Item.Icon : _icon;

    public void LoadIcon(Func<bool> needed) =>
        IconService.Current?.GetAsync(Item, image =>
        {
            _icon = image;
            OnPropertyChanged(nameof(Icon));
        }, needed);
    public ImageSource? GroupIcon => Group?.Icon;
    public bool IsRunning => Kind != SearchKind.Window && RunningApps.Current?.IsRunning(Item) == true;
}

/// <summary>
/// Поиск везде: программы из групп, открытые окна и приложения из меню «Пуск».
/// Enter — открыть или переключиться, Ctrl+Enter — добавить в группу.
/// </summary>
public partial class SearchWindow : Window
{
    private const int MaxResults = 40;
    private const int MaxWindows = 8;
    private const int MaxApps = 15;
    private ResourceDictionary? _theme;
    private bool _menuOpen;
    private DispatcherTimer? _statusTimer;
    private volatile int _generation;

    public SearchWindow()
    {
        InitializeComponent();
        WindowEffects.InstallChrome(this, 0, 0);
        SourceInitialized += (_, _) => WindowEffects.MakeToolWindow(this);
        Deactivated += (_, _) =>
        {
            if (IsVisible && !_menuOpen) Hide();
        };
        AppIndex.Updated += () =>
        {
            if (IsVisible && Query.Text.Trim().Length > 0) Refresh();
        };
    }

    private IntPtr Hwnd => new WindowInteropHelper(this).EnsureHandle();

    public void ShowNearCursor()
    {
        Native.GetCursorPos(out var cursor);
        var monitor = Monitors.FromPoint(cursor);
        AppIndex.RefreshIfStale();
        RunningApps.Current?.Refresh();

        bool dark = ThemeManager.PopupDark;
        var dict = ThemeManager.PopupDictionary(dark);
        if (!ReferenceEquals(_theme, dict))
        {
            if (_theme != null) Resources.MergedDictionaries.Remove(_theme);
            Resources.MergedDictionaries.Add(dict);
            _theme = dict;
        }

        // Сначала на нужный монитор — чтобы применился его масштаб.
        Native.SetWindowPos(Hwnd, IntPtr.Zero, monitor.Work.Left, monitor.Work.Top, 0, 0,
            Native.SWP_NOSIZE | Native.SWP_NOZORDER | Native.SWP_NOACTIVATE);
        bool backdrop = WindowEffects.Apply(this, BackdropKind.Acrylic, dark);
        SolidLayer.Visibility = backdrop ? Visibility.Collapsed : Visibility.Visible;

        Query.Text = "";
        StatusText.Visibility = Visibility.Collapsed;
        Refresh();
        UpdateSize();

        double scale = monitor.Dpi / 96.0;
        int w = (int)Math.Round(Width * scale);
        int x = (monitor.Work.Left + monitor.Work.Right - w) / 2;
        int y = monitor.Work.Top + (int)(monitor.Work.Height * 0.2);
        Native.SetWindowPos(Hwnd, Native.HWND_TOPMOST, x, y, 0, 0, Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);

        Show();
        WindowEffects.ForceForeground(this);
        Query.Focus();
        Keyboard.Focus(Query);
    }

    private void UpdateSize()
    {
        Body.Measure(new Size(Width, double.PositiveInfinity));
        Height = Math.Ceiling(Body.DesiredSize.Height + Body.Margin.Top + Body.Margin.Bottom);
    }

    private void Query_TextChanged(object sender, TextChangedEventArgs e) => Refresh();

    private void Refresh()
    {
        var query = Query.Text.Trim();
        var groups = App.Host.Store.Config.Groups;
        var results = new List<SearchResult>();

        // 1. Программы из групп — свои, поэтому чуть выше остальных.
        int order = 0;
        foreach (var group in groups)
        {
            foreach (var item in group.Items)
            {
                int score = Score(item.Name, group.Name, query);
                // Порядок в группах — второй критерий, чтобы выдача была стабильной.
                if (score > 0) results.Add(new SearchResult(SearchKind.GroupItem, item, item.Name,
                    item.Kind == ItemKind.Url ? item.Target : $"{group.Name} · {item.KindLabel}", (score + 5) * 10000 - order, group));
                order++;
            }
        }

        if (query.Length > 0)
        {
            // 2. Открытые окна: по заголовку и по имени программы («тел» найдёт окно Telegram с чатом).
            if (RunningApps.Current is { } running)
            {
                var windows = new List<SearchResult>();
                foreach (var (w, z) in running.Windows.Select((w, z) => (w, z)))
                {
                    var title = AppIndex.WindowTitle(w.Handle);
                    var app = AppIndex.AppName(w.Path);
                    int score = WindowScore(title, app, query);
                    if (score == 0) continue;
                    if (WindowItem(w, app) is not { } item) continue;
                    windows.Add(new SearchResult(SearchKind.Window, item, title,
                        Loc.F("Окно · {0}", app), score * 10000 - z, window: w.Handle));
                }
                results.AddRange(windows.OrderByDescending(r => r.Score).Take(MaxWindows));
            }

            // 3. Приложения из меню «Пуск», которых нет в группах.
            var inGroups = groups.SelectMany(g => g.Items).ToList();
            var apps = new List<SearchResult>();
            foreach (var app in AppIndex.Apps)
            {
                int score = Score(app.Name, "", query);
                if (score == 0 || IsInGroups(app, inGroups)) continue;
                var item = ItemFactory.FromInstalled(app);
                apps.Add(new SearchResult(SearchKind.App, item, app.Name,
                    app.FilePath == null ? Loc.T("Приложение из меню «Пуск»") : Loc.F("Приложение · {0}", app.FilePath), (score - 3) * 10000));
            }
            results.AddRange(apps.OrderByDescending(r => r.Score).ThenBy(r => r.Name.Length).Take(MaxApps));
        }

        var top = results.OrderByDescending(r => r.Score).Take(MaxResults).ToList();
        int generation = ++_generation;
        foreach (var result in top.Where(r => r.Kind != SearchKind.GroupItem))
            result.LoadIcon(() => generation == _generation); // пока печатают дальше, старые иконки не грузим
        Results.ItemsSource = top;
        Results.SelectedIndex = top.Count > 0 ? 0 : -1;
        EmptyText.Visibility = top.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        Results.Visibility = top.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        if (!IsVisible) return;
        // Второй замер — после того как список пересоздал строки, иначе окно не уменьшается.
        Dispatcher.BeginInvoke(UpdateSize, DispatcherPriority.Loaded);
        Dispatcher.BeginInvoke(UpdateSize, DispatcherPriority.ContextIdle);
    }

    private readonly Dictionary<string, LaunchItem> _windowItems = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Что запускать и добавлять в группу для окна: обычную программу — по exe, приложение Store и окна,
    /// которые рисует ApplicationFrameHost (Параметры и т. п.), — по AppUserModelID. Без того и другого — ничего.
    /// Кэшируется: на каждое нажатие клавиши читать версию exe незачем.
    /// </summary>
    private LaunchItem? WindowItem(AppWindow w, string app)
    {
        var appId = w.AppId ?? w.ProcessAppId;
        bool hosted = w.Path == null || w.Path.Contains(@"\WindowsApps\", StringComparison.OrdinalIgnoreCase) ||
                      w.Path.EndsWith(@"\ApplicationFrameHost.exe", StringComparison.OrdinalIgnoreCase);
        if (hosted && appId == null) return null;
        var key = hosted ? "app|" + appId : "exe|" + w.Path;
        if (!_windowItems.TryGetValue(key, out var item))
            _windowItems[key] = item = hosted
                ? new LaunchItem { Kind = ItemKind.ShellApp, Target = appId!, Name = app }
                : ItemFactory.FromPath(w.Path!);
        return item;
    }

    private static bool IsInGroups(InstalledApp app, List<LaunchItem> items) => items.Any(i =>
        string.Equals(i.Name, app.Name, StringComparison.CurrentCultureIgnoreCase) ||
        (app.FilePath != null && string.Equals(i.Target, app.FilePath, StringComparison.OrdinalIgnoreCase)) ||
        (i.Kind == ItemKind.ShellApp && string.Equals(i.Target, app.ParsingName, StringComparison.OrdinalIgnoreCase)));

    /// <summary>0 — не подходит; больше — лучше.</summary>
    public static int Score(string name, string groupName, string query)
    {
        if (query.Length == 0) return 1;
        var cmp = StringComparison.CurrentCultureIgnoreCase;
        if (name.StartsWith(query, cmp)) return 100;
        var words = name.Split(new[] { ' ', '-', '_', '.', '(', ')' }, StringSplitOptions.RemoveEmptyEntries);
        if (words.Any(w => w.StartsWith(query, cmp))) return 80;
        if (name.Contains(query, cmp)) return 60;
        if (words.Length > 1 && string.Concat(words.Select(w => w[0])).StartsWith(query, cmp)) return 50; // «vsc» → Visual Studio Code
        if (groupName.Length > 0 && groupName.Contains(query, cmp)) return 20;
        return 0;
    }

    /// <summary>Окно ищется по заголовку и по имени программы; совпадение по программе чуть слабее.</summary>
    public static int WindowScore(string title, string appName, string query) =>
        Math.Max(Score(title, "", query), Math.Max(0, Score(appName, "", query) - 10));

    private void Query_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        int count = Results.Items.Count;
        bool ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        switch (e.Key)
        {
            case Key.Escape:
                if (Query.Text.Length > 0) Query.Text = "";
                else Hide();
                e.Handled = true;
                break;
            case Key.Down when count > 0:
                Results.SelectedIndex = (Results.SelectedIndex + 1) % count;
                Results.ScrollIntoView(Results.SelectedItem);
                e.Handled = true;
                break;
            case Key.Up when count > 0:
                Results.SelectedIndex = (Results.SelectedIndex - 1 + count) % count;
                Results.ScrollIntoView(Results.SelectedItem);
                e.Handled = true;
                break;
            case Key.Enter when ctrl:
                ShowAddMenu(Results.ItemContainerGenerator.ContainerFromItem(Results.SelectedItem) as UIElement);
                e.Handled = true;
                break;
            case Key.Enter:
                OpenSelected(Keyboard.Modifiers.HasFlag(ModifierKeys.Shift));
                e.Handled = true;
                break;
        }
    }

    private void Results_MouseUp(object sender, MouseButtonEventArgs e) =>
        OpenSelected(Keyboard.Modifiers.HasFlag(ModifierKeys.Shift));

    private void Results_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (ItemsControl.ContainerFromElement(Results, (DependencyObject)e.OriginalSource) is not ListBoxItem row) return;
        row.IsSelected = true;
        ShowAddMenu(row, withOpen: true);
        e.Handled = true;
    }

    private void OpenSelected(bool newInstance)
    {
        if (Results.SelectedItem is not SearchResult result) return;
        // Сначала открываем (пока у нас есть право вывести окно вперёд), потом прячемся.
        if (result.Kind == SearchKind.Window && !newInstance && Native.IsWindow(result.Window))
        {
            RunningApps.Activate(result.Window);
            Hide();
            return;
        }
        var error = Launcher.Open(result.Item, newInstance);
        if (error != null)
        {
            ShowStatus(error);
            return;
        }
        Hide();
    }

    // ================= Добавить в группу =================

    private void ShowAddMenu(UIElement? target, bool withOpen = false)
    {
        if (Results.SelectedItem is not SearchResult result) return;
        var menu = new ContextMenu
        {
            PlacementTarget = target ?? Results,
            Placement = target != null ? PlacementMode.Bottom : PlacementMode.MousePoint,
        };
        if (_theme != null) menu.Resources.MergedDictionaries.Add(_theme);
        menu.Opened += (_, _) => _menuOpen = true;
        menu.Closed += (_, _) =>
        {
            _menuOpen = false;
            Dispatcher.BeginInvoke(() =>
            {
                if (IsVisible && !IsActive) Hide();
                else Query.Focus();
            }, DispatcherPriority.Input);
        };

        if (withOpen)
        {
            menu.Items.Add(App.MenuItem("", result.Kind == SearchKind.Window ? Loc.T("Переключиться") : Loc.T("Открыть"), () => OpenSelected(false)));
            menu.Items.Add(new Separator());
        }
        var header = new MenuItem { Header = Loc.T("Добавить в группу"), IsEnabled = false };
        menu.Items.Add(header);
        var groups = App.Host.Store.Config.Groups;
        if (groups.Count == 0)
            menu.Items.Add(new MenuItem { Header = Loc.T("Сначала создайте группу в редакторе"), IsEnabled = false });
        foreach (var group in groups)
        {
            var item = App.MenuItem(null, group.Name, () => AddToGroup(result, group));
            item.Icon = new Image { Source = group.Icon, Width = 16, Height = 16 };
            if (group == result.Group) item.IsEnabled = false;
            menu.Items.Add(item);
        }
        menu.IsOpen = true;
        if (menu.Items.OfType<MenuItem>().FirstOrDefault(i => i.IsEnabled && i != header && !withOpen) is { } first)
            Dispatcher.BeginInvoke(() => first.Focus(), DispatcherPriority.Input);
    }

    internal void AddToGroup(SearchResult result, Group group)
    {
        var item = result.Item;
        if (group.Items.Any(i => string.Equals(i.Target, item.Target, StringComparison.OrdinalIgnoreCase) && i.Kind == item.Kind))
        {
            ShowStatus(Loc.F("«{0}» уже есть в группе «{1}»", item.Name, group.Name));
            return;
        }
        var copy = item.Clone();
        if (result.Kind == SearchKind.Window && copy.Kind == ItemKind.Path && string.IsNullOrWhiteSpace(copy.Name))
            copy.Name = AppIndex.AppName(copy.Target);
        group.Items.Add(copy);
        ShowStatus(Loc.F("«{0}» добавлено в группу «{1}»", copy.Name, group.Name));
        Refresh();
    }

    private void ShowStatus(string text)
    {
        StatusText.Text = text;
        StatusText.Visibility = Visibility.Visible;
        _statusTimer?.Stop();
        _statusTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
        _statusTimer.Tick += (_, _) =>
        {
            _statusTimer.Stop();
            StatusText.Visibility = Visibility.Collapsed;
            UpdateSize();
        };
        _statusTimer.Start();
        Dispatcher.BeginInvoke(UpdateSize, DispatcherPriority.Loaded);
    }
}
