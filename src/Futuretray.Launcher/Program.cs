using System.Diagnostics;
using System.Runtime.InteropServices;
using Futuretray.Core;

namespace Futuretray.Launcher;

/// <summary>
/// Значок группы на панели задач запускает этот exe. Если Futuretray уже работает в фоне, команда
/// уходит ему по именованному каналу за несколько миллисекунд — без запуска среды .NET и WPF.
/// Иначе (или для команд, которые запускателю не нужны) просто запускаем Futuretray.exe с теми же аргументами.
/// </summary>
internal static class Program
{
    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X, Y;
    }

    [DllImport("user32.dll")] private static extern int GetCursorPos(out Point point);
    [DllImport("user32.dll")] private static extern int AllowSetForegroundWindow(int processId);

    private const int AsfwAny = -1;

    private static int Main(string[] args)
    {
        var command = HostCommand.Parse(args);
        if (Forwardable(command, args))
        {
            if (command.Kind == CommandKind.Popup && GetCursorPos(out var cursor) != 0)
                command = command with { X = cursor.X, Y = cursor.Y };
            // Нас запустил Проводник по клику — разрешаем хосту вывести окно группы на передний план.
            AllowSetForegroundWindow(AsfwAny);
            // Работающий хост принимает соединение сразу; не дождались — значит, его нет.
            if (Ipc.TrySend(command, 300)) return 0;
        }

        var exe = Path.Combine(AppContext.BaseDirectory, "Futuretray.exe");
        if (!File.Exists(exe)) return 1;
        var start = new ProcessStartInfo(exe) { UseShellExecute = false, WorkingDirectory = AppContext.BaseDirectory };
        foreach (var arg in args) start.ArgumentList.Add(arg);
        Process.Start(start)?.Dispose();
        return 0;
    }

    /// <summary>Только команды значков и списка переходов; остальное (выход, обновление…) — дело самой программы.</summary>
    private static bool Forwardable(HostCommand command, string[] args) => command.Kind switch
    {
        CommandKind.Popup or CommandKind.Launch or CommandKind.Arrange or CommandKind.CloseAll or CommandKind.Focus => true,
        CommandKind.Editor => args.Any(a => a.Equals("--editor", StringComparison.OrdinalIgnoreCase)),
        _ => false,
    };
}
