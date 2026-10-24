using System.Collections.Specialized;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Threading;

namespace Futuretray.Core;

/// <summary>Хранит настройки и группы в %AppData%\Futuretray\config.json с автосохранением.</summary>
public sealed class ConfigStore
{
    public static string DataDir { get; } = Environment.GetEnvironmentVariable("FUTURETRAY_DATA") is { Length: > 0 } custom
        ? custom
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Futuretray");

    public static string ConfigPath => Path.Combine(DataDir, "config.json");

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

    /// <summary>Группа изменилась (свойства, состав, порядок элементов).</summary>
    public event Action<Group>? GroupChanged;

    public void Load()
    {
        try
        {
            if (File.Exists(ConfigPath))
            {
                var json = File.ReadAllText(ConfigPath);
                Config = JsonSerializer.Deserialize<AppConfig>(json, JsonOptions) ?? new AppConfig();
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

        Config.Groups.CollectionChanged += OnGroupsChanged;
        foreach (var group in Config.Groups) group.Changed += OnGroupChanged;
        Config.Settings.PropertyChanged += (_, _) => ScheduleSave();
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
            Directory.CreateDirectory(DataDir);
            var json = JsonSerializer.Serialize(Config, JsonOptions);
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
