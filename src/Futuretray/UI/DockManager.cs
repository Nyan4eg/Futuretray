using System.Windows;
using System.Windows.Threading;
using Futuretray.Core;
using Microsoft.Win32;

namespace Futuretray.UI;

/// <summary>Держит открытыми треи тех групп, у которых включено «Показывать на рабочем столе».</summary>
public sealed class DockManager
{
    private readonly ConfigStore _store;
    private readonly Dictionary<string, DockWindow> _docks = new();
    private readonly HashSet<string> _queued = new();
    private bool _shuttingDown;

    public DockManager(ConfigStore store)
    {
        _store = store;
        store.GroupChanged += Queue;
        store.Config.Groups.CollectionChanged += (_, _) => Dispatch(SyncAll);
        ThemeManager.Changed += () => Dispatch(ReapplyAll);
        SystemEvents.DisplaySettingsChanged += (_, _) => Dispatch(ReapplyAll);
    }

    public void SyncAll()
    {
        foreach (var group in _store.Config.Groups) Sync(group);
        foreach (var id in _docks.Keys.Where(id => _store.Find(id) == null).ToList()) Close(id);
    }

    public void CloseAll()
    {
        _shuttingDown = true;
        foreach (var id in _docks.Keys.ToList()) Close(id);
    }

    private void Queue(Group group)
    {
        if (!_queued.Add(group.Id)) return;
        Dispatch(() =>
        {
            _queued.Remove(group.Id);
            Sync(group);
        });
    }

    private void Sync(Group group)
    {
        if (_shuttingDown) return;
        bool wanted = group.DockEnabled && _store.Config.Groups.Contains(group);
        if (!wanted)
        {
            Close(group.Id);
            return;
        }

        if (!_docks.TryGetValue(group.Id, out var dock))
        {
            dock = new DockWindow(group);
            var created = dock;
            dock.Closed += (_, _) => OnDockClosed(group, created);
            _docks[group.Id] = dock;
        }

        try
        {
            dock.Apply();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Dock " + group.Name);
        }
    }

    private void OnDockClosed(Group group, DockWindow dock)
    {
        if (!_docks.TryGetValue(group.Id, out var current) || current != dock) return;
        _docks.Remove(group.Id);
        if (_shuttingDown || dock.ClosedByCode) return;

        // Окно закрыла не программа (например, перезапустился Проводник) — восстанавливаем.
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            Sync(group);
        };
        timer.Start();
    }

    private void Close(string id)
    {
        if (!_docks.Remove(id, out var dock)) return;
        dock.CloseDock();
    }

    private void ReapplyAll()
    {
        foreach (var dock in _docks.Values.ToList())
        {
            try { dock.Apply(); }
            catch (Exception ex) { Log.Error(ex, "Dock reapply"); }
        }
    }

    private static void Dispatch(Action action) =>
        Application.Current?.Dispatcher.BeginInvoke(action, DispatcherPriority.Background);
}
