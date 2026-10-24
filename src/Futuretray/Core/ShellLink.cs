using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;

namespace Futuretray.Core;

public sealed record LinkInfo(string Target, string Arguments, string IconPath, string? AppUserModelId = null);

/// <summary>Создание и чтение .lnk через IShellLink, включая AppUserModelID.</summary>
internal static class ShellLink
{
    public static void Save(string path, string target, string arguments, string? workingDirectory,
        string iconPath, string? description, string? appUserModelId)
    {
        var link = (IShellLinkW)new CShellLink();
        try
        {
            link.SetPath(target);
            link.SetArguments(arguments);
            if (workingDirectory != null) link.SetWorkingDirectory(workingDirectory);
            link.SetIconLocation(iconPath, 0);
            if (!string.IsNullOrEmpty(description)) link.SetDescription(description);
            if (appUserModelId != null) SetAppId((IPropertyStore)link, appUserModelId);
            ((IPersistFile)link).Save(path, true);
        }
        finally
        {
            Marshal.FinalReleaseComObject(link);
        }
    }

    /// <summary>Пункт списка переходов: ссылка в памяти (не файл) с заголовком и значком.</summary>
    public static object CreateTask(string target, string arguments, string title, string iconPath, string? description)
    {
        var link = (IShellLinkW)new CShellLink();
        link.SetPath(target);
        link.SetArguments(arguments);
        link.SetIconLocation(iconPath, 0);
        if (!string.IsNullOrEmpty(description)) link.SetDescription(description);
        PropertyStore.SetString((IPropertyStore)link, ref PropertyStore.Title, title);
        return link;
    }

    public static object CreateSeparator()
    {
        var link = (IShellLinkW)new CShellLink();
        PropertyStore.SetBool((IPropertyStore)link, ref PropertyStore.IsDestListSeparator, true);
        return link;
    }

    public static LinkInfo? Read(string path)
    {
        var link = (IShellLinkW)new CShellLink();
        try
        {
            ((IPersistFile)link).Load(path, 0);
            var target = new StringBuilder(1024);
            link.GetPath(target, target.Capacity, IntPtr.Zero, 0x4 /* SLGP_RAWPATH */);
            var args = new StringBuilder(2048);
            link.GetArguments(args, args.Capacity);
            var icon = new StringBuilder(1024);
            link.GetIconLocation(icon, icon.Capacity, out _);
            var appId = PropertyStore.GetString((IPropertyStore)link, ref PropertyStore.AppUserModelId);
            return new LinkInfo(target.ToString(), args.ToString(), icon.ToString(), appId);
        }
        catch
        {
            return null;
        }
        finally
        {
            Marshal.FinalReleaseComObject(link);
        }
    }

    /// <summary>Обновляет цель и иконку существующего ярлыка, сохраняя всё остальное (в т.ч. закрепление).</summary>
    public static bool Update(string path, string target, string? workingDirectory, string iconPath, string? appUserModelId)
    {
        var link = (IShellLinkW)new CShellLink();
        try
        {
            ((IPersistFile)link).Load(path, 2 /* STGM_READWRITE */);
            link.SetPath(target);
            if (workingDirectory != null) link.SetWorkingDirectory(workingDirectory);
            link.SetIconLocation(iconPath, 0);
            if (appUserModelId != null) SetAppId((IPropertyStore)link, appUserModelId);
            ((IPersistFile)link).Save(null, true);
            return true;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Update link " + path);
            return false;
        }
        finally
        {
            Marshal.FinalReleaseComObject(link);
        }
    }

    private static void SetAppId(IPropertyStore store, string appId) =>
        PropertyStore.SetString(store, ref PropertyStore.AppUserModelId, appId);

    [ComImport, Guid("00021401-0000-0000-C000-000000000046")]
    private class CShellLink { }

    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("000214F9-0000-0000-C000-000000000046")]
    private interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder file, int maxPath, IntPtr findData, uint flags);
        void GetIDList(out IntPtr pidl);
        void SetIDList(IntPtr pidl);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder name, int maxName);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string name);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder dir, int maxPath);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string dir);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder args, int maxPath);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string args);
        void GetHotkey(out short hotkey);
        void SetHotkey(short hotkey);
        void GetShowCmd(out int showCmd);
        void SetShowCmd(int showCmd);
        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder iconPath, int iconPathLength, out int iconIndex);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string iconPath, int iconIndex);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string relativePath, uint reserved);
        void Resolve(IntPtr hwnd, uint flags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string file);
    }
}
