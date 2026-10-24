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

    /// <summary>
    /// Папка синхронизации недоступна (OneDrive ещё не запущен, файл не докачан или испорчен):
    /// работаем с локальной копией и ничего не пишем в общую папку, пока она не оживёт.
    /// </summary>
    public bool SyncUnavailable { get; private set; }

    /// <summary>Сохранять больше нельзя: в памяти устаревшие группы, программа сейчас перезапустится.</summary>
    private bool _frozen;

    private string? _lastJson;       // что лежит в config.json (общем или локальном), насколько мы знаем
    private string? _lastLocalJson;  // локальная копия при синхронизации
    private string? _lastStateJson;  // local.json
    private FileSystemWatcher? _watcher;
    private DispatcherTimer? _externalTimer;
    private DispatcherTimer? _retryTimer;

    /// <summary>Группа изменилась (свойства, состав, порядок элементов).</summary>
    public event Action<Group>? GroupChanged;

    /// <summary>Локальная копия групп: при синхронизации — запасная на случай недоступной папки.</summary>
    private static string LocalConfigPath => Path.Combine(DataDir, "config.json");

    /// <summary>То, что относится только к этому компьютеру и не синхронизируется.</summary>
    private static string LocalStatePath => Path.Combine(DataDir, "local.json");

    private sealed class LocalState
    {
        public DateTime? LastUpdateCheck { get; set; }
        public string? LastSeenVersion { get; set; }
    }

    public void Load()
    {
        if (SyncFolder != null)
        {
            // Общая папка: при любой неудаче — локальная копия, а в папку не пишем ни байта.
            if (TryRead(ConfigPath, out var shared, out var sharedJson))
            {
                Config = shared;
                _lastJson = sharedJson;
            }
            else
            {
                SyncUnavailable = true;
                Log.Info("Sync folder unavailable, using the local copy: " + ConfigPath);
                if (TryRead(LocalConfigPath, out var local, out var localJson))
                {
                    Config = local;
                    _lastLocalJson = localJson;
                }
            }
        }
        else
        {
            IsNew = !File.Exists(ConfigPath);
            if (!IsNew && TryRead(ConfigPath, out var config, out var json))
            {
                Config = config;
                _lastJson = json;
            }
            else if (!IsNew)
            {
                try { File.Copy(ConfigPath, ConfigPath + $".broken-{DateTime.Now:yyyyMMddHHmmss}", true); } catch { }
            }
        }

        LoadLocalState();
        RemapUserIcons(Config);
        Config.Groups.CollectionChanged += OnGroupsChanged;
        foreach (var group in Config.Groups) group.Changed += OnGroupChanged;
        Config.Settings.PropertyChanged += (_, _) => ScheduleSave();
        WatchSyncFolder();
    }

    /// <summary>Читает и проверяет config.json. Неполный или испорченный файл — false, без исключений.</summary>
    private static bool TryRead(string path, out AppConfig config, out string json)
    {
        config = new AppConfig();
        json = "";
        try
        {
            if (!File.Exists(path)) return false;
            json = File.ReadAllText(path);
            var parsed = JsonSerializer.Deserialize<AppConfig>(json, JsonOptions);
            if (parsed == null) return false;
            Normalize(parsed);
            config = parsed;
            return true;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Config load " + path);
            return false;
        }
    }

    /// <summary>Защита от «битых» данных, которые ломали оригинальную программу (в том числе null вместо списков).</summary>
    private static void Normalize(AppConfig config)
    {
        config.Settings ??= new AppSettings();
        config.Groups ??= new();
        var seen = new HashSet<string>();
        foreach (var group in config.Groups.ToList())
        {
            if (group == null) { config.Groups.Remove(group!); continue; }
            if (string.IsNullOrWhiteSpace(group.Id) || !seen.Add(group.Id)) group.Id = Ids.New();
            group.Items ??= new();
            foreach (var item in group.Items.Where(i => i == null || string.IsNullOrWhiteSpace(i.Target)).ToList())
                group.Items.Remove(item);
        }
    }

    private void LoadLocalState()
    {
        try
        {
            if (!File.Exists(LocalStatePath)) return;
            _lastStateJson = File.ReadAllText(LocalStatePath);
            var state = JsonSerializer.Deserialize<LocalState>(_lastStateJson, JsonOptions);
            if (state == null) return;
            Config.Settings.LastUpdateCheck = state.LastUpdateCheck;
            Config.Settings.LastSeenVersion = state.LastSeenVersion;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Local state load");
        }
    }

    public Group? Find(string? id) =>
        id == null ? null : Config.Groups.FirstOrDefault(g => string.Equals(g.Id, id, StringComparison.OrdinalIgnoreCase));

    public void ScheduleSave()
    {
        if (_frozen) return;
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    /// <summary>Больше ничего не сохранять: в памяти устаревшие группы, сейчас будет перезапуск.</summary>
    public void Freeze()
    {
        _frozen = true;
        _saveTimer.Stop();
        _retryTimer?.Stop();
    }

    /// <returns>false — записать не удалось (повторим через несколько секунд).</returns>
    public bool SaveNow()
    {
        _saveTimer.Stop();
        if (_frozen) return true;
        bool ok = true;
        try
        {
            var json = JsonSerializer.Serialize(Config, JsonOptions);
            if (SyncFolder == null)
            {
                ok = WriteIfChanged(ConfigPath, json, ref _lastJson);
            }
            else
            {
                // Локальная копия — всегда; общая папка — только если она жива.
                ok = WriteIfChanged(LocalConfigPath, json, ref _lastLocalJson);
                if (!SyncUnavailable) ok &= WriteIfChanged(ConfigPath, json, ref _lastJson);
            }
            var state = JsonSerializer.Serialize(new LocalState
            {
                LastUpdateCheck = Config.Settings.LastUpdateCheck,
                LastSeenVersion = Config.Settings.LastSeenVersion,
            }, JsonOptions);
            ok &= WriteIfChanged(LocalStatePath, state, ref _lastStateJson);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Config save");
            ok = false;
        }

        if (!ok)
        {
            // Облако или антивирус держат файл — попробуем ещё раз, а не потеряем изменения.
            _retryTimer ??= CreateRetryTimer();
            _retryTimer.Stop();
            _retryTimer.Start();
        }
        return ok;
    }

    private DispatcherTimer CreateRetryTimer()
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        timer.Tick += (_, _) => { timer.Stop(); SaveNow(); };
        return timer;
    }

    /// <summary>Атомарная запись через временный файл. Запомненное содержимое меняется только после успеха.</summary>
    private static bool WriteIfChanged(string path, string json, ref string? last)
    {
        if (json == last && File.Exists(path)) return true;
        var temp = path + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(temp, json);
            if (File.Exists(path)) File.Replace(temp, path, path + ".bak", true);
            else File.Move(temp, path);
            last = json;
            return true;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Config write " + path);
            try { File.Delete(temp); } catch { }
            return false;
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
        if (useExisting && TryRead(Path.Combine(folder, "config.json"), out _, out _))
        {
            // Группы возьмёт новая копия программы; эта больше ничего не пишет — иначе затёрла бы их своими.
            WriteSyncPointer(folder);
            Freeze();
            return true;
        }

        // Сначала пишем группы в папку и только потом переключаемся на неё: не вышло — всё остаётся как было.
        var json = JsonSerializer.Serialize(Config, JsonOptions);
        string? written = null;
        if (!WriteIfChanged(Path.Combine(folder, "config.json"), json, ref written))
            throw new IOException(Loc.F("Не удалось записать в папку {0}", folder));

        var oldIcons = UserIconsDir;
        WriteSyncPointer(folder);
        SyncFolder = folder;
        SyncUnavailable = false;
        _lastJson = written;
        MoveUserIcons(oldIcons, UserIconsDir);
        SaveNow();
        WatchSyncFolder();
        return false;
    }

    private static void WriteSyncPointer(string folder)
    {
        Directory.CreateDirectory(DataDir);
        File.WriteAllText(SyncPointer, JsonSerializer.Serialize(new { Folder = folder }, JsonOptions));
    }

    /// <summary>Выключить синхронизацию: группы остаются на этом компьютере в их текущем виде.</summary>
    public void DisableSync()
    {
        if (SyncFolder == null) return;
        var oldIcons = UserIconsDir;
        StopWatching();
        try { File.Delete(SyncPointer); } catch (Exception ex) { Log.Error(ex, "Sync pointer delete"); }
        SyncFolder = null;
        SyncUnavailable = false;
        MoveUserIcons(oldIcons, UserIconsDir);
        _lastJson = _lastLocalJson;
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
        if (SyncFolder == null) return;
        // Папка недоступна или пропала — проверяем раз в полминуты, не ожила ли она.
        _pollTimer ??= new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        _pollTimer.Tick -= OnPoll;
        _pollTimer.Tick += OnPoll;
        _pollTimer.Start();
        if (!Directory.Exists(SyncFolder)) return;
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
            // Переполнение буфера или отключение сетевого диска — пересоздадим наблюдение.
            _watcher.Error += (_, _) => System.Windows.Application.Current?.Dispatcher.BeginInvoke(() => { WatchSyncFolder(); QueueExternalCheck(); });
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
        _pollTimer?.Stop();
    }

    private DispatcherTimer? _pollTimer;

    private void OnPoll(object? sender, EventArgs e)
    {
        if (_watcher == null && SyncFolder != null && Directory.Exists(SyncFolder)) WatchSyncFolder();
        QueueExternalCheck();
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
        if (_frozen || SyncFolder == null) return;
        // Недописанный или испорченный файл — подождём следующего события.
        if (!TryRead(ConfigPath, out _, out var json)) return;
        if (json == _lastJson) return; // это мы сами и сохранили
        // Папка ожила после недоступности или файл поменял другой компьютер: перечитываем его,
        // а свои устаревшие группы больше не сохраняем, чтобы не затереть чужие изменения.
        Freeze();
        ExternalChange?.Invoke();
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
