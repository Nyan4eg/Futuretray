using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shell;

namespace Futuretray.Core;

/// <summary>Нативные эффекты Windows 11: Mica/Acrylic, скругления, тёмный заголовок.</summary>
public static class WindowEffects
{
    public static int Build { get; } = Environment.OSVersion.Version.Build;
    public static bool SupportsBackdrop => Build >= 22621;
    public static bool SupportsRoundCorners => Build >= 22000;

    /// <summary>Подключает WindowChrome. Вызывать до создания HWND.</summary>
    public static void InstallChrome(Window window, double captionHeight, double resizeBorder)
    {
        WindowChrome.SetWindowChrome(window, new WindowChrome
        {
            CaptionHeight = captionHeight,
            ResizeBorderThickness = new Thickness(resizeBorder),
            GlassFrameThickness = SupportsBackdrop ? new Thickness(-1) : new Thickness(0),
            UseAeroCaptionButtons = SupportsBackdrop,
            CornerRadius = new CornerRadius(0),
            NonClientFrameEdges = NonClientFrameEdges.None,
        });
    }

    /// <returns>true, если системный фон (Mica/Acrylic) включён и окно должно быть прозрачным.</returns>
    public static bool Apply(Window window, BackdropKind backdrop, bool dark, bool smallCorners = false)
    {
        var hwnd = new WindowInteropHelper(window).EnsureHandle();

        int darkMode = dark ? 1 : 0;
        Native.DwmSetWindowAttribute(hwnd, Native.DWMWA_USE_IMMERSIVE_DARK_MODE, ref darkMode, sizeof(int));

        if (SupportsRoundCorners)
        {
            int corner = smallCorners ? 3 : 2;
            Native.DwmSetWindowAttribute(hwnd, Native.DWMWA_WINDOW_CORNER_PREFERENCE, ref corner, sizeof(int));
        }

        bool useBackdrop = SupportsBackdrop && backdrop != BackdropKind.Solid;
        if (SupportsBackdrop)
        {
            int type = backdrop switch
            {
                BackdropKind.Acrylic => 3,
                BackdropKind.Mica => 2,
                _ => 1,
            };
            Native.DwmSetWindowAttribute(hwnd, Native.DWMWA_SYSTEMBACKDROP_TYPE, ref type, sizeof(int));
        }

        if (HwndSource.FromHwnd(hwnd) is { CompositionTarget: { } target })
            target.BackgroundColor = useBackdrop ? Colors.Transparent : (dark ? Color.FromRgb(0x20, 0x20, 0x20) : Color.FromRgb(0xF3, 0xF3, 0xF3));

        return useBackdrop;
    }

    public static void MakeToolWindow(Window window)
    {
        var hwnd = new WindowInteropHelper(window).EnsureHandle();
        var ex = (long)Native.GetWindowLongPtr(hwnd, Native.GWL_EXSTYLE);
        ex = (ex | Native.WS_EX_TOOLWINDOW) & ~Native.WS_EX_APPWINDOW;
        Native.SetWindowLongPtr(hwnd, Native.GWL_EXSTYLE, new IntPtr(ex));

        // Без системного меню DWM не рисует кнопки «свернуть/закрыть» поверх стекла.
        var style = (long)Native.GetWindowLongPtr(hwnd, Native.GWL_STYLE);
        style &= ~(Native.WS_SYSMENU | Native.WS_MINIMIZEBOX | Native.WS_MAXIMIZEBOX);
        Native.SetWindowLongPtr(hwnd, Native.GWL_STYLE, new IntPtr(style));
        Native.SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0,
            Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOZORDER | Native.SWP_NOACTIVATE | Native.SWP_FRAMECHANGED);
    }

    /// <summary>Надёжно выводит окно на передний план.</summary>
    public static void ForceForeground(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero) return;
        window.Activate();
        if (Native.GetForegroundWindow() == hwnd) return;

        var foreground = Native.GetForegroundWindow();
        uint foregroundThread = Native.GetWindowThreadProcessId(foreground, out _);
        uint currentThread = Native.GetCurrentThreadId();
        if (foregroundThread != currentThread) Native.AttachThreadInput(currentThread, foregroundThread, true);
        Native.BringWindowToTop(hwnd);
        Native.SetForegroundWindow(hwnd);
        if (foregroundThread != currentThread) Native.AttachThreadInput(currentThread, foregroundThread, false);
        window.Activate();
    }
}
