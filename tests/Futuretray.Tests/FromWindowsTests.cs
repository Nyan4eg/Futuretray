using Futuretray.Core;

namespace Futuretray.Tests;

public class FromWindowsTests
{
    private static string SquirrelApp(params string[] versions)
    {
        var root = TestSetup.TempDir("squirrel");
        File.WriteAllText(Path.Combine(root, "Update.exe"), "");
        foreach (var v in versions)
        {
            Directory.CreateDirectory(Path.Combine(root, "app-" + v));
            File.WriteAllText(Path.Combine(root, "app-" + v, "Discord.exe"), "");
        }
        return root;
    }

    [Fact]
    public void SquirrelApp_IsLaunchedLikeItsOwnShortcut()
    {
        var root = SquirrelApp("1.0.9265");
        var item = AppIndex.SquirrelItem(Path.Combine(root, "app-1.0.9265", "Discord.exe"))!;
        Assert.Equal(Path.Combine(root, "Update.exe"), item.Target);
        Assert.Equal("--processStart \"Discord.exe\"", item.Arguments);

        // Распознаётся как запущенная та же программа — после обновления в app-1.0.9300 тоже.
        var spec = RunningApps.BuildSpec(item)!;
        var window = new AppWindow(IntPtr.Zero, 1, Path.Combine(root, "app-1.0.9300", "Discord.exe"), null, null);
        Assert.True(RunningApps.Matches(window, spec));
    }

    [Fact]
    public void RegularExe_IsNotSquirrel()
    {
        var dir = TestSetup.TempDir("plain");
        File.WriteAllText(Path.Combine(dir, "Tool.exe"), "");
        Assert.Null(AppIndex.SquirrelItem(Path.Combine(dir, "Tool.exe")));
    }

    [Fact]
    public void SquirrelIcon_ComesFromNewestVersion()
    {
        var root = SquirrelApp("1.0.900", "1.0.10000", "1.0.9999");
        var item = new LaunchItem { Kind = ItemKind.Path, Target = Path.Combine(root, "Update.exe"), Arguments = "--processStart \"Discord.exe\"" };
        Assert.Equal(Path.Combine(root, "app-1.0.10000", "Discord.exe"), IconService.SquirrelExe(item));
    }
}
