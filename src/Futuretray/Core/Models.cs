using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using System.Windows.Media;

namespace Futuretray.Core;

public abstract class Observable : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(name);
        return true;
    }

    protected virtual void OnPropertyChanged(string? name) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public static class Ids
{
    public static string New() => Guid.NewGuid().ToString("N")[..12];
}

public enum ItemKind { Path, Url, ShellApp }
public enum TileSize { Small, Medium, Large }
public enum BackdropKind { Acrylic, Mica, Solid }
public enum GroupIconMode { Auto, Item, Custom }
public enum ThemePreference { System, Light, Dark }
public enum UiLanguage { Auto, Russian, English }
public enum DockEdge { Left, Top, Right, Bottom, Free }
public enum DockAlign { Start, Center, End }

/// <summary>Что делать с остальными окнами, когда группа открывается целиком («Открыть все»).</summary>
public enum OthersOnOpen { Keep, Minimize, CloseGroups }

/// <summary>
/// Запомненное место окна программы: монитор и прямоугольник в долях его рабочей области
/// (0..1) — так расположение переживает смену разрешения и масштаба.
/// </summary>
public sealed class WindowLayout
{
    /// <summary>Имя устройства монитора (\\.\DISPLAY2); null — основной.</summary>
    public string? Monitor { get; set; }
    /// <summary>Границы монитора «x,y,ш,в» — по ним монитор находится, даже если Windows сменила его имя.</summary>
    public string? Screen { get; set; }
    public DateTime Captured { get; set; }
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
    public bool Maximized { get; set; }

    public WindowLayout Clone() => (WindowLayout)MemberwiseClone();
}

public sealed class LaunchItem : Observable
{
    private string _id = Ids.New();
    private ItemKind _kind;
    private string _target = "";
    private string _name = "";
    private string? _arguments;
    private string? _workingDirectory;
    private bool _runAsAdmin;
    private string? _customIconPath;

    public string Id { get => _id; set => Set(ref _id, value); }
    public ItemKind Kind { get => _kind; set { if (Set(ref _kind, value)) IconInputsChanged(); } }
    public string Target { get => _target; set { if (Set(ref _target, value ?? "")) IconInputsChanged(); } }
    public string Name { get => _name; set => Set(ref _name, value ?? ""); }
    public string? Arguments { get => _arguments; set => Set(ref _arguments, string.IsNullOrWhiteSpace(value) ? null : value); }
    public string? WorkingDirectory { get => _workingDirectory; set => Set(ref _workingDirectory, string.IsNullOrWhiteSpace(value) ? null : value); }
    public bool RunAsAdmin { get => _runAsAdmin; set => Set(ref _runAsAdmin, value); }
    public string? CustomIconPath { get => _customIconPath; set { if (Set(ref _customIconPath, string.IsNullOrWhiteSpace(value) ? null : value)) IconInputsChanged(); } }

    private Dictionary<string, WindowLayout>? _layouts;
    private int _startDelay;

    /// <summary>
    /// Места окна для рабочего пространства группы — по одному на каждый набор мониторов
    /// (ключ — <see cref="Workspaces.ProfileKey"/>). Меняется только заменой словаря целиком.
    /// </summary>
    public Dictionary<string, WindowLayout>? Layouts
    {
        get => _layouts;
        set => Set(ref _layouts, value is { Count: > 0 } ? value : null);
    }

    /// <summary>Место из версии 1.6.0 (без привязки к набору мониторов) — переносится в <see cref="Layouts"/> при запуске.</summary>
    [JsonPropertyName("Layout")]
    public WindowLayout? LegacyLayout { get; set; }

    /// <summary>Пауза в секундах перед запуском при «Открыть все» — после предыдущей программы группы.</summary>
    public int StartDelay { get => _startDelay; set => Set(ref _startDelay, Math.Clamp(value, 0, 120)); }

    [JsonIgnore] public ImageSource? Icon => IconService.Current?.Get(this, IconService.TilePx);

    /// <summary>Ключ, по которому кэшируется иконка элемента (для ссылок — с учётом скачанного значка сайта).</summary>
    [JsonIgnore]
    public string IconKey => Kind == ItemKind.Url
        ? $"{Kind}|{Target}|{CustomIconPath}|{Favicons.Stamp(Target)}"
        : $"{Kind}|{Target}|{CustomIconPath}";

    /// <summary>Иконка могла измениться снаружи (например, скачался значок сайта).</summary>
    public void RefreshIcon() => IconInputsChanged();

    [JsonIgnore]
    public string KindLabel => Kind switch
    {
        ItemKind.Url => Loc.T("Ссылка"),
        ItemKind.ShellApp => Loc.T("Приложение Windows"),
        _ when Directory.Exists(Target) => Loc.T("Папка"),
        _ when Target.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) => Loc.T("Программа"),
        _ when Target.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) => Loc.T("Ярлык"),
        _ => Loc.T("Файл"),
    };

    [JsonIgnore] public bool IsPath => Kind == ItemKind.Path;

    private void IconInputsChanged()
    {
        OnPropertyChanged(nameof(Icon));
        OnPropertyChanged(nameof(IconKey));
        OnPropertyChanged(nameof(KindLabel));
        OnPropertyChanged(nameof(IsPath));
    }

    public LaunchItem Clone() => new()
    {
        Kind = Kind,
        Target = Target,
        Name = Name,
        Arguments = Arguments,
        WorkingDirectory = WorkingDirectory,
        RunAsAdmin = RunAsAdmin,
        CustomIconPath = CustomIconPath,
        Layouts = Layouts?.ToDictionary(p => p.Key, p => p.Value.Clone()),
        StartDelay = StartDelay,
    };
}

public sealed class Group : Observable
{
    private static readonly HashSet<string> IconProperties = new()
    {
        nameof(IconMode), nameof(IconItemId), nameof(CustomIconPath), nameof(ShowIconBackground), nameof(Color),
    };

    private string _id = Ids.New();
    private string _name = Loc.T("Новая группа");
    private ObservableCollection<LaunchItem> _items = new();
    private GroupIconMode _iconMode = GroupIconMode.Auto;
    private string? _iconItemId;
    private string? _customIconPath;
    private bool _showIconBackground = true;
    private string _color = "#6D5DFC";
    private int _columns;
    private TileSize _tileSize = TileSize.Medium;
    private bool _showLabels;
    private BackdropKind _backdrop = BackdropKind.Acrylic;
    private double _tintOpacity = 0.12;
    private bool _closeAfterLaunch = true;
    private bool _dockEnabled;
    private DockEdge _dockEdge = DockEdge.Left;
    private DockAlign _dockAlign = DockAlign.Center;
    private string? _dockMonitor;
    private bool _dockTopmost;
    private bool _dockAutoHide;
    private bool _dockReserveSpace;
    private int? _dockX;
    private int? _dockY;
    private string? _hotkey;
    private string? _sourceFolder;
    private bool _dockPlaced;
    private bool _openAtLogin;
    private OthersOnOpen _othersOnOpen;
    private List<string>? _closeGroupIds;

    public Group()
    {
        _items.CollectionChanged += OnItemsChanged;
    }

    /// <summary>Любое изменение группы или её элементов.</summary>
    public event EventHandler? Changed;

    public string Id { get => _id; set => Set(ref _id, value); }
    public string Name { get => _name; set => Set(ref _name, value ?? ""); }

    public ObservableCollection<LaunchItem> Items
    {
        get => _items;
        set
        {
            _items.CollectionChanged -= OnItemsChanged;
            foreach (var item in _items) item.PropertyChanged -= OnItemPropertyChanged;
            _items = value ?? new();
            _items.CollectionChanged += OnItemsChanged;
            foreach (var item in _items) item.PropertyChanged += OnItemPropertyChanged;
            OnPropertyChanged(nameof(Items));
            OnPropertyChanged(nameof(Icon));
        }
    }

    public GroupIconMode IconMode { get => _iconMode; set => Set(ref _iconMode, value); }
    public string? IconItemId { get => _iconItemId; set => Set(ref _iconItemId, value); }
    public string? CustomIconPath { get => _customIconPath; set => Set(ref _customIconPath, value); }
    public bool ShowIconBackground { get => _showIconBackground; set => Set(ref _showIconBackground, value); }

    /// <summary>Цвет группы: "#RRGGBB" или "accent" (акцентный цвет Windows).</summary>
    public string Color { get => _color; set => Set(ref _color, string.IsNullOrWhiteSpace(value) ? "accent" : value.Trim()); }

    /// <summary>0 — подбирать автоматически.</summary>
    public int Columns { get => _columns; set => Set(ref _columns, Math.Clamp(value, 0, 16)); }
    public TileSize TileSize { get => _tileSize; set => Set(ref _tileSize, value); }
    public bool ShowLabels { get => _showLabels; set => Set(ref _showLabels, value); }
    public BackdropKind Backdrop { get => _backdrop; set => Set(ref _backdrop, value); }
    public double TintOpacity { get => _tintOpacity; set => Set(ref _tintOpacity, Math.Clamp(value, 0, 0.8)); }
    public bool CloseAfterLaunch { get => _closeAfterLaunch; set => Set(ref _closeAfterLaunch, value); }

    // ----- Трей на рабочем столе -----

    /// <summary>Показывать группу отдельной панелью на рабочем столе.</summary>
    public bool DockEnabled { get => _dockEnabled; set => Set(ref _dockEnabled, value); }
    public DockEdge DockEdge { get => _dockEdge; set => Set(ref _dockEdge, value); }
    public DockAlign DockAlign { get => _dockAlign; set => Set(ref _dockAlign, value); }

    /// <summary>Имя устройства монитора (\\.\DISPLAY2); null — основной.</summary>
    public string? DockMonitor { get => _dockMonitor; set => Set(ref _dockMonitor, value); }
    public bool DockTopmost { get => _dockTopmost; set => Set(ref _dockTopmost, value); }

    /// <summary>Прятать за край экрана и показывать при наведении. Несовместимо с резервированием места.</summary>
    public bool DockAutoHide
    {
        get => _dockAutoHide;
        set { if (Set(ref _dockAutoHide, value) && value) DockReserveSpace = false; }
    }

    /// <summary>Как панель задач: развёрнутые окна не заходят под трей.</summary>
    public bool DockReserveSpace
    {
        get => _dockReserveSpace;
        set { if (Set(ref _dockReserveSpace, value) && value) DockAutoHide = false; }
    }

    /// <summary>Положение в свободном режиме (физические пиксели, левый верхний угол).</summary>
    public int? DockX { get => _dockX; set => Set(ref _dockX, value); }
    public int? DockY { get => _dockY; set => Set(ref _dockY, value); }

    /// <summary>Трей уже ставили к краю при первом включении — дальше положение выбирает пользователь.</summary>
    public bool DockPlaced { get => _dockPlaced; set => Set(ref _dockPlaced, value); }

    /// <summary>Открывать группу («Открыть все» с раскладкой окон) при входе в Windows.</summary>
    public bool OpenAtLogin { get => _openAtLogin; set => Set(ref _openAtLogin, value); }

    /// <summary>Переключение пространств: при «Открыть все» свернуть остальное или закрыть другие группы.</summary>
    public OthersOnOpen OthersOnOpen { get => _othersOnOpen; set => Set(ref _othersOnOpen, value); }

    /// <summary>Группы, чьи программы закрываются при «Открыть все» (для <see cref="OthersOnOpen.CloseGroups"/>). Меняется заменой списка.</summary>
    public List<string>? CloseGroupIds { get => _closeGroupIds; set => Set(ref _closeGroupIds, value is { Count: > 0 } ? value : null); }

    /// <summary>
    /// Первое включение трея: ставим его к той же стороне экрана, где панель задач
    /// (снизу — рядом снизу, слева — столбцом слева), а не всегда слева.
    /// </summary>
    public void PlaceDockNextToTaskbar(TaskbarEdge taskbarEdge)
    {
        if (DockPlaced) return;
        DockPlaced = true;
        if (DockEdge != DockEdge.Left || DockX != null) return; // пользователь уже что-то выбрал
        DockEdge = taskbarEdge switch
        {
            TaskbarEdge.Top => DockEdge.Top,
            TaskbarEdge.Right => DockEdge.Right,
            TaskbarEdge.Left => DockEdge.Left,
            _ => DockEdge.Bottom,
        };
    }

    /// <summary>Глобальное сочетание клавиш, открывающее группу у курсора (например, "Ctrl+Alt+1").</summary>
    public string? Hotkey { get => _hotkey; set => Set(ref _hotkey, string.IsNullOrWhiteSpace(value) ? null : value); }

    /// <summary>Папка, содержимое которой группа подхватывает сама. null — обычная группа.</summary>
    public string? SourceFolder { get => _sourceFolder; set => Set(ref _sourceFolder, string.IsNullOrWhiteSpace(value) ? null : value); }

    /// <summary>Сообщить подписчикам, что вид группы изменился (например, у элемента появилась новая иконка).</summary>
    public void Touch() => Changed?.Invoke(this, EventArgs.Empty);

    /// <summary>Есть ли запомненное рабочее пространство (места окон).</summary>
    [JsonIgnore] public bool HasLayout => Items.Any(i => i.Layouts != null);

    [JsonIgnore] public ImageSource? Icon => GroupIconRenderer.Get(this, 96);

    public void RefreshIcon() => OnPropertyChanged(nameof(Icon));

    protected override void OnPropertyChanged(string? name)
    {
        base.OnPropertyChanged(name);
        if (name == nameof(Icon)) return;
        if (name != null && IconProperties.Contains(name)) base.OnPropertyChanged(nameof(Icon));
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void OnItemsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems != null)
            foreach (LaunchItem item in e.OldItems) item.PropertyChanged -= OnItemPropertyChanged;
        if (e.NewItems != null)
            foreach (LaunchItem item in e.NewItems) item.PropertyChanged += OnItemPropertyChanged;
        base.OnPropertyChanged(nameof(Icon));
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void OnItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(LaunchItem.Icon) or nameof(LaunchItem.IconKey) or nameof(LaunchItem.KindLabel) or nameof(LaunchItem.IsPath))
        {
            if (e.PropertyName == nameof(LaunchItem.Icon)) base.OnPropertyChanged(nameof(Icon));
            return;
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public Group Duplicate(string newName)
    {
        var copy = new Group
        {
            Name = newName,
            IconMode = IconMode == GroupIconMode.Item ? GroupIconMode.Auto : IconMode,
            CustomIconPath = CustomIconPath,
            ShowIconBackground = ShowIconBackground,
            Color = Color,
            Columns = Columns,
            TileSize = TileSize,
            ShowLabels = ShowLabels,
            Backdrop = Backdrop,
            TintOpacity = TintOpacity,
            CloseAfterLaunch = CloseAfterLaunch,
            OthersOnOpen = OthersOnOpen,
            CloseGroupIds = CloseGroupIds?.ToList(),
            DockEdge = DockEdge,
            DockAlign = DockAlign,
            DockMonitor = DockMonitor,
            DockTopmost = DockTopmost,
            DockAutoHide = DockAutoHide,
        };
        foreach (var item in Items) copy.Items.Add(item.Clone());
        return copy;
    }
}

public sealed class AppSettings : Observable
{
    private ThemePreference _theme = ThemePreference.System;
    private bool _runAtStartup = true;
    private bool _showTrayIcon = true;
    private bool _welcomeShown;
    private bool _checkUpdates = true;
    private bool _switchToRunning = true;
    private string? _searchHotkey = "Win+Alt+Space";
    private bool _downloadFavicons = true;
    private UiLanguage _language = UiLanguage.Auto;

    public UiLanguage Language { get => _language; set => Set(ref _language, value); }

    /// <summary>Сочетание для поиска по всем группам; null — выключено.</summary>
    /// <remarks>Пишем и null, иначе «выключено» после перезапуска превращалось бы в сочетание по умолчанию.</remarks>
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public string? SearchHotkey { get => _searchHotkey; set => Set(ref _searchHotkey, string.IsNullOrWhiteSpace(value) ? null : value); }

    /// <summary>Скачивать значки сайтов для ссылок (обращение к самому сайту).</summary>
    public bool DownloadFavicons { get => _downloadFavicons; set => Set(ref _downloadFavicons, value); }

    /// <summary>Клик по уже открытой программе переключает на её окно, а не запускает копию.</summary>
    public bool SwitchToRunning { get => _switchToRunning; set => Set(ref _switchToRunning, value); }
    private DateTime? _lastUpdateCheck;

    public bool CheckUpdates { get => _checkUpdates; set => Set(ref _checkUpdates, value); }
    public DateTime? LastUpdateCheck { get => _lastUpdateCheck; set => Set(ref _lastUpdateCheck, value); }

    private string? _lastSeenVersion;

    /// <summary>Версия, чьё «Что нового» пользователь уже видел.</summary>
    public string? LastSeenVersion { get => _lastSeenVersion; set => Set(ref _lastSeenVersion, value); }

    public ThemePreference Theme { get => _theme; set => Set(ref _theme, value); }
    public bool RunAtStartup { get => _runAtStartup; set => Set(ref _runAtStartup, value); }
    public bool ShowTrayIcon { get => _showTrayIcon; set => Set(ref _showTrayIcon, value); }
    public bool WelcomeShown { get => _welcomeShown; set => Set(ref _welcomeShown, value); }
}

public sealed class AppConfig
{
    public int Version { get; set; } = 1;
    public AppSettings Settings { get; set; } = new();
    public ObservableCollection<Group> Groups { get; set; } = new();
}
