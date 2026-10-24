using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace Futuretray.Core;

/// <summary>
/// Список переходов закреплённой группы: правый клик по её значку на панели задач показывает
/// программы группы родным меню Windows. Список привязан к AppUserModelID ярлыка группы.
/// Пункты запускаются через Futuretray (--launch), поэтому работают ссылки, приложения Store
/// и переключение на уже открытое окно — так же, как в окне группы.
/// </summary>
internal static class JumpLists
{
    private const int MaxItems = 40;

    public static string ItemIconsDir => Path.Combine(ShortcutManager.IconsDir, "items");

    public static void Update(Group group, string appId)
    {
        var list = (ICustomDestinationList)new CDestinationList();
        try
        {
            list.SetAppID(appId);
            var iid = typeof(IObjectArray).GUID;
            list.BeginList(out _, ref iid, out _);

            var exe = ShortcutManager.ExePath;
            var tasks = (IObjectCollection)new CEnumerableObjectCollection();
            foreach (var item in group.Items.Take(MaxItems))
                tasks.AddObject(ShellLink.CreateTask(exe, $"--launch {group.Id} {item.Id}", item.Name, ItemIcon(item), item.KindLabel));
            if (group.Items.Count > 0) tasks.AddObject(ShellLink.CreateSeparator());
            if (group.Items.Count > 0)
            {
                tasks.AddObject(ShellLink.CreateTask(exe, $"--arrange {group.Id}",
                    group.HasLayout ? Loc.T("Открыть все и разложить окна") : Loc.T("Открыть все"), exe, null));
                tasks.AddObject(ShellLink.CreateTask(exe, $"--focus {group.Id}", Loc.T("Свернуть остальное"), exe, null));
                tasks.AddObject(ShellLink.CreateTask(exe, $"--close {group.Id}", Loc.T("Закрыть все программы группы"), exe, null));
            }
            tasks.AddObject(ShellLink.CreateTask(exe, $"--editor {group.Id}", Loc.T("Изменить группу…"), exe, null));

            list.AddUserTasks((IObjectArray)tasks);
            list.CommitList();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Jump list " + group.Name);
            try { list.AbortList(); } catch { }
        }
        finally
        {
            Marshal.ReleaseComObject(list);
        }
    }

    public static void Delete(string appId)
    {
        var list = (ICustomDestinationList)new CDestinationList();
        try { list.DeleteList(appId); }
        catch (Exception ex) { Log.Error(ex, "Jump list delete"); }
        finally { Marshal.ReleaseComObject(list); }
    }

    /// <summary>Убирает значки элементов, которых уже нет ни в одной группе.</summary>
    public static void CleanupIcons(IEnumerable<Group> groups)
    {
        if (!Directory.Exists(ItemIconsDir)) return;
        var used = groups.SelectMany(g => g.Items).Select(IconFile).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var file in Directory.GetFiles(ItemIconsDir, "*.ico"))
            if (!used.Contains(file))
                try { File.Delete(file); } catch { }
    }

    private static string IconFile(LaunchItem item)
    {
        var hash = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(item.IconKey)))[..12].ToLowerInvariant();
        return Path.Combine(ItemIconsDir, hash + ".ico");
    }

    private static string ItemIcon(LaunchItem item)
    {
        var path = IconFile(item);
        if (!File.Exists(path))
        {
            try { GroupIconRenderer.WriteItemIco(item, path); }
            catch (Exception ex) { Log.Error(ex, "Jump list icon " + item.Name); }
        }
        return File.Exists(path) ? path : ShortcutManager.ExePath;
    }

    [ComImport, Guid("77f10cf0-3db5-4966-b520-b7c54fd35ed6")]
    private class CDestinationList { }

    [ComImport, Guid("2d3468c1-36a7-43b6-ac24-d3f02fd9607a")]
    private class CEnumerableObjectCollection { }

    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("6332debf-87b5-4670-90c0-5e57b408a49e")]
    private interface ICustomDestinationList
    {
        void SetAppID([MarshalAs(UnmanagedType.LPWStr)] string appId);
        void BeginList(out uint minSlots, ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out object removed);
        void AppendCategory([MarshalAs(UnmanagedType.LPWStr)] string category, IObjectArray items);
        void AppendKnownCategory(int category);
        void AddUserTasks(IObjectArray tasks);
        void CommitList();
        void GetRemovedDestinations(ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out object removed);
        void DeleteList([MarshalAs(UnmanagedType.LPWStr)] string appId);
        void AbortList();
    }

    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("92ca9dcd-5622-4bba-a805-5e9f541bd8c9")]
    private interface IObjectArray
    {
        void GetCount(out uint count);
        void GetAt(uint index, ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out object item);
    }

    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("5632b1a4-e38a-400a-928a-d4cd63230295")]
    private interface IObjectCollection
    {
        // Методы IObjectArray — интерфейс наследуется, порядок в vtable важен.
        void GetCount(out uint count);
        void GetAt(uint index, ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out object item);
        void AddObject([MarshalAs(UnmanagedType.Interface)] object item);
        void AddFromArray(IObjectArray source);
        void RemoveObjectAt(uint index);
        void Clear();
    }
}
