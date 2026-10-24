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
        var command = HostCommand.Parse(args);
        if (command.Kind == CommandKind.Popup && Native.GetCursorPos(out var cursor))
            command = command with { X = cursor.X, Y = cursor.Y };

        using var hostMutex = new Mutex(true, Ipc.MutexName, out bool isHost);
        if (!isHost)
        {
            // Разрешаем хосту вывести окно на передний план: нас запустил Проводник
            // по клику пользователя, поэтому у нас это право есть.
            Native.AllowSetForegroundWindow(Native.ASFW_ANY);
            return Ipc.TrySend(command, 5000) ? 0 : 1;
        }

        return RunHost(command);
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
