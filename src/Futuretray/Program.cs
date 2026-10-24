using System.Runtime.CompilerServices;
using Futuretray.Core;

namespace Futuretray;

/// <summary>
/// Точка входа. Важно: этот файл не должен трогать WPF-типы на «быстром пути».
/// Когда фоновый хост уже запущен, процесс только передаёт ему команду по
/// именованному каналу и сразу завершается — так группа открывается мгновенно.
/// </summary>
internal static class Program
{
    public const string AppUserModelId = "Futuretray.App";

    [STAThread]
    private static int Main(string[] args)
    {
#if DEBUG
        int snap = Array.IndexOf(args, "--snapshot");
        if (snap >= 0 && snap + 1 < args.Length)
        {
            DebugSnapshot.Directory = args[snap + 1];
            return RunHost(new HostCommand(CommandKind.Background));
        }
#endif
        // Перезапуск: ждём, пока старая копия отпустит «замок» хоста, и становимся хостом сами.
        if (args.Contains("--restart", StringComparer.OrdinalIgnoreCase)) WaitForHostExit();

        var command = HostCommand.Parse(args);
        if (command.Kind == CommandKind.Popup && Native.GetCursorPos(out var cursor))
            command = command with { X = cursor.X, Y = cursor.Y };

        bool isHost;
        using (var hostMutex = new Mutex(true, Ipc.MutexName, out isHost))
        {
            if (!isHost)
            {
                // Разрешаем хосту вывести окно на передний план: нас запустил Проводник
                // по клику пользователя, поэтому у нас это право есть.
                Native.AllowSetForegroundWindow(Native.ASFW_ANY);
                bool sent = Ipc.TrySend(command.Kind == CommandKind.Uninstall ? new HostCommand(CommandKind.Exit) : command, 5000);
                if (command.Kind is CommandKind.Exit or CommandKind.Uninstall)
                {
                    hostMutex.Dispose();
                    WaitForHostExit();
                    if (command.Kind == CommandKind.Uninstall) Uninstaller.Cleanup();
                }
                return sent ? 0 : 1;
            }

            // Нечего закрывать — не поднимаем хост ради этого.
            if (command.Kind == CommandKind.Exit) return 0;
            if (command.Kind == CommandKind.Uninstall)
            {
                Uninstaller.Cleanup();
                return 0;
            }

            return RunHost(command);
        }
    }

    /// <summary>Ждём, пока хост действительно завершится и отпустит файлы (нужно установщику).</summary>
    private static void WaitForHostExit()
    {
        for (int i = 0; i < 60; i++)
        {
            using var probe = new Mutex(false, Ipc.MutexName, out bool createdNew);
            if (createdNew) return;
            Thread.Sleep(100);
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int RunHost(HostCommand command)
    {
        // Без этого окна хоста «прилипали» бы к ярлыку группы на панели задач,
        // если первый запуск был именно с него.
        try { Native.SetCurrentProcessExplicitAppUserModelID(AppUserModelId); } catch { }

        var app = new App { Initial = command };
        app.InitializeComponent();
        return app.Run();
    }
}
