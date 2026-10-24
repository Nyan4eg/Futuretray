using System.ComponentModel;
using System.Windows;
using System.Windows.Threading;

namespace Futuretray.Core;

/// <summary>
/// Группы, привязанные к папке: всё, что лежит в папке (файлы, ярлыки, подпапки), появляется в группе само.
/// Элементы, добавленные вручную из других мест, синхронизация не трогает.
/// </summary>
public sealed class FolderSync
{
    private readonly ConfigStore _store;
    private readonly Dictionary<string, FileSystemWatcher> _watchers = new();
    private readonly Dictionary<string, DispatcherTimer> _pending = new();
    private readonly HashSet<Group> _hooked = new();

    public FolderSync(ConfigStore store)
    {
        _store = store;
        store.Config.Groups.CollectionChanged += (_, _) => EnsureAll();
        EnsureAll();
    }

    private void EnsureAll()
    {
        foreach (var group in _store.Config.Groups)
        {
            if (_hooked.Add(group)) group.PropertyChanged += OnGroupPropertyChanged;
            Ensure(group);
        }
        foreach (var id in _watchers.Keys.Where(id => _store.Find(id) == null).ToList()) StopWatching(id);
    }

    private void OnGroupPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(Group.SourceFolder) && sender is Group group) Ensure(group);
    }

    private void Ensure(Group group)
    {
        var folder = group.SourceFolder;
        if (folder == null || !Directory.Exists(folder) || _store.Find(group.Id) == null)
        {
            StopWatching(group.Id);
            return;
        }

        if (!_watchers.TryGetValue(group.Id, out var watcher) || !string.Equals(watcher.Path, folder, StringComparison.OrdinalIgnoreCase))
        {
            StopWatching(group.Id);
            try
            {
                watcher = new FileSystemWatcher(folder)
                {
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName,
                    IncludeSubdirectories = false,
                };
                FileSystemEventHandler changed = (_, _) => Schedule(group);
                watcher.Created += changed;
                watcher.Deleted += changed;
                watcher.Renamed += (_, _) => Schedule(group);
                watcher.EnableRaisingEvents = true;
                _watchers[group.Id] = watcher;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Folder watch " + folder);
            }
        }
        Sync(group);
    }

    private void StopWatching(string id)
    {
        if (_watchers.Remove(id, out var watcher)) watcher.Dispose();
    }

    /// <summary>События файловой системы приходят пачками и в чужом потоке — собираем и синхронизируем в UI.</summary>
    private void Schedule(Group group) => Application.Current?.Dispatcher.BeginInvoke(() =>
    {
        if (!_pending.TryGetValue(group.Id, out var timer))
        {
            timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                if (_store.Find(group.Id) != null) Sync(group);
            };
            _pending[group.Id] = timer;
        }
        timer.Stop();
        timer.Start();
    });

    public static void Sync(Group group)
    {
        var folder = group.SourceFolder;
        if (folder == null || !Directory.Exists(folder)) return;

        List<string> entries;
        try
        {
            entries = Directory.EnumerateFileSystemEntries(folder)
                .Where(path => !IsHidden(path))
                .Select(Normalize)
                .OrderBy(path => !Directory.Exists(path)) // папки первыми
                .ThenBy(path => Path.GetFileName(path), StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Folder sync " + folder);
            return;
        }

        var present = new HashSet<string>(entries, StringComparer.OrdinalIgnoreCase);
        foreach (var item in group.Items.Where(i => IsFromFolder(i, folder) && !present.Contains(Normalize(i.Target))).ToList())
            group.Items.Remove(item);

        var existing = new HashSet<string>(group.Items.Where(i => i.Kind == ItemKind.Path).Select(i => Normalize(i.Target)), StringComparer.OrdinalIgnoreCase);
        foreach (var path in entries.Where(p => !existing.Contains(p)))
            group.Items.Add(ItemFactory.FromPath(path));
    }

    private static bool IsFromFolder(LaunchItem item, string folder) =>
        item.Kind == ItemKind.Path &&
        string.Equals(Path.GetDirectoryName(Normalize(item.Target)), Normalize(folder), StringComparison.OrdinalIgnoreCase);

    private static string Normalize(string path)
    {
        try { return Path.GetFullPath(Environment.ExpandEnvironmentVariables(path)).TrimEnd('\\'); }
        catch { return path; }
    }

    private static bool IsHidden(string path)
    {
        var name = Path.GetFileName(path);
        if (name.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase) || name.StartsWith('~') || name.StartsWith('.')) return true;
        try { return (File.GetAttributes(path) & (FileAttributes.Hidden | FileAttributes.System)) != 0; }
        catch { return true; }
    }
}
