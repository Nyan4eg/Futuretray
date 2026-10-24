using System.Collections.Specialized;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Threading;

namespace Futuretray.Core;

/// <summary>
/// Хранит настройки и группы в config.json с автосохранением. Обычно файл лежит в %AppData%\Futuretray,
/// а при синхронизации — в выбранной папке (например, в OneDrive), общей для нескольких компьютеров.
/// </summary>
public sealed class ConfigStore
{
    private static string _dataDir = AppPaths.Roaming;

    /// <summary>Локальные данные этого компьютера: ярлыки групп, значки, указатель на папку синхронизации.</summary>
    public static string DataDir
    {
        get => _dataDir;
        internal set
        {
            _dataDir = value;
            SyncFolder = ReadSyncFolder();
        }
    }

    private static string SyncPointer => Path.Combine(DataDir, "sync.json");

    /// <summary>Папка синхронизации; null — группы хранятся только на этом компьютере.</summary>
    public static string? SyncFolder { get; private set; } = ReadSyncFolder();

    /// <summary>Где лежит config.json: в папке синхронизации или локально.</summary>
    public static string ConfigDir => SyncFolder ?? DataDir;

    public static string ConfigPath => Path.Combine(ConfigDir, "config.json");

    /// <summary>Картинки, выбранные пользователем, — рядом с config.json, чтобы синхронизировались вместе с ним.</summary>
    public static string UserIconsDir => Path.Combine(ConfigDir, "UserIcons");

    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly DispatcherTimer _saveTimer;

    public ConfigStore()
    {
        _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        _saveTimer.Tick += (_, _) => { _saveTimer.Stop(); SaveNow(); };
    }

    public AppConfig Config { get; private set; } = new();

    /// <summary>При загрузке config.json не было — первый запуск (или первый на этом месте).</summary>
    public bool IsNew { get; private set; }

    /// <summary>config.json поменяли снаружи — другой компьютер через папку синхронизации.</summary>
    public event Action? ExternalChange;

    private string? _lastJson;
    private FileSystemWatcher? _watcher;
    private DispatcherTimer? _externalTimer;

    /// <summary>Группа изменилась (свойства, состав, порядок элементов).</summary>
    public event Action<Group>? GroupChanged;

    public void Load()
    {
        try
        {
            IsNew = !File.Exists(ConfigPath);
            if (!IsNew)
            {
                var json = File.ReadAllText(ConfigPath);
                Config = JsonSerializer.Deserialize<AppConfig>(json, JsonOptions) ?? new AppConfig();
                _lastJson = json;
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Config load");
            try { File.Copy(ConfigPath, ConfigPath + $".broken-{DateTime.Now:yyyyMMddHHmmss}", true); } catch { }
            Config = new AppConfig();
        }

        // Защита от «битых» данных, которые ломали оригинальную программу.
        var seen = new HashSet<string>();
        foreach (var group in Config.Groups.ToList())
        {
            if (group == null) { Config.Groups.Remove(group!); continue; }
            if (string.IsNullOrWhiteSpace(group.Id) || !seen.Add(group.Id)) group.Id = Ids.New();
            foreach (var item in group.Items.Where(i => i == null || string.IsNullOrWhiteSpace(i.Target)).ToList())
                group.Items.Remove(item);
        }

        RemapUserIcons(Config);
        Config.Groups.CollectionChanged += OnGroupsChanged;
        foreach (var group in Config.Groups) group.Changed += OnGroupChanged;
        Config.Settings.PropertyChanged += (_, _) => ScheduleSave();
        WatchSyncFolder();
    }

    public Group? Find(string? id) =>
        id == null ? null : Config.Groups.FirstOrDefault(g => string.Equals(g.Id, id, StringComparison.OrdinalIgnoreCase));

    public void ScheduleSave()
    {
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    public void SaveNow()
    {
        _saveTimer.Stop();
        try
        {
            Directory.CreateDirectory(ConfigDir);
            var json = JsonSerializer.Serialize(Config, JsonOptions);
            if (json == _lastJson && File.Exists(ConfigPath)) return;
            _lastJson = json;
            var temp = ConfigPath + ".tmp";
            File.WriteAllText(temp, json);
            if (File.Exists(ConfigPath)) File.Replace(temp, ConfigPath, ConfigPath + ".bak", true);
            else File.Move(temp, ConfigPath);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Config save");
        }
    }

    // ================= Синхронизация через папку =================

    private static string? ReadSyncFolder()
    {
        try
        {
            if (!File.Exists(SyncPointer)) return null;
            using var doc = JsonDocument.Parse(File.ReadAllText(SyncPointer));
            return doc.RootElement.TryGetProperty("Folder", out var f) && f.GetString() is { Length: > 0 } folder ? folder : null;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Sync pointer");
            return null;
        }
    }

    /// <summary>В папке уже есть группы (например, с другого компьютера).</summary>
    public static bool FolderHasConfig(string folder) => File.Exists(Path.Combine(folder, "config.json"));

    /// <summary>
    /// Включить синхронизацию. <paramref name="useExisting"/> — взять группы из папки, иначе — записать туда текущие.
    /// </summary>
    /// <returns>true — нужно перезапустить программу, чтобы прочитать группы из папки.</returns>
    public bool EnableSync(string folder, bool useExisting)
    {
        folder = Path.GetFullPath(folder);
        Directory.CreateDirectory(folder);
        var oldIcons = UserIconsDir;
        File.WriteAllText(SyncPointer, JsonSerializer.Serialize(new { Folder = folder }, JsonOptions));
        SyncFolder = folder;
        if (useExisting && FolderHasConfig(folder)) return true;

        MoveUserIcons(oldIcons, UserIconsDir);
        _lastJson = null;
        SaveNow();
        WatchSyncFolder();
        return false;
    }

    /// <summary>Выключить синхронизацию: группы остаются на этом компьютере в их текущем виде.</summary>
    public void DisableSync()
    {
        if (SyncFolder == null) return;
        var oldIcons = UserIconsDir;
        StopWatching();
        try { File.Delete(SyncPointer); } catch (Exception ex) { Log.Error(ex, "Sync pointer delete"); }
        SyncFolder = null;
        MoveUserIcons(oldIcons, UserIconsDir);
        _lastJson = null;
        SaveNow();
    }

    /// <summary>Копирует свои картинки в новую папку и переписывает пути к ним в группах.</summary>
    private void MoveUserIcons(string from, string to)
    {
        if (string.Equals(from, to, StringComparison.OrdinalIgnoreCase) || !Directory.Exists(from)) return;
        Directory.CreateDirectory(to);
        foreach (var file in Directory.GetFiles(from))
            try { File.Copy(file, Path.Combine(to, Path.GetFileName(file)), true); } catch (Exception ex) { Log.Error(ex, "User icon copy"); }
        string? Remap(string? path) => path != null && path.StartsWith(from + "\\", StringComparison.OrdinalIgnoreCase)
            ? Path.Combine(to, Path.GetFileName(path))
            : path;
        foreach (var group in Config.Groups)
        {
            group.CustomIconPath = Remap(group.CustomIconPath);
            foreach (var item in group.Items) item.CustomIconPath = Remap(item.CustomIconPath);
        }
    }

    /// <summary>
    /// Пути к своим картинкам записаны на другом компьютере (другое имя пользователя, другая буква диска):
    /// если файла нет, но картинка с тем же именем лежит в UserIcons рядом с config.json, берём её.
    /// </summary>
    internal static void RemapUserIcons(AppConfig config)
    {
        var dir = UserIconsDir;
        string? Fix(string? path)
        {
            if (string.IsNullOrEmpty(path) || File.Exists(path)) return path;
            var local = Path.Combine(dir, Path.GetFileName(path));
            return File.Exists(local) ? local : path;
        }
        foreach (var group in config.Groups)
        {
            group.CustomIconPath = Fix(group.CustomIconPath);
            foreach (var item in group.Items) item.CustomIconPath = Fix(item.CustomIconPath);
        }
    }

    private void WatchSyncFolder()
    {
        StopWatching();
        if (SyncFolder == null || !Directory.Exists(SyncFolder)) return;
        try
        {
            _watcher = new FileSystemWatcher(SyncFolder, "config.json")
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
                EnableRaisingEvents = true,
            };
            FileSystemEventHandler handler = (_, _) => System.Windows.Application.Current?.Dispatcher.BeginInvoke(QueueExternalCheck);
            _watcher.Changed += handler;
            _watcher.Created += handler;
            _watcher.Renamed += (_, _) => System.Windows.Application.Current?.Dispatcher.BeginInvoke(QueueExternalCheck);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Sync watcher");
        }
    }

    private void StopWatching()
    {
        _watcher?.Dispose();
        _watcher = null;
    }

    /// <summary>Облако пишет файл кусками — ждём, пока утихнет, и сравниваем с тем, что знаем.</summary>
    private void QueueExternalCheck()
    {
        _externalTimer ??= new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
        _externalTimer.Tick -= OnExternalTimer;
        _externalTimer.Tick += OnExternalTimer;
        _externalTimer.Stop();
        _externalTimer.Start();
    }

    private void OnExternalTimer(object? sender, EventArgs e)
    {
        _externalTimer!.Stop();
        try
        {
            if (!File.Exists(ConfigPath)) return;
            var json = File.ReadAllText(ConfigPath);
            if (json == _lastJson) return; // это мы сами и сохранили
            JsonSerializer.Deserialize<AppConfig>(json, JsonOptions); // недописанный файл — подождём следующего события
            _lastJson = json;
            ExternalChange?.Invoke();
        }
        catch (Exception ex)
        {
            Log.Info("Sync: config not ready yet — " + ex.Message);
        }
    }

    private void OnGroupsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems != null)
            foreach (Group g in e.OldItems) g.Changed -= OnGroupChanged;
        if (e.NewItems != null)
            foreach (Group g in e.NewItems) g.Changed += OnGroupChanged;
        ScheduleSave();
    }

    private void OnGroupChanged(object? sender, EventArgs e)
    {
        if (sender is Group g) GroupChanged?.Invoke(g);
        ScheduleSave();
    }
}
