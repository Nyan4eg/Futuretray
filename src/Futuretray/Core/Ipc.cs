using System.Diagnostics;
using System.IO.Pipes;
using System.Text;

namespace Futuretray.Core;

public enum CommandKind { Editor, Popup, Background, Exit, Updated, Uninstall, Launch, Arrange, Startup, CloseAll, Focus }

public sealed record HostCommand(CommandKind Kind, string? GroupId = null, int X = 0, int Y = 0, string? ItemId = null)
{
    public static HostCommand Parse(string[] args)
    {
        for (int i = 0; i < args.Length; i++)
        {
            var a = args[i].Trim().ToLowerInvariant();
            switch (a)
            {
                case "--group" or "-g" or "/group" when i + 1 < args.Length:
                    return new HostCommand(CommandKind.Popup, args[i + 1].Trim());
                case "--startup":
                    // Автозапуск при входе в Windows: фоновый режим плюс группы «Открывать при входе».
                    return new HostCommand(CommandKind.Startup);
                case "--background" or "/background":
                    return new HostCommand(CommandKind.Background);
                case "--exit" or "/exit":
                    return new HostCommand(CommandKind.Exit);
                case "--updated":
                    return new HostCommand(CommandKind.Updated);
                case "--uninstall":
                    return new HostCommand(CommandKind.Uninstall);
                case "--launch" when i + 2 < args.Length:
                    // Пункт списка переходов: запустить элемент группы, не открывая окно группы.
                    return new HostCommand(CommandKind.Launch, args[i + 1].Trim(), ItemId: args[i + 2].Trim());
                case "--arrange" when i + 1 < args.Length:
                    return new HostCommand(CommandKind.Arrange, args[i + 1].Trim());
                case "--close" when i + 1 < args.Length:
                    return new HostCommand(CommandKind.CloseAll, args[i + 1].Trim());
                case "--focus" when i + 1 < args.Length:
                    return new HostCommand(CommandKind.Focus, args[i + 1].Trim());
                case "--editor" or "/editor":
                    return new HostCommand(CommandKind.Editor, i + 1 < args.Length ? args[i + 1].Trim() : null);
            }
        }
        return new HostCommand(CommandKind.Editor);
    }

    public string Serialize() => $"{Kind}\t{GroupId}\t{X}\t{Y}\t{ItemId}";

    public static HostCommand? Deserialize(string? line)
    {
        if (string.IsNullOrWhiteSpace(line)) return null;
        var parts = line.Split('\t');
        if (parts.Length < 4 || !Enum.TryParse<CommandKind>(parts[0], out var kind)) return null;
        int.TryParse(parts[2], out var x);
        int.TryParse(parts[3], out var y);
        var itemId = parts.Length > 4 && parts[4].Length > 0 ? parts[4] : null;
        return new HostCommand(kind, string.IsNullOrEmpty(parts[1]) ? null : parts[1], x, y, itemId);
    }
}

/// <summary>Связь «запускатель → фоновый хост» через именованный канал текущего пользователя.</summary>
public static class Ipc
{
    private static readonly string Suffix = $"{Environment.UserName}.{Process.GetCurrentProcess().SessionId}";
    public static string PipeName => "Futuretray.Pipe." + Suffix;
    public static string MutexName => @"Local\Futuretray.Host." + Suffix;

    public static bool TrySend(HostCommand command, int timeoutMs)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut, PipeOptions.CurrentUserOnly);
            client.Connect(timeoutMs);
            using var writer = new StreamWriter(client, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };
            using var reader = new StreamReader(client, Encoding.UTF8, leaveOpen: true);
            writer.WriteLine(command.Serialize());
            return reader.ReadLine() == "ok";
        }
        catch
        {
            return false;
        }
    }
}

public sealed class IpcServer
{
    public event Action<HostCommand>? CommandReceived;

    public void Start()
    {
        var thread = new Thread(Loop) { IsBackground = true, Name = "Futuretray IPC" };
        thread.Start();
    }

    private void Loop()
    {
        while (true)
        {
            try
            {
                using var server = new NamedPipeServerStream(Ipc.PipeName, PipeDirection.InOut,
                    NamedPipeServerStream.MaxAllowedServerInstances, PipeTransmissionMode.Byte, PipeOptions.CurrentUserOnly);
                server.WaitForConnection();
                using var reader = new StreamReader(server, Encoding.UTF8, leaveOpen: true);
                using var writer = new StreamWriter(server, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };
                var command = HostCommand.Deserialize(reader.ReadLine());
                writer.WriteLine(command != null ? "ok" : "bad");
                if (command != null) CommandReceived?.Invoke(command);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "IPC");
                Thread.Sleep(200);
            }
        }
    }
}
