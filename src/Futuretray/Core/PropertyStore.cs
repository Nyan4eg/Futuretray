using System.Runtime.InteropServices;

namespace Futuretray.Core;

/// <summary>IPropertyStore — свойства ярлыков и окон (нам нужен AppUserModelID).</summary>
internal static class PropertyStore
{
    public static PropertyKey AppUserModelId = new(new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"), 5);

    public static void SetString(IPropertyStore store, ref PropertyKey key, string value)
    {
        var pv = new PropVariant { vt = 31 /* VT_LPWSTR */, pointer = Marshal.StringToCoTaskMemUni(value) };
        try
        {
            store.SetValue(ref key, ref pv);
            store.Commit();
        }
        finally
        {
            PropVariantClear(ref pv);
        }
    }

    public static string? GetString(IPropertyStore store, ref PropertyKey key)
    {
        PropVariant pv = default;
        try
        {
            store.GetValue(ref key, out pv);
            return pv.vt == 31 && pv.pointer != IntPtr.Zero ? Marshal.PtrToStringUni(pv.pointer) : null;
        }
        catch
        {
            return null;
        }
        finally
        {
            PropVariantClear(ref pv);
        }
    }

    /// <summary>AppUserModelID, заданный окну (так помечены окна Store-приложений и многих программ).</summary>
    public static string? WindowAppId(IntPtr hwnd)
    {
        var iid = typeof(IPropertyStore).GUID;
        if (SHGetPropertyStoreForWindow(hwnd, ref iid, out var store) != 0 || store == null) return null;
        try
        {
            return GetString(store, ref AppUserModelId);
        }
        finally
        {
            Marshal.ReleaseComObject(store);
        }
    }

    [DllImport("ole32.dll")]
    private static extern int PropVariantClear(ref PropVariant pvar);

    [DllImport("shell32.dll")]
    private static extern int SHGetPropertyStoreForWindow(IntPtr hwnd, ref Guid iid,
        [MarshalAs(UnmanagedType.Interface)] out IPropertyStore? store);
}

[StructLayout(LayoutKind.Sequential, Pack = 4)]
internal struct PropertyKey
{
    public Guid FormatId;
    public uint PropertyId;
    public PropertyKey(Guid formatId, uint propertyId) { FormatId = formatId; PropertyId = propertyId; }
}

[StructLayout(LayoutKind.Explicit, Size = 24)]
internal struct PropVariant
{
    [FieldOffset(0)] public ushort vt;
    [FieldOffset(8)] public IntPtr pointer;
}

[ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IPropertyStore
{
    void GetCount(out uint count);
    void GetAt(uint index, out PropertyKey key);
    void GetValue(ref PropertyKey key, out PropVariant value);
    void SetValue(ref PropertyKey key, ref PropVariant value);
    void Commit();
}
