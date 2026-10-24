using Futuretray.Core;

namespace Futuretray.Tests;

/// <summary>Узнаём ли мы окно программы по элементу группы — во всех хитрых случаях Windows.</summary>
public class RunningAppsMatchTests
{
    private static AppWindow Window(string? path, string? appId = null, string? processAppId = null) =>
        new(new IntPtr(1), 1, path, appId, processAppId);

    private static bool Matches(LaunchItem item, AppWindow window) =>
        RunningApps.BuildSpec(item) is { } spec && RunningApps.Matches(window, spec);

    private static LaunchItem Exe(string path, string? args = null) =>
        new() { Kind = ItemKind.Path, Target = path, Arguments = args };

    [Fact]
    public void PlainExe_MatchesSamePath_IgnoringCase()
    {
        var item = Exe(@"C:\Apps\Tool\tool.exe");
        Assert.True(Matches(item, Window(@"c:\apps\tool\TOOL.EXE")));
        Assert.False(Matches(item, Window(@"C:\Apps\Other\other.exe")));
    }

    [Fact]
    public void NewSquirrel_StubInRoot_MatchesAppSubfolder()
    {
        // Discord/Element: ярлык ведёт на ...\Discord\Discord.exe, работает ...\Discord\app-1.0.9262\Discord.exe
        var item = Exe(@"C:\Users\u\AppData\Local\Discord\Discord.exe");
        Assert.True(Matches(item, Window(@"C:\Users\u\AppData\Local\Discord\app-1.0.9262\Discord.exe")));
        Assert.False(Matches(item, Window(@"D:\Portable\Discord.exe")));
    }

    [Fact]
    public void OldSquirrel_UpdateExeProcessStart_MatchesRealExe()
    {
        var item = Exe(@"C:\Users\u\AppData\Local\slack\Update.exe", "--processStart slack.exe");
        Assert.True(Matches(item, Window(@"C:\Users\u\AppData\Local\slack\app-4.41.0\slack.exe")));
        Assert.False(Matches(item, Window(@"C:\Users\u\AppData\Local\slack\Update.exe2")));
    }

    [Fact]
    public void Windows11Stub_NotepadInSystem32_MatchesStoreNotepad()
    {
        var item = Exe(Path.Combine(Environment.SystemDirectory, "notepad.exe"));
        Assert.True(Matches(item, Window(@"C:\Program Files\WindowsApps\Microsoft.WindowsNotepad_11.2607.14.0_x64__8wekyb3d8bbwe\Notepad\Notepad.exe")));
        Assert.False(Matches(item, Window(@"D:\Tools\notepad.exe")));
    }

    [Fact]
    public void StoreApp_MatchesWindowOrProcessAppUserModelId()
    {
        var item = new LaunchItem { Kind = ItemKind.ShellApp, Target = "Claude_pzs8sxrjxfjjc!Claude" };
        Assert.True(Matches(item, Window(@"C:\Program Files\WindowsApps\Claude_1\app\claude.exe", processAppId: "Claude_pzs8sxrjxfjjc!Claude")));
        Assert.True(Matches(item, Window(@"C:\Windows\System32\ApplicationFrameHost.exe", appId: "claude_pzs8sxrjxfjjc!claude")));
        Assert.False(Matches(item, Window(@"C:\Program Files\WindowsApps\Other\other.exe", processAppId: "Other!App")));
    }

    [Fact]
    public void Steam_MatchesSteamWebHelperWindow()
    {
        var item = Exe(@"C:\Program Files (x86)\Steam\steam.exe");
        Assert.True(Matches(item, Window(@"C:\Program Files (x86)\Steam\bin\cef\cef.win64\steamwebhelper.exe", appId: "Valve.Steam.Client")));
    }

    [Fact]
    public void FileExplorerShortcut_MatchesExplorerWindows()
    {
        var item = new LaunchItem { Kind = ItemKind.ShellApp, Target = "Microsoft.Windows.Explorer" };
        var explorer = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");
        Assert.True(Matches(item, Window(explorer)));
    }

    [Fact]
    public void Shortcut_IsResolvedToItsTargetAndAppId()
    {
        var dir = TestSetup.TempDir("lnk");
        var lnk = Path.Combine(dir, "Tool.lnk");
        ShellLink.Save(lnk, @"C:\Apps\Tool\tool.exe", "", null, @"C:\Apps\Tool\tool.exe", null, "Vendor.Tool");

        var item = ItemFactory.FromPath(lnk);
        Assert.True(Matches(item, Window(@"C:\Apps\Tool\tool.exe")));
        Assert.True(Matches(item, Window(@"C:\Elsewhere\host.exe", appId: "Vendor.Tool")));
        Assert.False(Matches(item, Window(@"C:\Elsewhere\host.exe")));
    }

    [Theory]
    [InlineData(ItemKind.Url, "https://github.com")]
    [InlineData(ItemKind.Path, @"C:\Docs\report.pdf")]
    public void LinksAndDocuments_AreNeverRunning(ItemKind kind, string target) =>
        Assert.Null(RunningApps.BuildSpec(new LaunchItem { Kind = kind, Target = target }));
}
