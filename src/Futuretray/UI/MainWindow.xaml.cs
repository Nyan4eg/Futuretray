using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shell;
using System.Windows.Threading;
using Futuretray.Core;
using Microsoft.Win32;

namespace Futuretray.UI;

public partial class MainWindow : Window
{
    private enum View { Editor, Settings, Empty }

    private readonly App _app = App.Host;
    private Group? _group;
    private List<InstalledApp>? _installed;
    private ICollectionView? _installedView;
    private DispatcherTimer? _toastTimer;
    private Action? _toastAction;
    private Action? _toastExpire;
    private DispatcherTimer? _pinWatch;

    public MainWindow()
    {
        InitializeComponent();
        WindowEffects.InstallChrome(this, TitleBarHeight, 6);
        ApplyTitleBarHeight(maximized: false);

        GroupList.ItemsSource = Groups;
        SettingsPanel.DataContext = _app.Store.Config.Settings;
        UpdatesCard.DataContext = _app.Updates;
        // Строку формата со скобками {0} надёжнее задать здесь, а не через {ui:T} в XAML.
        InstallUpdateButton.ContentStringFormat = Loc.T("Обновить до {0}");
        Loaded += (_, _) =>
        {
            if (_app.Updates.Available is { } update) ShowUpdateToast(update);
        };
        VersionText.Text = Loc.F("Версия {0} · группы приложений на панели задач", Updater.CurrentVersionText);

        Groups.CollectionChanged += OnGroupsChanged;
        ThemeManager.Changed += ApplyEffects;
        _app.Hotkeys.FailuresChanged += UpdateHotkeyErrors;
        _app.Store.Config.Settings.PropertyChanged += OnSettingsChanged;
        Loaded += (_, _) => UpdateHotkeyErrors();
        Closed += (_, _) =>
        {
            Groups.CollectionChanged -= OnGroupsChanged;
            ThemeManager.Changed -= ApplyEffects;
            _app.Hotkeys.FailuresChanged -= UpdateHotkeyErrors;
            _app.Store.Config.Settings.PropertyChanged -= OnSettingsChanged;
            _pinWatch?.Stop();
            FlushToast();
            BindGroup(null);
        };
        SourceInitialized += (_, _) => ApplyEffects();
        StateChanged += (_, _) =>
        {
            bool max = WindowState == WindowState.Maximized;
            RootGrid.Margin = max ? new Thickness(7) : new Thickness(0);
            ApplyTitleBarHeight(max);
            MaximizeButton.Content = max ? "" : "";
        };
        PreviewKeyDown += OnWindowKeyDown;

        DragReorder.Attach(GroupList, () => Groups, "Futuretray.Group");
        DragReorder.Attach(ItemsList, () => _group?.Items, "Futuretray.Item");

        if (Groups.Count > 0) GroupList.SelectedIndex = 0;
        else ShowView(View.Empty);
    }

    private ObservableCollection<Group> Groups => _app.Store.Config.Groups;

    private LaunchItem? SelectedItem => ItemsList.SelectedItem as LaunchItem;

    // ================= Окно =================

    private void ApplyEffects()
    {
        bool backdrop = WindowEffects.Apply(this, BackdropKind.Mica, ThemeManager.AppDark);
        if (backdrop) Background = Brushes.Transparent;
        else SetResourceReference(BackgroundProperty, "Brush.WindowFallback");
        ContentLayer.SetResourceReference(Border.BackgroundProperty, backdrop ? "Brush.Layer" : "Brush.LayerSolid");
        CaptionButtons.Visibility = WindowEffects.SupportsBackdrop ? Visibility.Collapsed : Visibility.Visible;
        if (_group != null) UpdatePreviewTint();
    }

    /// <summary>
    /// Высота системного заголовка (заголовок + рамка). Когда в Windows включён цвет акцента в заголовках,
    /// система закрашивает ровно эту полосу — наш заголовок и начало содержимого должны совпасть с ней.
    /// </summary>
    private static double TitleBarHeight =>
        // 23 (заголовок) + 4 (рамка) + 4 (SM_CXPADDEDBORDER, его WPF не учитывает) = 31 при 100%.
        SystemParameters.WindowCaptionHeight + SystemParameters.WindowResizeBorderThickness.Top +
        Native.GetSystemMetricsForDpi(92 /* SM_CXPADDEDBORDER */, 96);

    private void ApplyTitleBarHeight(bool maximized)
    {
        // У развёрнутого окна рамка уходит за край экрана, и видимая полоса заголовка ниже.
        double height = maximized ? SystemParameters.WindowCaptionHeight : TitleBarHeight;
        TitleRow.Height = new GridLength(height);
        ContentLayer.Margin = new Thickness(0, height, 0, 0);
        foreach (var button in CaptionButtons.Children.OfType<Button>()) button.Height = height;
        if (WindowChrome.GetWindowChrome(this) is { } chrome) chrome.CaptionHeight = height;
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) => SystemCommands.MinimizeWindow(this);

    private void Maximize_Click(object sender, RoutedEventArgs e)
    {
        if (WindowState == WindowState.Maximized) SystemCommands.RestoreWindow(this);
        else SystemCommands.MaximizeWindow(this);
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void OnWindowKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && Overlay.Visibility == Visibility.Visible)
        {
            CloseOverlay();
            e.Handled = true;
        }
        else if (e.Key == Key.N && Keyboard.Modifiers == ModifierKeys.Control)
        {
            NewGroup_Click(this, new RoutedEventArgs());
            e.Handled = true;
        }
        else if (e.Key == Key.F2 && _group != null)
        {
            GroupNameBox.Focus();
            GroupNameBox.SelectAll();
            e.Handled = true;
        }
    }

    private void ShowView(View view)
    {
        EditorScroll.Visibility = view == View.Editor ? Visibility.Visible : Visibility.Collapsed;
        SettingsScroll.Visibility = view == View.Settings ? Visibility.Visible : Visibility.Collapsed;
        EmptyState.Visibility = view == View.Empty ? Visibility.Visible : Visibility.Collapsed;
        if (view == View.Settings) SettingsNav.SetResourceReference(BackgroundProperty, "Brush.SubtleSelected");
        else SettingsNav.Background = Brushes.Transparent;
    }

    // ================= Группы =================

    public void SelectGroup(string id)
    {
        var group = Groups.FirstOrDefault(g => string.Equals(g.Id, id, StringComparison.OrdinalIgnoreCase));
        if (group != null)
        {
            GroupList.SelectedItem = group;
            GroupList.ScrollIntoView(group);
        }
    }

    private void GroupList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (GroupList.SelectedItem is Group g)
        {
            BindGroup(g);
            ShowView(View.Editor);
            EditorScroll.ScrollToTop();
        }
        else if (SettingsScroll.Visibility != Visibility.Visible)
        {
            BindGroup(null);
            ShowView(Groups.Count == 0 ? View.Empty : View.Editor);
        }
    }

    private void BindGroup(Group? group)
    {
        if (_group != null) _group.PropertyChanged -= OnGroupPropertyChanged;
        _group = group;
        EditorPanel.DataContext = group;
        Overlay.DataContext = group;
        ItemsList.SelectedIndex = -1;
        UpdateItemCard();
        if (group == null)
        {
            PreviewTiles.Unbind();
            return;
        }
        group.PropertyChanged += OnGroupPropertyChanged;
        PreviewTiles.Bind(group);
        BuildSwatches();
        UpdateIconPanels();
        UpdatePreviewTint();
        BuildMonitorChoices();
        UpdateHotkeyErrors();
        UpdateLayoutStatus();
    }

    private void BuildMonitorChoices()
    {
        DockMonitorPanel.Children.Clear();
        var monitors = Monitors.All();
        DockMonitorRow.Visibility = monitors.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
        if (_group == null || monitors.Count <= 1) return;

        var current = Monitors.Find(_group.DockMonitor);
        for (int i = 0; i < monitors.Count; i++)
        {
            var monitor = monitors[i];
            var choice = new RadioButton
            {
                Style = (Style)FindResource("Segment"),
                GroupName = "DockMonitor",
                Content = monitor.IsPrimary ? Loc.F("{0} · основной", i + 1) : $"{i + 1}",
                ToolTip = $"{monitor.Device}  {monitor.Bounds.Width}×{monitor.Bounds.Height}",
                IsChecked = monitor.Handle == current.Handle,
            };
            choice.Checked += (_, _) =>
            {
                // Основной храним как null — трей пойдёт за основным монитором, если его поменять.
                if (_group != null) _group.DockMonitor = monitor.IsPrimary ? null : monitor.Device;
            };
            DockMonitorPanel.Children.Add(choice);
        }
    }

    private void OnGroupPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(Group.Color):
                Dispatcher.BeginInvoke(BuildSwatches);
                UpdatePreviewTint();
                break;
            case nameof(Group.TintOpacity):
                UpdatePreviewTint();
                break;
            case nameof(Group.DockMonitor):
                Dispatcher.BeginInvoke(BuildMonitorChoices);
                break;
            case nameof(Group.DockEnabled) when _group is { DockEnabled: true, DockPlaced: false } g:
                var primary = Monitors.Find(g.DockMonitor);
                var center = new Native.POINT((primary.Bounds.Left + primary.Bounds.Right) / 2, (primary.Bounds.Top + primary.Bounds.Bottom) / 2);
                g.PlaceDockNextToTaskbar(TaskbarInfo.Locate(center).Edge);
                break;
            case nameof(Group.IconMode):
                if (_group?.IconMode == GroupIconMode.Custom && _group.CustomIconPath == null)
                    Dispatcher.BeginInvoke(() => PickGroupIcon_Click(this, new RoutedEventArgs()));
                UpdateIconPanels();
                break;
            case nameof(Group.Icon):
            case nameof(Group.IconItemId):
            case nameof(Group.CustomIconPath):
                UpdateIconPanels();
                break;
        }
    }

    private void OnGroupsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (Groups.Count == 0 && SettingsScroll.Visibility != Visibility.Visible) ShowView(View.Empty);
    }

    private void NewGroup_Click(object sender, RoutedEventArgs e)
    {
        var palette = ColorUtil.Palette.Skip(1).ToArray();
        var group = new Group
        {
            Name = UniqueName(Loc.T("Новая группа")),
            Color = palette[Groups.Count % palette.Length],
        };
        Groups.Add(group);
        GroupList.SelectedItem = group;
        Dispatcher.BeginInvoke(() =>
        {
            GroupNameBox.Focus();
            GroupNameBox.SelectAll();
        }, DispatcherPriority.Input);
    }

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        ShowView(View.Settings);
        GroupList.SelectedItem = null;
    }

    private string UniqueName(string baseName)
    {
        if (!Groups.Any(g => g.Name.Equals(baseName, StringComparison.CurrentCultureIgnoreCase))) return baseName;
        for (int i = 2; ; i++)
        {
            var name = $"{baseName} {i}";
            if (!Groups.Any(g => g.Name.Equals(name, StringComparison.CurrentCultureIgnoreCase))) return name;
        }
    }

    private void Test_Click(object sender, RoutedEventArgs e)
    {
        if (_group == null || sender is not FrameworkElement element) return;
        var pt = element.PointToScreen(new Point(element.ActualWidth / 2, element.ActualHeight + 8));
        _app.ShowPopup(_group.Id, new Native.POINT((int)pt.X, (int)pt.Y));
    }

    private void More_Click(object sender, RoutedEventArgs e)
    {
        if (_group == null) return;
        var group = _group;
        var menu = new ContextMenu { PlacementTarget = (UIElement)sender, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
        menu.Items.Add(App.MenuItem("", Loc.T("Дублировать"), () => DuplicateGroup(group)));
        menu.Items.Add(group.SourceFolder == null
            ? App.MenuItem("", Loc.T("Синхронизировать с папкой…"), () => LinkFolder(group))
            : App.MenuItem("", Loc.T("Отвязать от папки"), () => group.SourceFolder = null));
        menu.Items.Add(App.MenuItem("", Loc.T("Показать ярлык в Проводнике"), () => _app.Shortcuts.Reveal(group)));
        menu.Items.Add(App.MenuItem("", Loc.T("Добавить в меню «Пуск»"), () => AddToStart(group)));
        menu.Items.Add(App.MenuItem("", Loc.T("Ярлык на рабочем столе"), () =>
        {
            _app.Shortcuts.AddToDesktop(group);
            ShowToast(Loc.T("Ярлык группы создан на рабочем столе"));
        }));
        menu.Items.Add(new Separator());
        var delete = App.MenuItem("", Loc.T("Удалить группу"), () => DeleteGroup(group));
        delete.SetResourceReference(ForegroundProperty, "Brush.Danger");
        menu.Items.Add(delete);
        menu.IsOpen = true;
    }

    private void LinkFolder(Group group)
    {
        var dialog = new OpenFolderDialog { Title = Loc.T("Папка, за которой будет следить группа") };
        if (dialog.ShowDialog(this) != true) return;
        group.SourceFolder = dialog.FolderName;
        if (group.Items.Count == 0 || group.Name.StartsWith(Loc.T("Новая группа"), StringComparison.CurrentCulture))
            group.Name = Path.GetFileName(dialog.FolderName.TrimEnd('\\'));
        ShowToast(Loc.T("Группа синхронизирована с папкой: всё новое в ней появится здесь само"));
    }

    private void OpenSourceFolder_Click(object sender, RoutedEventArgs e)
    {
        if (_group?.SourceFolder is { } folder) Launcher.OpenFolder(folder);
    }

    private void UnlinkFolder_Click(object sender, RoutedEventArgs e)
    {
        if (_group == null) return;
        _group.SourceFolder = null;
        ShowToast(Loc.T("Группа больше не следит за папкой — уже добавленные элементы остались"));
    }

    private void ImportTaskbarGroups_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = Loc.T("Папка программы Taskbar Groups (где лежат config и Shortcuts)"),
            InitialDirectory = TaskbarGroupsImport.GuessFolder() ?? Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
        };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            var groups = TaskbarGroupsImport.Load(dialog.FolderName);
            if (groups.Count == 0)
            {
                ShowToast(Loc.T("Группы Taskbar Groups не найдены — выберите папку, где лежит config"));
                return;
            }
            foreach (var g in groups)
            {
                g.Name = UniqueName(g.Name);
                Groups.Add(g);
            }
            GroupList.SelectedItem = groups[0];
            ShowToast(Loc.F("Перенесено групп: {0}. Закрепите их на панели задач заново — старые значки Taskbar Groups можно открепить", groups.Count));
        }
        catch (Exception ex)
        {
            Log.Error(ex, "taskbar-groups import");
            ShowToast(Loc.T("Не удалось прочитать группы: ") + ex.Message);
        }
    }

    private void OnSettingsChanged(object? sender, PropertyChangedEventArgs e)
    {
        // Окна собираются при запуске, поэтому новый язык включается после перезапуска.
        if (e.PropertyName == nameof(AppSettings.Language))
            ShowToast(Loc.T("Язык сменится после перезапуска Futuretray"), Loc.T("Перезапустить"), () =>
            {
                _app.Store.SaveNow();
                _app.Restart();
            });
    }

    // ================= Рабочее пространство =================

    private void CaptureLayout_Click(object sender, RoutedEventArgs e)
    {
        if (_group == null) return;
        ShowToast(Workspaces.Describe(Workspaces.Capture(_group)));
        UpdateLayoutStatus();
    }

    private void ArrangeLayout_Click(object sender, RoutedEventArgs e)
    {
        if (_group == null) return;
        ShowToast(Loc.T("Раскладываю окна… недостающие программы запускаются"));
        Workspaces.Arrange(_group, result => ShowToast(Workspaces.Describe(result)));
    }

    private void ForgetLayout_Click(object sender, RoutedEventArgs e)
    {
        if (_group == null) return;
        Workspaces.Forget(_group);
        UpdateLayoutStatus();
        ShowToast(Loc.T("Расположение окон забыто"));
    }

    private void UpdateLayoutStatus()
    {
        if (_group == null) return;
        int saved = _group.Items.Count(i => i.Layout != null);
        LayoutStatus.Text = saved == 0
            ? Loc.T("Расположение ещё не запомнено")
            : Loc.F("Запомнены места окон: {0} из {1}", saved, _group.Items.Count);
        ArrangeButton.IsEnabled = ForgetLayoutButton.IsEnabled = saved > 0;
    }

    private void UpdateHotkeyErrors()
    {
        var failures = _app.Hotkeys.Failures;
        SetError(SearchHotkeyError, failures.GetValueOrDefault("search"));
        SetError(GroupHotkeyError, _group == null ? null : failures.GetValueOrDefault(_group.Id));

        static void SetError(TextBlock block, string? text)
        {
            block.Text = text ?? "";
            block.Visibility = text == null ? Visibility.Collapsed : Visibility.Visible;
        }
    }

    private void DuplicateGroup(Group group)
    {
        var copy = group.Duplicate(UniqueName(group.Name + Loc.T(" — копия")));
        Groups.Insert(Groups.IndexOf(group) + 1, copy);
        GroupList.SelectedItem = copy;
    }

    private void DeleteGroup(Group group)
    {
        int index = Groups.IndexOf(group);
        if (index < 0) return;
        Groups.Remove(group);
        if (Groups.Count > 0) GroupList.SelectedIndex = Math.Min(index, Groups.Count - 1);

        ShowToast(Loc.F("Группа «{0}» удалена", group.Name), Loc.T("Отменить"),
            action: () =>
            {
                Groups.Insert(Math.Min(index, Groups.Count), group);
                GroupList.SelectedItem = group;
            },
            onExpire: () => _app.Shortcuts.Remove(group));
    }

    // ================= Закрепление =================

    private void Pin_Click(object sender, RoutedEventArgs e)
    {
        if (_group == null) return;
        var group = _group;
        _app.Shortcuts.Update(group);
        bool alreadyPinned = _app.Shortcuts.IsPinned(group);

        PinStatus.Text = alreadyPinned
            ? Loc.T("Эта группа уже закреплена. Если значок пропал с панели, закрепите его ещё раз любым способом ниже.")
            : Loc.T("Windows не даёт программам закрепляться самостоятельно, поэтому последний шаг — за вами. Это займёт пару секунд.");
        StartHint.Text = Loc.F("Затем нажмите Win, начните вводить «{0}», щёлкните по найденной группе правой кнопкой → «Закрепить на панели задач».", group.Name);
        SetPinWaiting(alreadyPinned ? PinState.Already : PinState.Waiting);
        ShowOverlay(PinPanel);

        // Windows не сообщает о закреплении, поэтому просто следим за папкой закреплённых ярлыков.
        _pinWatch?.Stop();
        if (alreadyPinned) return;
        _pinWatch = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _pinWatch.Tick += (_, _) =>
        {
            if (Overlay.Visibility != Visibility.Visible || PinPanel.Visibility != Visibility.Visible || group != _group)
            {
                _pinWatch?.Stop();
                return;
            }
            if (!_app.Shortcuts.IsPinned(group)) return;
            _pinWatch?.Stop();
            SetPinWaiting(PinState.Done);
            var close = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
            close.Tick += (_, _) =>
            {
                close.Stop();
                if (PinPanel.Visibility == Visibility.Visible) CloseOverlay();
                ShowToast(Loc.F("Готово! «{0}» закреплена на панели задач", group.Name));
            };
            close.Start();
        };
        _pinWatch.Start();
    }

    private enum PinState { Waiting, Done, Already }

    private void SetPinWaiting(PinState state)
    {
        (PinWaitingGlyph.Text, PinWaitingText.Text) = state switch
        {
            PinState.Done => ("", Loc.T("Готово! Группа закреплена на панели задач")),
            PinState.Already => ("", Loc.T("Уже закреплена на панели задач")),
            _ => ("", Loc.T("Жду, пока значок появится на панели задач…")),
        };
        var key = state == PinState.Waiting ? "Brush.TextSecondary" : "Brush.AccentForeground";
        PinWaitingGlyph.SetResourceReference(TextBlock.ForegroundProperty, key);
        PinWaitingText.SetResourceReference(TextBlock.ForegroundProperty, key);
    }

    private void RevealLink_Click(object sender, RoutedEventArgs e)
    {
        if (_group != null) _app.Shortcuts.Reveal(_group);
    }

    private void AddToStart_Click(object sender, RoutedEventArgs e)
    {
        if (_group != null) AddToStart(_group);
    }

    private void AddToStart(Group group)
    {
        _app.Shortcuts.AddToStartMenu(group);
        ShowToast(Loc.F("«{0}» добавлена в меню «Пуск». В поиске «Пуска» она может появиться через несколько секунд", group.Name));
    }

    // ================= Элементы группы =================

    private void AddItems(IEnumerable<LaunchItem> items)
    {
        if (_group == null) return;
        LaunchItem? last = null;
        int count = 0;
        foreach (var item in items)
        {
            _group.Items.Add(item);
            last = item;
            count++;
        }
        if (last == null) return;
        ItemsList.SelectedItem = last;
        ItemsList.ScrollIntoView(last);
        if (count > 1) ShowToast(Loc.F("Добавлено: {0} {1}", count, Loc.Plural(count, "элемент", "элемента", "элементов")));
    }

    private async void AddApp_Click(object sender, RoutedEventArgs e)
    {
        ShowOverlay(PickerPanel);
        PickerSearch.Text = "";
        PickerSearch.Focus();

        if (_installed == null)
        {
            PickerLoading.Visibility = Visibility.Visible;
            try
            {
                _installed = await ItemFactory.EnumerateInstalledAsync();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Installed apps");
                _installed = new List<InstalledApp>();
            }
            PickerLoading.Text = Loc.T("Ничего не найдено");
            PickerLoading.Visibility = _installed.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            foreach (var app in _installed)
                app.PropertyChanged += (_, args) =>
                {
                    if (args.PropertyName == nameof(InstalledApp.IsSelected)) UpdatePickerCount();
                };
            _installedView = CollectionViewSource.GetDefaultView(_installed);
            _installedView.Filter = o => o is InstalledApp a && MatchesSearch(a, PickerSearch.Text);
            PickerList.ItemsSource = _installedView;
            LoadPickerIcons(_installed);
        }
        else
        {
            foreach (var app in _installed) app.IsSelected = false;
            _installedView?.Refresh();
        }
        UpdatePickerCount();
    }

    private static bool MatchesSearch(InstalledApp app, string query) =>
        string.IsNullOrWhiteSpace(query) ||
        app.Name.Contains(query.Trim(), StringComparison.CurrentCultureIgnoreCase) ||
        (app.FilePath?.Contains(query.Trim(), StringComparison.OrdinalIgnoreCase) ?? false);

    private void LoadPickerIcons(List<InstalledApp> apps)
    {
        var icons = _app.Icons;
        var thread = new Thread(() =>
        {
            foreach (var app in apps)
            {
                try
                {
                    var icon = icons.GetShell(app.FilePath ?? @"shell:AppsFolder\" + app.ParsingName, 64);
                    if (icon != null) Dispatcher.BeginInvoke(() => app.Icon = icon, DispatcherPriority.Background);
                }
                catch
                {
                    // пропускаем
                }
            }
        })
        { IsBackground = true, Name = "Picker icons" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
    }

    private void PickerSearch_TextChanged(object sender, TextChangedEventArgs e) => _installedView?.Refresh();

    private void PickerSearch_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Down && PickerList.Items.Count > 0)
        {
            PickerList.Focus();
            (PickerList.ItemContainerGenerator.ContainerFromIndex(0) as ListBoxItem)?.Focus();
            e.Handled = true;
        }
        else if (e.Key == Key.Enter)
        {
            if (_installed?.Any(a => a.IsSelected) != true && PickerList.Items.Count > 0 && PickerList.Items[0] is InstalledApp first)
                first.IsSelected = true;
            PickerAdd_Click(sender, e);
            e.Handled = true;
        }
    }

    private void UpdatePickerCount()
    {
        int n = _installed?.Count(a => a.IsSelected) ?? 0;
        PickerCount.Text = n == 0 ? Loc.T("Ничего не выбрано") : Loc.F("Выбрано: {0}", n);
        PickerAddButton.IsEnabled = n > 0;
    }

    private void PickerList_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if ((e.OriginalSource as DependencyObject) is { } source &&
            ItemsControl.ContainerFromElement(PickerList, source) is ListBoxItem { DataContext: InstalledApp app })
        {
            app.IsSelected = true;
            PickerAdd_Click(sender, e);
        }
    }

    private void PickerAdd_Click(object sender, RoutedEventArgs e)
    {
        var selected = _installed?.Where(a => a.IsSelected).ToList() ?? new();
        if (selected.Count == 0) return;
        CloseOverlay();
        AddItems(selected.Select(ItemFactory.FromInstalled));
    }

    private void AddFile_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = Loc.T("Выберите программы, ярлыки или файлы"),
            Multiselect = true,
            DereferenceLinks = false,
            Filter = Loc.T("Программы и ярлыки|*.exe;*.lnk;*.url;*.bat;*.cmd;*.ps1;*.appref-ms|Все файлы|*.*"),
        };
        if (dialog.ShowDialog(this) == true) AddItems(dialog.FileNames.Select(ItemFactory.FromPath));
    }

    private void AddFolder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = Loc.T("Выберите папки"), Multiselect = true };
        if (dialog.ShowDialog(this) == true) AddItems(dialog.FolderNames.Select(ItemFactory.FromPath));
    }

    private void AddUrl_Click(object sender, RoutedEventArgs e)
    {
        UrlBox.Text = "";
        UrlNameBox.Text = "";
        if (Clipboard.ContainsText() && ItemFactory.LooksLikeUrl(Clipboard.GetText())) UrlBox.Text = Clipboard.GetText().Trim();
        ShowOverlay(UrlPanel);
        UrlBox.Focus();
        UrlBox.SelectAll();
    }

    private void UrlBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        UrlAdd_Click(sender, e);
        e.Handled = true;
    }

    private void UrlAdd_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(UrlBox.Text))
        {
            UrlBox.Focus();
            return;
        }
        CloseOverlay();
        AddItems(new[] { ItemFactory.FromUrl(UrlBox.Text, UrlNameBox.Text) });
    }

    private void ItemsList_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateItemCard();

    private void UpdateItemCard()
    {
        ItemCard.DataContext = SelectedItem;
        ItemCard.Visibility = SelectedItem == null ? Visibility.Collapsed : Visibility.Visible;
    }

    private void ItemsList_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Delete && SelectedItem is { } item)
        {
            RemoveItem(item);
            e.Handled = true;
        }
    }

    private void RemoveItem_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedItem is { } item) RemoveItem(item);
    }

    private void RemoveItem(LaunchItem item)
    {
        if (_group == null) return;
        var group = _group;
        int index = group.Items.IndexOf(item);
        if (index < 0) return;
        group.Items.Remove(item);
        if (group.Items.Count > 0) ItemsList.SelectedIndex = Math.Min(index, group.Items.Count - 1);
        ShowToast(Loc.F("«{0}» удалено из группы", item.Name), Loc.T("Отменить"), () =>
        {
            group.Items.Insert(Math.Min(index, group.Items.Count), item);
            if (group == _group) ItemsList.SelectedItem = item;
        });
    }

    private void RunItem_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedItem is { } item && Launcher.Launch(item) is { } error) ShowToast(error);
    }

    private void OpenItemLocation_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedItem is { } item) Launcher.OpenLocation(item);
    }

    private void BrowseTarget_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedItem is not { } item) return;
        var dialog = new OpenFileDialog { Title = Loc.T("Выберите файл"), DereferenceLinks = false, Filter = Loc.T("Все файлы|*.*") };
        if (dialog.ShowDialog(this) != true) return;
        var fresh = ItemFactory.FromPath(dialog.FileName);
        item.Kind = ItemKind.Path;
        item.Target = fresh.Target;
        if (string.IsNullOrWhiteSpace(item.Name)) item.Name = fresh.Name;
    }

    private void ItemIcon_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedItem is { } item && PickIconFile() is { } path) item.CustomIconPath = path;
    }

    private void ResetItemIcon_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedItem is { } item) item.CustomIconPath = null;
    }

    private string? PickIconFile()
    {
        var dialog = new OpenFileDialog
        {
            Title = Loc.T("Выберите картинку или значок"),
            Filter = Loc.T("Картинки и значки|*.png;*.jpg;*.jpeg;*.bmp;*.ico;*.exe;*.dll|Все файлы|*.*"),
        };
        if (dialog.ShowDialog(this) != true) return null;
        var ext = Path.GetExtension(dialog.FileName).ToLowerInvariant();
        if (ext is ".exe" or ".dll") return dialog.FileName;
        try
        {
            // Копируем к себе, чтобы иконка не пропала, если исходный файл удалят.
            var dir = Path.Combine(ConfigStore.DataDir, "UserIcons");
            Directory.CreateDirectory(dir);
            var dest = Path.Combine(dir, Ids.New() + ext);
            File.Copy(dialog.FileName, dest, true);
            return dest;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Copy icon");
            return dialog.FileName;
        }
    }

    // ================= Перетаскивание из Проводника/браузера =================

    private void Editor_DragOver(object sender, DragEventArgs e)
    {
        if (e.Handled) return;
        bool accept = _group != null &&
                      (e.Data.GetDataPresent(DataFormats.FileDrop) || DroppedUrl(e.Data) != null);
        e.Effects = accept ? DragDropEffects.Copy : DragDropEffects.None;
        SetDropHighlight(accept);
        e.Handled = true;
    }

    private void Editor_DragLeave(object sender, DragEventArgs e) => SetDropHighlight(false);

    private void Editor_Drop(object sender, DragEventArgs e)
    {
        SetDropHighlight(false);
        if (e.Handled || _group == null) return;
        if (e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0)
            AddItems(files.Select(ItemFactory.FromPath));
        else if (DroppedUrl(e.Data) is { } url)
            AddItems(new[] { ItemFactory.FromUrl(url) });
        e.Handled = true;
    }

    private static string? DroppedUrl(IDataObject data)
    {
        try
        {
            if (data.GetData(DataFormats.UnicodeText) is string text && ItemFactory.LooksLikeUrl(text))
                return text.Trim().Split('\n')[0].Trim();
        }
        catch
        {
            // некоторые источники отдают битые данные
        }
        return null;
    }

    private void SetDropHighlight(bool on)
    {
        if (on)
        {
            DropHintFrame.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, "Brush.Accent");
            DropHintFrame.Opacity = 1;
            DropHintText.Text = Loc.T("Отпустите, чтобы добавить в группу");
        }
        else
        {
            DropHintFrame.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, "Brush.ControlStrong");
            DropHintFrame.Opacity = 0.5;
            DropHintText.Text = Loc.T("Перетащите сюда программы, ярлыки, папки, файлы или ссылки из браузера");
        }
    }

    // ================= Внешний вид =================

    private void BuildSwatches()
    {
        SwatchPanel.Children.Clear();
        if (_group == null) return;
        foreach (var value in ColorUtil.Palette)
        {
            var swatch = new RadioButton
            {
                Style = (Style)FindResource("Swatch"),
                GroupName = "GroupColor",
                Background = ColorUtil.Brush(ColorUtil.Resolve(value)),
                ToolTip = value == "accent" ? Loc.T("Цвет акцента Windows") : value,
                IsChecked = string.Equals(_group.Color, value, StringComparison.OrdinalIgnoreCase),
            };
            if (value == "accent")
                swatch.Content = new TextBlock { Text = "", FontFamily = (FontFamily)FindResource("Font.Icons"), FontSize = 10, Foreground = Brushes.White };
            var captured = value;
            swatch.Checked += (_, _) =>
            {
                if (_group != null) _group.Color = captured;
            };
            SwatchPanel.Children.Add(swatch);
        }
        if (!HexBox.IsKeyboardFocused) HexBox.Text = ColorUtil.ToHex(ColorUtil.Resolve(_group.Color));
    }

    private void HexBox_LostFocus(object sender, RoutedEventArgs e) => ApplyHex();

    private void HexBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) ApplyHex();
    }

    private void ApplyHex()
    {
        if (_group == null) return;
        if (ColorUtil.TryParse(HexBox.Text, out var color)) _group.Color = ColorUtil.ToHex(color);
        else HexBox.Text = ColorUtil.ToHex(ColorUtil.Resolve(_group.Color));
    }

    private void UpdatePreviewTint()
    {
        if (_group == null) return;
        PreviewTint.Background = ColorUtil.Brush(ColorUtil.WithAlpha(ColorUtil.Resolve(_group.Color), _group.TintOpacity));
    }

    private void UpdateIconPanels()
    {
        if (_group == null) return;
        IconChoicePanel.Visibility = _group.IconMode == GroupIconMode.Item ? Visibility.Visible : Visibility.Collapsed;
        CustomIconPanel.Visibility = _group.IconMode == GroupIconMode.Custom ? Visibility.Visible : Visibility.Collapsed;
        CustomIconName.Text = _group.CustomIconPath != null ? Path.GetFileName(_group.CustomIconPath) : "";

        if (_group.IconMode != GroupIconMode.Item) return;
        IconChoicePanel.Children.Clear();
        var selectedId = _group.Items.FirstOrDefault(i => i.Id == _group.IconItemId)?.Id ?? _group.Items.FirstOrDefault()?.Id;
        foreach (var item in _group.Items)
        {
            var choice = new RadioButton
            {
                Style = (Style)FindResource("Swatch"),
                GroupName = "IconChoice",
                Width = 40,
                Height = 40,
                Background = Brushes.Transparent,
                ToolTip = item.Name,
                IsChecked = item.Id == selectedId,
                Content = new Image { Source = item.Icon, Width = 24, Height = 24 },
            };
            var captured = item;
            choice.Checked += (_, _) =>
            {
                if (_group != null) _group.IconItemId = captured.Id;
            };
            IconChoicePanel.Children.Add(choice);
        }
        if (_group.Items.Count == 0)
            IconChoicePanel.Children.Add(new TextBlock { Text = Loc.T("Сначала добавьте приложения"), Style = (Style)FindResource("Text.Caption") });
    }

    private void PickGroupIcon_Click(object sender, RoutedEventArgs e)
    {
        if (_group == null) return;
        var path = PickIconFile();
        if (path != null)
        {
            _group.CustomIconPath = path;
            _group.IconMode = GroupIconMode.Custom;
        }
        else if (_group.CustomIconPath == null)
        {
            _group.IconMode = GroupIconMode.Auto;
        }
    }

    // ================= Настройки =================

    private void Export_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog { Title = Loc.T("Экспорт групп"), FileName = Loc.T("Futuretray-группы.json"), Filter = "Futuretray (*.json)|*.json" };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            File.WriteAllText(dialog.FileName, JsonSerializer.Serialize(Groups.ToList(), ConfigStore.JsonOptions));
            ShowToast(Loc.F("Сохранено групп: {0}", Groups.Count));
        }
        catch (Exception ex)
        {
            ShowToast(Loc.T("Не удалось сохранить: ") + ex.Message);
        }
    }

    private void Import_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Title = Loc.T("Импорт групп"), Filter = Loc.T("Futuretray (*.json)|*.json|Все файлы|*.*") };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            var json = File.ReadAllText(dialog.FileName);
            List<Group>? groups;
            try
            {
                groups = JsonSerializer.Deserialize<List<Group>>(json, ConfigStore.JsonOptions);
            }
            catch (JsonException)
            {
                groups = JsonSerializer.Deserialize<AppConfig>(json, ConfigStore.JsonOptions)?.Groups.ToList();
            }
            if (groups == null || groups.Count == 0)
            {
                ShowToast(Loc.T("В файле нет групп"));
                return;
            }
            foreach (var g in groups)
            {
                g.Id = Ids.New();
                foreach (var item in g.Items) item.Id = Ids.New();
                g.Name = UniqueName(g.Name);
                Groups.Add(g);
            }
            GroupList.SelectedItem = groups[0];
            ShowToast(Loc.F("Импортировано групп: {0}", groups.Count));
        }
        catch (Exception ex)
        {
            ShowToast(Loc.T("Не удалось прочитать файл: ") + ex.Message);
        }
    }

    private void OpenDataFolder_Click(object sender, RoutedEventArgs e) => Launcher.OpenFolder(ConfigStore.DataDir);

    private void CreateReport_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var path = DiagnosticsReport.Create(_app.Store);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true })?.Dispose();
            ShowToast(Loc.T("Отчёт сохранён на рабочий стол — отправьте этот zip разработчику"));
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Report");
            ShowToast(Loc.T("Не удалось собрать отчёт: ") + ex.Message);
        }
    }

    private void ReportIssue_Click(object sender, RoutedEventArgs e) =>
        Launcher.Launch(new LaunchItem { Kind = ItemKind.Url, Target = $"https://github.com/{Updater.Repository}/issues/new" });

    private async void CheckUpdates_Click(object sender, RoutedEventArgs e) => await _app.Updates.CheckAsync(manual: true);

    private async void InstallUpdate_Click(object sender, RoutedEventArgs e) => await _app.Updates.InstallAsync();

    private void ReleaseNotes_Click(object sender, RoutedEventArgs e)
    {
        if (_app.Updates.Available is { } update)
            Launcher.Launch(new LaunchItem { Kind = ItemKind.Url, Target = update.PageUrl });
    }

    public void ShowUpdateToast(UpdateInfo update) =>
        ShowToast(Loc.F("Доступна новая версия Futuretray — {0}", update.Version.ToString(3)), Loc.T("Обновить"),
            () => _ = _app.Updates.InstallAsync());

    private void Exit_Click(object sender, RoutedEventArgs e) => _app.ExitApp();

    // ================= Диалоги =================

    private void ShowOverlay(FrameworkElement panel)
    {
        foreach (var p in new FrameworkElement[] { PinPanel, PickerPanel, UrlPanel })
            p.Visibility = p == panel ? Visibility.Visible : Visibility.Collapsed;
        Overlay.Visibility = Visibility.Visible;

        var ease = new ExponentialEase { EasingMode = EasingMode.EaseOut, Exponent = 5 };
        var scale = (ScaleTransform)DialogCard.RenderTransform;
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.96, 1, TimeSpan.FromMilliseconds(220)) { EasingFunction = ease });
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.96, 1, TimeSpan.FromMilliseconds(220)) { EasingFunction = ease });
        Overlay.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(140)));
    }

    private void CloseOverlay()
    {
        _pinWatch?.Stop();
        Overlay.Visibility = Visibility.Collapsed;
    }

    private void CloseOverlay_Click(object sender, RoutedEventArgs e) => CloseOverlay();

    private void Overlay_BackgroundClick(object sender, MouseButtonEventArgs e) => CloseOverlay();

    // ================= Уведомления =================

    public void ShowToast(string text, string? actionText = null, Action? action = null, Action? onExpire = null)
    {
        FlushToast();
        _toastAction = action;
        _toastExpire = onExpire;
        ToastText.Text = text;
        ToastAction.Content = actionText;
        ToastAction.Visibility = actionText == null ? Visibility.Collapsed : Visibility.Visible;
        Toast.Visibility = Visibility.Visible;
        Toast.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(160)));

        _toastTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(action != null ? 7 : 4) };
        _toastTimer.Tick += (_, _) => FlushToast();
        _toastTimer.Start();
    }

    /// <summary>Скрывает уведомление и окончательно применяет отложенное действие (например, удаление ярлыков).</summary>
    private void FlushToast()
    {
        _toastTimer?.Stop();
        _toastTimer = null;
        var expire = _toastExpire;
        _toastExpire = null;
        _toastAction = null;
        Toast.Visibility = Visibility.Collapsed;
        expire?.Invoke();
    }

    private void ToastAction_Click(object sender, RoutedEventArgs e)
    {
        var action = _toastAction;
        _toastExpire = null;
        FlushToast();
        action?.Invoke();
    }

    private void ToastClose_Click(object sender, RoutedEventArgs e) => FlushToast();

#if DEBUG
    internal void SnapshotSelectFirstItem()
    {
        Left = -4000;
        ItemsList.SelectedIndex = 0;
    }

    internal void SnapshotScroll(double offset) => EditorScroll.ScrollToVerticalOffset(offset);

    internal void SnapshotShowPin()
    {
        EditorScroll.ScrollToTop();
        Pin_Click(this, new RoutedEventArgs());
    }

    internal void SnapshotShowSettings()
    {
        CloseOverlay();
        Settings_Click(this, new RoutedEventArgs());
    }

    internal void SnapshotSelectGroup(int index) => GroupList.SelectedIndex = index;
#endif
}
