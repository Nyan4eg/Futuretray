using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace Futuretray.Core;

/// <summary>Значок в области уведомлений без WinForms.</summary>
public sealed class TrayIcon : IDisposable
{
    private const uint NIM_ADD = 0, NIM_MODIFY = 1, NIM_DELETE = 2, NIM_SETVERSION = 4;
    private const uint NIF_MESSAGE = 1, NIF_ICON = 2, NIF_TIP = 4, NIF_SHOWTIP = 0x80;
    private const int WM_APP_TRAY = 0x8000 + 17;
    private const int WM_LBUTTONUP = 0x202, WM_RBUTTONUP = 0x205, WM_CONTEXTMENU = 0x7B, NIN_SELECT = 0x400;

    private readonly HwndSource _source;
    private readonly uint _taskbarCreated;
    private IntPtr _icon;
    private bool _added;

    public TrayIcon(string tooltip)
    {
        Tooltip = tooltip;
        _taskbarCreated = Native.RegisterWindowMessage("TaskbarCreated");
        _source = new HwndSource(new HwndSourceParameters("FuturetrayTray") { Width = 0, Height = 0, WindowStyle = 0 });
        _source.AddHook(WndProc);

        int size = Native.GetSystemMetrics(49 /* SM_CXSMICON */);
        Native.SHDefExtractIcon(ShortcutManager.ExePath, 0, 0, out var large, out _icon, (uint)((size << 16) | 32));
        if (large != IntPtr.Zero) Native.DestroyIcon(large);
        Add();
    }

    public string Tooltip { get; }
    public IntPtr Handle => _source.Handle;

    public event Action? Click;
    public event Action? RightClick;

    private Native.NOTIFYICONDATA Data(uint flags) => new()
    {
        cbSize = Marshal.SizeOf<Native.NOTIFYICONDATA>(),
        hWnd = _source.Handle,
        uID = 1,
        uFlags = flags,
        uCallbackMessage = WM_APP_TRAY,
        hIcon = _icon,
        szTip = Tooltip,
        szInfo = "",
        szInfoTitle = "",
        uVersion = 4,
    };

    private void Add()
    {
        var data = Data(NIF_MESSAGE | NIF_ICON | NIF_TIP | NIF_SHOWTIP);
        _added = Native.Shell_NotifyIcon(NIM_ADD, ref data);
        if (_added) Native.Shell_NotifyIcon(NIM_SETVERSION, ref data);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_APP_TRAY)
        {
            int ev = (int)((long)lParam & 0xFFFF);
            if (ev is WM_LBUTTONUP or NIN_SELECT) Click?.Invoke();
            else if (ev is WM_RBUTTONUP or WM_CONTEXTMENU) RightClick?.Invoke();
            handled = true;
        }
        else if (msg == (int)_taskbarCreated)
        {
            // Проводник перезапустился — возвращаем значок.
            Add();
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        if (_added)
        {
            var data = Data(0);
            Native.Shell_NotifyIcon(NIM_DELETE, ref data);
            _added = false;
        }
        if (_icon != IntPtr.Zero) Native.DestroyIcon(_icon);
        _icon = IntPtr.Zero;
        _source.Dispose();
    }
}
