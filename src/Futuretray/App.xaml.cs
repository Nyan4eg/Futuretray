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

    public static App Host => (App)Current;

    public HostCommand Initial { get; init; } = new(CommandKind.Editor);
    public ConfigStore Store { get; } = new();
    public IconService Icons { get; } = new();
    public ShortcutManager Shortcuts { get; } = new();
    public UpdateManager Updates { get; private set; } = null!;

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
        ThemeManager.Initialize(Store.Config.Settings);
        ThemeManager.Changed += () =>
        {
            foreach (var g in Store.Config.Groups) g.RefreshIcon();
        };

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
                case CommandKind.Updated:
                    ShowEditor(null, $"Готово! Futuretray обновлён до версии {Updater.CurrentVersionText}");
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
            ShowEditor(null, "Эта группа больше не существует. Открепите её значок с панели задач.");
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
        }
    }

    private void UpdateTray()
    {
        if (Store.Config.Settings.ShowTrayIcon && _tray == null)
        {
            try
            {
                _tray = new TrayIcon("Futuretray — группы на панели задач");
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
        menu.Items.Add(MenuItem("", "Открыть Futuretray", () => ShowEditor()));
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
            menu.Items.Add(MenuItem("", $"Обновить до версии {update.Version.ToString(3)}", () => _ = Updates.InstallAsync()));
        menu.Items.Add(MenuItem("", "Выйти", ExitApp));

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
