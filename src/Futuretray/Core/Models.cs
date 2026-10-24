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

    [JsonIgnore] public ImageSource? Icon => IconService.Current?.Get(this, IconService.TilePx);

    /// <summary>Ключ, по которому кэшируется иконка элемента.</summary>
    [JsonIgnore] public string IconKey => $"{Kind}|{Target}|{CustomIconPath}";

    [JsonIgnore]
    public string KindLabel => Kind switch
    {
        ItemKind.Url => "Ссылка",
        ItemKind.ShellApp => "Приложение Windows",
        _ when Directory.Exists(Target) => "Папка",
        _ when Target.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) => "Программа",
        _ when Target.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) => "Ярлык",
        _ => "Файл",
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
    };
}

public sealed class Group : Observable
{
    private static readonly HashSet<string> IconProperties = new()
    {
        nameof(IconMode), nameof(IconItemId), nameof(CustomIconPath), nameof(ShowIconBackground), nameof(Color),
    };

    private string _id = Ids.New();
    private string _name = "Новая группа";
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
