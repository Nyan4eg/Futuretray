using System.Runtime.InteropServices;

namespace Futuretray.Core;

public sealed record MonitorInfo(IntPtr Handle, string Device, Native.RECT Bounds, Native.RECT Work, bool IsPrimary)
{
    public uint Dpi => Native.GetMonitorDpi(Handle);
}

public static class Monitors
{
    public static List<MonitorInfo> All()
    {
        var list = new List<MonitorInfo>();
        Native.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr handle, IntPtr hdc, ref Native.RECT rect, IntPtr data) =>
        {
            var info = new Native.MONITORINFOEX { cbSize = Marshal.SizeOf<Native.MONITORINFOEX>(), szDevice = "" };
            if (Native.GetMonitorInfoEx(handle, ref info))
                list.Add(new MonitorInfo(handle, info.szDevice, info.rcMonitor, info.rcWork, (info.dwFlags & 1) != 0));
            return true;
        }, IntPtr.Zero);
        // Слева направо, как их видит пользователь.
        return list.OrderBy(m => m.Bounds.Left).ThenBy(m => m.Bounds.Top).ToList();
    }

    /// <summary>Монитор по имени устройства; если его отключили — основной.</summary>
    public static MonitorInfo Find(string? device)
    {
        var all = All();
        return all.FirstOrDefault(m => device != null && string.Equals(m.Device, device, StringComparison.OrdinalIgnoreCase))
               ?? all.FirstOrDefault(m => m.IsPrimary)
               ?? all.First();
    }

    public static MonitorInfo FromPoint(Native.POINT point)
    {
        var handle = Native.MonitorFromPoint(point, Native.MONITOR_DEFAULTTONEAREST);
        return All().FirstOrDefault(m => m.Handle == handle) ?? Find(null);
    }
}
