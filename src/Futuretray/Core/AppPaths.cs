namespace Futuretray.Core;

/// <summary>
/// Где Futuretray хранит свои файлы. Обычно — в профиле пользователя, а в портативном режиме
/// (рядом с exe лежит файл «portable» или «portable.txt») — в папке Data рядом с программой.
/// </summary>
public static class AppPaths
{
    public static string ExePath { get; } = Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "Futuretray.exe");

    public static string ExeDir => Path.GetDirectoryName(ExePath) ?? AppContext.BaseDirectory;

    /// <summary>Портативный режим: настройки, значки и журнал — в папке Data рядом с exe.</summary>
    public static bool Portable { get; } = Environment.GetEnvironmentVariable("FUTURETRAY_DATA") is not { Length: > 0 } &&
        (File.Exists(Path.Combine(ExeDir, "portable")) || File.Exists(Path.Combine(ExeDir, "portable.txt")));

    private static string PortableDir => Path.Combine(ExeDir, "Data");

    /// <summary>Настройки, ярлыки групп, значки (обычно %AppData%\Futuretray).</summary>
    public static string Roaming { get; } = Environment.GetEnvironmentVariable("FUTURETRAY_DATA") is { Length: > 0 } custom
        ? custom
        : Portable ? PortableDir : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Futuretray");

    /// <summary>Кэши и журнал (обычно %LocalAppData%\Futuretray).</summary>
    public static string Local { get; } = Portable
        ? PortableDir
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Futuretray");
}
