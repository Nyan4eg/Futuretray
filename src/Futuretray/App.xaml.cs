using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Futuretray.Core;
using Futuretray.UI;

namespace Futuretray;

/// <summary>Фоновый хост: держит конфиг, иконки и заранее «прогретые» окна групп.</summary>
public partial class App : Application
{
    private readonly IpcServer _ipc = new();
    private readonly Dictionary<string, PopupWindow> _popups = new();
    private readonly Dictionary<string, long> _lastHidden = new();
    private MainWindow? _editor;
    private TrayIcon? _tray;
    private DockManager? _docks;
    private SearchWindow? _search;
    private bool _hotkeysQueued;

    public static App Host => (App)Current;

    public HostCommand Initial { get; init; } = new(CommandKind.Editor);
    public ConfigStore Store { get; } = new();
    public IconService Icons { get; } = new();
    public ShortcutManager Shortcuts { get; } = new();
    public UpdateManager Updates { get; private set; } = null!;
    public RunningApps Running { get; private set; } = null!;
    public HotkeyManager Hotkeys { get; private set; } = null!;
    public FolderSync Folders { get; private set; } = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += (_, args) =>
        {
            Log.Error(args.Exception, "UI");
            args.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) => Log.Error(args.ExceptionObject as Exception, "Domain");
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Log.Error(args.Exception, "Task");
            args.SetObserved();
        };

        ToolTipService.InitialShowDelayProperty.OverrideMetadata(typeof(FrameworkElement), new FrameworkPropertyMetadata(450));

        Store.Load();
        // Портативная версия по умолчанию не прописывается в автозапуск: её часто носят на флешке.
        if (Store.IsNew && AppPaths.Portable) Store.Config.Settings.RunAtStartup = false;
        PendingWhatsNew = WhatsNew.Pending(Store.Config.Settings.LastSeenVersion, Store.IsNew, Updater.CurrentVersion, WhatsNew.Load());
        if (PendingWhatsNew.Count == 0) Store.Config.Settings.LastSeenVersion = Updater.CurrentVersionText;
        Store.ExternalChange += OnExternalConfigChange;
        if (Workspaces.MigrateLegacy(Store.Config.Groups, Workspaces.CurrentProfile)) Store.ScheduleSave();
        Loc.Init(Store.Config.Settings.Language);
#if DEBUG
        if (Environment.GetEnvironmentVariable("FUTURETRAY_LANG") == "en") Loc.Init(UiLanguage.English);
#endif
        ThemeManager.Initialize(Store.Config.Settings);
        ThemeManager.Changed += () =>
        {
            foreach (var g in Store.Config.Groups) g.RefreshIcon();
        };

        Running = new RunningApps();
        Hotkeys = new HotkeyManager();
        Favicons.Enabled = () => Store.Config.Settings.DownloadFavicons;
        Favicons.Ready += OnFaviconReady;
        Updates = new UpdateManager(Store.Config.Settings);
        Updates.Found += update => _editor?.ShowUpdateToast(update);

        Store.GroupChanged += g => Shortcuts.Schedule(g);
        Store.Config.Groups.CollectionChanged += OnGroupsChanged;
        Store.Config.Settings.PropertyChanged += OnSettingsChanged;

#if DEBUG
        if (DebugSnapshot.Directory != null)
        {
            DebugSnapshot.SeedDemo(Store);
            DebugSnapshot.Run(this);
            return;
        }
#endif
        _ipc.CommandReceived += command => Dispatcher.BeginInvoke(() => Handle(command));
        _ipc.Start();

        UpdateTray();
        _docks = new DockManager(Store);
        _docks.SyncAll();
        Folders = new FolderSync(Store);
        Store.GroupChanged += _ => QueueHotkeys();
        Store.Config.Groups.CollectionChanged += (_, _) => QueueHotkeys();
        ApplyHotkeys();
        Handle(Initial);
        Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, AfterStartup);
    }

    private void AfterStartup()
    {
        try
        {
            AutoStart.Sync(Store.Config.Settings.RunAtStartup);
            Shortcuts.EnsureAppShortcut();
            Icons.Prewarm(Store.Config.Groups.SelectMany(g => g.Items));
            _ = AppIndex.RefreshAsync();
            if (AppPaths.Portable) Updater.CleanupOldExe();
            Shortcuts.RepairAll(Store.Config.Groups.ToList());
            Updates.Start();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "After startup");
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Store.SaveNow();
        _docks?.CloseAll();
        _tray?.Dispose();
        Hotkeys?.Dispose();
        base.OnExit(e);
    }

    public void Handle(HostCommand command)
    {
        try
        {
            switch (command.Kind)
            {
                case CommandKind.Popup when command.GroupId != null:
                    ShowPopup(command.GroupId, new Native.POINT(command.X, command.Y));
                    break;
                case CommandKind.Exit:
                    ExitApp();
                    break;
                case CommandKind.Background:
                    break;
                case CommandKind.Startup:
                    OpenLoginGroups();
                    break;
                case CommandKind.Launch:
                    LaunchFromJumpList(command.GroupId, command.ItemId);
                    break;
                case CommandKind.Arrange when Store.Find(command.GroupId) is { } arrangeGroup:
                    OpenAll(arrangeGroup);
                    break;
                case CommandKind.CloseAll when Store.Find(command.GroupId) is { } closeGroup:
                    Workspaces.CloseAll(closeGroup);
                    break;
                case CommandKind.Focus when Store.Find(command.GroupId) is { } focusGroup:
                    Workspaces.MinimizeOthers(focusGroup);
                    break;
                case CommandKind.Updated:
                    // Что нового — покажет сам редактор; без него хотя бы скажем, что обновились.
                    ShowEditor(null, PendingWhatsNew.Count > 0 ? null : Loc.F("Готово! Futuretray обновлён до версии {0}", Updater.CurrentVersionText));
                    break;
                default:
                    ShowEditor(command.GroupId);
                    break;
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Handle " + command);
        }
    }

    public void ShowPopup(string groupId, Native.POINT cursor)
    {
        var group = Store.Find(groupId);
        if (group == null)
        {
            ShowEditor(null, Loc.T("Эта группа больше не существует. Открепите её значок с панели задач."));
            return;
        }

        if (_popups.TryGetValue(group.Id, out var popup) && popup.IsVisible)
        {
            popup.HidePopup();
            return;
        }

        // Клик по значку на панели сначала закрывает окно (потеря фокуса), а затем снова
        // запускает группу. Такой повторный запуск считаем «закрыть», а не «открыть заново».
        if (_lastHidden.TryGetValue(group.Id, out var hiddenAt) && Environment.TickCount64 - hiddenAt < 450)
            return;

        foreach (var other in _popups.Values.Where(p => p.IsVisible)) other.HidePopup();

        if (popup == null)
        {
            popup = new PopupWindow(group);
            popup.Hidden += p => _lastHidden[p.Group.Id] = Environment.TickCount64;
            _popups[group.Id] = popup;
        }
        popup.ShowAt(cursor);
    }

    public void ShowEditor(string? groupId = null, string? message = null)
    {
        if (_editor == null)
        {
            _editor = new MainWindow();
            _editor.Closed += (_, _) => _editor = null;
        }
        _editor.Show();
        if (_editor.WindowState == WindowState.Minimized) _editor.WindowState = WindowState.Normal;
        WindowEffects.ForceForeground(_editor);
        if (groupId != null) _editor.SelectGroup(groupId);
        if (message != null) _editor.ShowToast(message);
        if (PendingWhatsNew.Count > 0)
        {
            _editor.ShowWhatsNew(PendingWhatsNew);
            MarkWhatsNewSeen();
        }
    }

    /// <summary>
    /// «Открыть все»: запустить недостающие программы по порядку (с паузами) и, если у группы
    /// запомнено рабочее пространство, разложить окна.
    /// </summary>
    public void OpenAll(Group group)
    {
        Workspaces.OpenAll(group, result =>
        {
            if (_editor?.IsVisible == true) _editor.ShowToast(Workspaces.Describe(result));
        }, Store.Config.Groups);
    }

    /// <summary>Пункты «Свернуть остальное» и «Закрыть все» для меню окна группы и трея.</summary>
    public static void AddFinishItems(ContextMenu menu, Group group, Action? before = null)
    {
        var focus = MenuItem("", Loc.T("Свернуть остальное"), () => { before?.Invoke(); Workspaces.MinimizeOthers(group); });
        focus.InputGestureText = "Ctrl+M";
        menu.Items.Add(focus);
        if (!Workspaces.HasOpenWindows(group)) return;
        var close = MenuItem("", Loc.T("Закрыть все программы группы"), () => { before?.Invoke(); Workspaces.CloseAll(group); });
        close.InputGestureText = "Ctrl+Shift+W";
        menu.Items.Add(close);
    }

    private bool _loginGroupsOpened;

    /// <summary>Вход в Windows: открываем группы с «Открывать при входе», дав рабочему столу прогрузиться.</summary>
    private void OpenLoginGroups()
    {
        if (_loginGroupsOpened) return;
        _loginGroupsOpened = true;
        var groups = Store.Config.Groups.Where(g => g.OpenAtLogin && g.Items.Count > 0).ToList();
        if (groups.Count == 0) return;
        var timer = new DispatcherTimer { Interval = LoginDelay };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            foreach (var g in groups) OpenAll(g);
        };
        timer.Start();
    }

    private static readonly TimeSpan LoginDelay = TimeSpan.FromSeconds(5);

    /// <summary>Пункт списка переходов закреплённой группы: открыть программу (или переключиться на неё).</summary>
    private void LaunchFromJumpList(string? groupId, string? itemId)
    {
        var group = Store.Find(groupId);
        var item = group?.Items.FirstOrDefault(i => i.Id == itemId);
        if (group == null || item == null)
        {
            ShowEditor(group?.Id, Loc.T("Этого приложения больше нет в группе — список переходов обновится сам."));
            return;
        }
        if (Launcher.Open(item) is { } error) ShowEditor(group.Id, error);
    }

    public void ShowSearch()
    {
        _search ??= new SearchWindow();
        if (_search.IsVisible) _search.Hide();
        else _search.ShowNearCursor();
    }

    private void QueueHotkeys()
    {
        if (_hotkeysQueued) return;
        _hotkeysQueued = true;
        Dispatcher.BeginInvoke(() =>
        {
            _hotkeysQueued = false;
            ApplyHotkeys();
        }, DispatcherPriority.Background);
    }

    private void ApplyHotkeys()
    {
        var bindings = new List<(string, string?, Action)> { ("search", Store.Config.Settings.SearchHotkey, ShowSearch) };
        foreach (var g in Store.Config.Groups)
        {
            var id = g.Id;
            bindings.Add((id, g.Hotkey, () =>
            {
                Native.GetCursorPos(out var cursor);
                ShowPopup(id, cursor);
            }));
        }
        Hotkeys.Apply(bindings);
    }

    private void OnFaviconReady(string host)
    {
        foreach (var g in Store.Config.Groups)
        {
            var items = g.Items.Where(i => i.Kind == ItemKind.Url && Favicons.HostOf(i.Target) == host).ToList();
            if (items.Count == 0) continue;
            foreach (var item in items) item.RefreshIcon();
            g.Touch();
        }
    }

    /// <summary>Перезапуск (например, после смены языка): новая копия дождётся, пока эта завершится.</summary>
    /// <summary>«Что нового», которое редактор покажет при следующем открытии.</summary>
    public List<WhatsNew.Section> PendingWhatsNew { get; private set; } = [];

    public void MarkWhatsNewSeen()
    {
        PendingWhatsNew = [];
        Store.Config.Settings.LastSeenVersion = Updater.CurrentVersionText;
    }

    /// <summary>Группы поменялись на другом компьютере: перечитываем их перезапуском (при открытом редакторе — по кнопке).</summary>
    private void OnExternalConfigChange()
    {
        if (_editor?.IsVisible == true)
            _editor.ShowToast(Loc.T("Группы изменились на другом компьютере"), Loc.T("Обновить"), Restart);
        else
            Restart();
    }

    public void Restart()
    {
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(ShortcutManager.ExePath, "--restart") { UseShellExecute = true })?.Dispose();
        ExitApp();
    }

    public void ExitApp()
    {
        _docks?.CloseAll();
        foreach (var popup in _popups.Values) popup.Close();
        Shutdown();
    }

    private void OnGroupsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems == null) return;
        foreach (Group g in e.OldItems)
        {
            if (Store.Config.Groups.Contains(g)) continue; // перемещение
            if (_popups.Remove(g.Id, out var popup)) popup.Close();
        }
    }

    private void OnSettingsChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(AppSettings.RunAtStartup):
                AutoStart.Sync(Store.Config.Settings.RunAtStartup);
                break;
            case nameof(AppSettings.ShowTrayIcon):
                UpdateTray();
                break;
            case nameof(AppSettings.SearchHotkey):
                QueueHotkeys();
                break;
        }
    }

    private void UpdateTray()
    {
        if (Store.Config.Settings.ShowTrayIcon && _tray == null)
        {
            try
            {
                _tray = new TrayIcon(Loc.T("Futuretray — группы на панели задач"));
                _tray.Click += () => ShowEditor();
                _tray.RightClick += ShowTrayMenu;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Tray");
            }
        }
        else if (!Store.Config.Settings.ShowTrayIcon && _tray != null)
        {
            _tray.Dispose();
            _tray = null;
        }
    }

    private void ShowTrayMenu()
    {
        var menu = new ContextMenu { Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint };
        menu.Items.Add(MenuItem("", Loc.T("Открыть Futuretray"), () => ShowEditor()));
        var searchItem = MenuItem("", Loc.T("Поиск по группам"), ShowSearch);
        if (Core.Hotkey.TryParse(Store.Config.Settings.SearchHotkey, out var searchHotkey))
            searchItem.InputGestureText = searchHotkey.Display;
        menu.Items.Add(searchItem);
        if (Store.Config.Groups.Count > 0)
        {
            menu.Items.Add(new Separator());
            Native.GetCursorPos(out var cursor);
            foreach (var g in Store.Config.Groups.Take(12))
            {
                var item = MenuItem(null, g.Name, () => ShowPopup(g.Id, cursor));
                item.Icon = null;
                menu.Items.Add(item);
            }
        }
        menu.Items.Add(new Separator());
        if (Updates.Available is { } update)
            menu.Items.Add(MenuItem("", Loc.F("Обновить до версии {0}", update.Version.ToString(3)), () => _ = Updates.InstallAsync()));
        menu.Items.Add(MenuItem("", Loc.T("Выйти"), ExitApp));

        if (_tray != null) Native.SetForegroundWindow(_tray.Handle);
        menu.IsOpen = true;
    }

    public static MenuItem MenuItem(string? glyph, string header, Action action)
    {
        var item = new MenuItem { Header = header };
        if (glyph != null) Ui.SetGlyph(item, glyph);
        item.Click += (_, _) => action();
        return item;
    }
}
