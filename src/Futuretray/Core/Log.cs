namespace Futuretray.Core;

public static class Log
{
    private static readonly object Gate = new();

    public static string Directory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Futuretray", "logs");

    public static string FilePath => Path.Combine(Directory, "futuretray.log");

    public static void Error(Exception? ex, string context) => Write("ERROR", $"{context}: {ex}");

    public static void Info(string message) => Write("INFO", message);

    private static void Write(string level, string message)
    {
        try
        {
            lock (Gate)
            {
                System.IO.Directory.CreateDirectory(Directory);
                var file = new FileInfo(FilePath);
                if (file.Exists && file.Length > 1_000_000) file.Delete();
                File.AppendAllText(FilePath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {level} {message}{Environment.NewLine}");
            }
        }
        catch
        {
            // Логирование никогда не должно ронять приложение.
        }
    }
}
