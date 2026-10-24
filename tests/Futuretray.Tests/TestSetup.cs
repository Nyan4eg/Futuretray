using System.Runtime.CompilerServices;
using Futuretray.Core;

namespace Futuretray.Tests;

internal static class TestSetup
{
    /// <summary>Тесты никогда не трогают настоящие данные пользователя в %APPDATA%.</summary>
    [ModuleInitializer]
    internal static void Init()
    {
        ConfigStore.DataDir = TempDir("data");
        // Тесты нарочно подсовывают битые файлы — их ошибки не должны попадать в журнал пользователя.
        Log.Directory = TempDir("logs");
    }

    public static string TempDir(string name)
    {
        var dir = Path.Combine(Path.GetTempPath(), "futuretray-tests", name + "-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        return dir;
    }
}
