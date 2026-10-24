namespace Futuretray.Core;

public enum TaskbarEdge { None, Left, Top, Right, Bottom }

/// <summary>Где на экране показать всплывающее окно группы (все координаты — физические пиксели).</summary>
public readonly record struct Placement(IntPtr Monitor, Native.RECT Available, TaskbarEdge Edge, bool FromTaskbar)
{
    public Native.POINT Place(int width, int height, Native.POINT cursor, int gap)
    {
        int x, y;
        var edge = FromTaskbar ? Edge : TaskbarEdge.None;
        switch (edge)
        {
            case TaskbarEdge.Bottom:
                x = cursor.X - width / 2;
                y = Available.Bottom - gap - height;
                break;
            case TaskbarEdge.Top:
                x = cursor.X - width / 2;
                y = Available.Top + gap;
                break;
            case TaskbarEdge.Left:
                x = Available.Left + gap;
                y = cursor.Y - height / 2;
                break;
            case TaskbarEdge.Right:
                x = Available.Right - gap - width;
                y = cursor.Y - height / 2;
                break;
            default:
                // Запуск не с панели задач (рабочий стол, «Пуск», клавиатура) — рядом с курсором.
                x = cursor.X - width / 2;
                y = cursor.Y - height - gap;
                if (y < Available.Top + gap) y = cursor.Y + gap;
                break;
        }

        x = Clamp(x, Available.Left + gap, Available.Right - gap - width);
        y = Clamp(y, Available.Top + gap, Available.Bottom - gap - height);
        return new Native.POINT(x, y);
    }

    private static int Clamp(int value, int min, int max) => max < min ? min : Math.Clamp(value, min, max);
}

public static class TaskbarInfo
{
    public static Placement Locate(Native.POINT cursor)
    {
        var monitor = Native.MonitorFromPoint(cursor, Native.MONITOR_DEFAULTTONEAREST);
        var info = new Native.MONITORINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<Native.MONITORINFO>() };
        Native.GetMonitorInfo(monitor, ref info);

        var available = info.rcWork;
        var edge = TaskbarEdge.None;
        bool fromTaskbar = false;

        foreach (var hwnd in Taskbars())
        {
            if (!Native.GetWindowRect(hwnd, out var bar) || !bar.Intersects(info.rcMonitor)) continue;

            edge = DetectEdge(bar, info.rcMonitor);
            fromTaskbar = bar.Inflate(6).Contains(cursor);

            // Работает и с автоскрытием: панель в этот момент выдвинута, а рабочая область её не учитывает.
            switch (edge)
            {
                case TaskbarEdge.Bottom: available.Bottom = Math.Min(available.Bottom, bar.Top); break;
                case TaskbarEdge.Top: available.Top = Math.Max(available.Top, bar.Bottom); break;
                case TaskbarEdge.Left: available.Left = Math.Max(available.Left, bar.Right); break;
                case TaskbarEdge.Right: available.Right = Math.Min(available.Right, bar.Left); break;
            }
            break;
        }

        return new Placement(monitor, available, edge, fromTaskbar);
    }

    private static IEnumerable<IntPtr> Taskbars()
    {
        var main = Native.FindWindow("Shell_TrayWnd", null);
        if (main != IntPtr.Zero) yield return main;
        var h = IntPtr.Zero;
        while ((h = Native.FindWindowEx(IntPtr.Zero, h, "Shell_SecondaryTrayWnd", null)) != IntPtr.Zero)
            yield return h;
    }

    private static TaskbarEdge DetectEdge(Native.RECT bar, Native.RECT monitor)
    {
        if (bar.Width >= bar.Height)
            return (bar.Top + bar.Bottom) / 2 < (monitor.Top + monitor.Bottom) / 2 ? TaskbarEdge.Top : TaskbarEdge.Bottom;
        return (bar.Left + bar.Right) / 2 < (monitor.Left + monitor.Right) / 2 ? TaskbarEdge.Left : TaskbarEdge.Right;
    }
}
