using System.Text.Json;
using Futuretray.Core;

namespace Futuretray.Tests;

public class WorkspaceTests
{
    private static Native.RECT R(int l, int t, int r, int b) => new() { Left = l, Top = t, Right = r, Bottom = b };

    private static readonly Native.RECT FullHd = R(0, 0, 1920, 1032);

    [Fact]
    public void LeftHalf_IsStoredAsFractions()
    {
        var layout = Workspaces.ToLayout(R(0, 0, 960, 1032), FullHd, null, false);
        Assert.Equal(0, layout.X, 3);
        Assert.Equal(0, layout.Y, 3);
        Assert.Equal(0.5, layout.Width, 3);
        Assert.Equal(1, layout.Height, 3);
    }

    [Fact]
    public void Layout_RoundTripsOnSameMonitor()
    {
        var frame = R(300, 120, 1500, 900);
        var back = Workspaces.ToRect(Workspaces.ToLayout(frame, FullHd, null, false), FullHd);
        Assert.Equal(frame, back);
    }

    [Fact]
    public void Layout_ScalesToMonitorWithOtherResolution()
    {
        // Правая половина на 1080p остаётся правой половиной на 4K со сдвинутым началом координат.
        var layout = Workspaces.ToLayout(R(960, 0, 1920, 1032), FullHd, @"\\.\DISPLAY2", false);
        var uhd = R(1920, 0, 1920 + 3840, 2112);
        var rect = Workspaces.ToRect(layout, uhd);
        Assert.Equal(R(1920 + 1920, 0, 1920 + 3840, 2112), rect);
    }

    [Fact]
    public void WorkAreaOffset_IsRespected()
    {
        // Панель задач слева: рабочая область начинается не с нуля.
        var work = R(48, 0, 1920, 1080);
        var frame = R(48, 0, 984, 1080);
        Assert.Equal(frame, Workspaces.ToRect(Workspaces.ToLayout(frame, work, null, false), work));
    }

    [Fact]
    public void WindowFarOffScreen_IsPulledBackIntoView()
    {
        var layout = new WindowLayout { X = 5, Y = -3, Width = 0.4, Height = 0.4 };
        var rect = Workspaces.ToRect(layout, FullHd);
        Assert.True(rect.Intersects(FullHd));
        Assert.True(rect.Top >= FullHd.Top);
    }

    [Fact]
    public void DegenerateSize_GetsSensibleMinimum()
    {
        var rect = Workspaces.ToRect(new WindowLayout { Width = 0, Height = -1 }, FullHd);
        Assert.True(rect.Width > 0 && rect.Height > 0);
    }

    private const string Home = "1920x1080@0,0*|2560x1440@1920,0";
    private const string Laptop = "1920x1200@0,0*";

    private static MonitorInfo M(int l, int t, int w, int h, bool primary, string device = "") =>
        new(IntPtr.Zero, device, R(l, t, l + w, t + h), R(l, t, l + w, t + h - 48), primary);

    [Fact]
    public void ProfileKey_DescribesMonitorsLeftToRight()
    {
        var key = Workspaces.ProfileKey([M(1920, 0, 2560, 1440, false), M(0, 0, 1920, 1080, true)]);
        Assert.Equal(Home, key);
        Assert.Equal("1920×1080 + 2560×1440", Workspaces.ScreensText(key));
    }

    [Fact]
    public void PickProfile_PrefersCurrentThenSameMonitorCountThenNewest()
    {
        var now = DateTime.Now;
        var profiles = new Dictionary<string, DateTime>
        {
            [Home] = now.AddDays(-3),
            [Laptop] = now.AddDays(-1),
            ["1280x720@0,0*|1280x720@1280,0"] = now.AddDays(-2),
        };
        Assert.Equal(Laptop, Workspaces.PickProfile(profiles, Laptop));
        // Два других монитора: из двухмониторных берём самое свежее.
        Assert.Equal("1280x720@0,0*|1280x720@1280,0", Workspaces.PickProfile(profiles, "1920x1080@0,0*|1920x1080@1920,0"));
        // Три монитора: таких нет — самое свежее вообще.
        Assert.Equal(Laptop, Workspaces.PickProfile(profiles, "1x1@0,0*|1x1@1,0|1x1@2,0"));
        Assert.Null(Workspaces.PickProfile(new Dictionary<string, DateTime>(), Laptop));
    }

    [Fact]
    public void FindMonitor_ByBoundsEvenIfDeviceNameChanged()
    {
        var monitors = new[] { M(0, 0, 1920, 1080, true, @"\\.\DISPLAY1"), M(1920, 0, 2560, 1440, false, @"\\.\DISPLAY7") };
        var layout = new WindowLayout { Monitor = @"\\.\DISPLAY2", Screen = "1920,0,2560,1440" };
        Assert.Same(monitors[1], Workspaces.FindMonitor(layout, monitors));
        Assert.Same(monitors[0], Workspaces.FindMonitor(new WindowLayout { Monitor = @"\\.\DISPLAY9" }, monitors));
    }

    [Fact]
    public void Layouts_SurviveSaveAndClone()
    {
        var item = ItemFactory.FromUrl("https://example.com");
        item.StartDelay = 5;
        item.Layouts = new() { [Home] = new WindowLayout { Monitor = @"\\.\DISPLAY2", Screen = "1920,0,2560,1440", X = 0.5, Width = 0.5, Height = 0.8, Maximized = true } };

        var copy = item.Clone();
        Assert.NotSame(item.Layouts[Home], copy.Layouts![Home]);
        Assert.Equal(5, copy.StartDelay);

        var back = JsonSerializer.Deserialize<LaunchItem>(JsonSerializer.Serialize(item, ConfigStore.JsonOptions), ConfigStore.JsonOptions)!;
        Assert.True(back.Layouts![Home].Maximized);
        Assert.Equal("1920,0,2560,1440", back.Layouts[Home].Screen);
        Assert.Equal(5, back.StartDelay);
    }

    [Fact]
    public void LayoutFromVersion160_MovesToCurrentMonitors()
    {
        var item = JsonSerializer.Deserialize<LaunchItem>("""{"Name":"x","Layout":{"X":0.25,"Width":0.5,"Height":0.5}}""", ConfigStore.JsonOptions)!;
        var g = new Group();
        g.Items.Add(item);
        Assert.True(Workspaces.MigrateLegacy([g], Laptop));
        Assert.Equal(0.25, item.Layouts![Laptop].X);
        Assert.Null(item.LegacyLayout);
        Assert.False(Workspaces.MigrateLegacy([g], Laptop));
        Assert.DoesNotContain("\"Layout\"", JsonSerializer.Serialize(item, ConfigStore.JsonOptions));
    }

    [Fact]
    public void StartDelay_IsClamped()
    {
        var item = new LaunchItem { StartDelay = 999 };
        Assert.Equal(120, item.StartDelay);
        item.StartDelay = -3;
        Assert.Equal(0, item.StartDelay);
    }

    [Fact]
    public void Group_KnowsWhetherItHasLayout_AndForgetsPerMonitorSet()
    {
        var g = new Group();
        g.Items.Add(ItemFactory.FromUrl("https://example.com"));
        Assert.False(g.HasLayout);
        g.Items[0].Layouts = new() { [Home] = new WindowLayout(), [Laptop] = new WindowLayout() };
        Assert.True(g.HasLayout);
        Assert.Equal(2, Workspaces.Profiles(g).Count);

        Workspaces.Forget(g, Laptop);
        Assert.Equal([Home], g.Items[0].Layouts!.Keys);
        Workspaces.Forget(g);
        Assert.False(g.HasLayout);
    }

    [Theory]
    [InlineData("--close", CommandKind.CloseAll)]
    [InlineData("--focus", CommandKind.Focus)]
    public void FinishArguments_AreParsedAndSurvivePipe(string arg, CommandKind kind)
    {
        var c = HostCommand.Parse(new[] { arg, "g1" });
        Assert.Equal(kind, c.Kind);
        Assert.Equal("g1", c.GroupId);
        Assert.Equal(c, HostCommand.Deserialize(c.Serialize()));
    }

    [Fact]
    public void SwitchSettings_SurviveSaveAndDuplicate()
    {
        var g = new Group { OthersOnOpen = OthersOnOpen.CloseGroups, CloseGroupIds = ["rest"] };
        var back = JsonSerializer.Deserialize<Group>(JsonSerializer.Serialize(g, ConfigStore.JsonOptions), ConfigStore.JsonOptions)!;
        Assert.Equal(OthersOnOpen.CloseGroups, back.OthersOnOpen);
        Assert.Equal(["rest"], back.CloseGroupIds!);

        var copy = g.Duplicate("copy");
        Assert.NotSame(g.CloseGroupIds, copy.CloseGroupIds);
        Assert.Equal(OthersOnOpen.CloseGroups, copy.OthersOnOpen);

        g.CloseGroupIds = [];
        Assert.Null(g.CloseGroupIds);
    }

    [Fact]
    public void StartupArgument_IsItsOwnCommand() =>
        Assert.Equal(CommandKind.Startup, HostCommand.Parse(new[] { "--startup" }).Kind);

    [Fact]
    public void ArrangeArgument_IsParsedAndSurvivesPipe()
    {
        var c = HostCommand.Parse(new[] { "--arrange", "g1" });
        Assert.Equal(CommandKind.Arrange, c.Kind);
        Assert.Equal("g1", c.GroupId);
        Assert.Equal(c, HostCommand.Deserialize(c.Serialize()));
    }
}

public class WindowPlacementTests
{
    private static Native.RECT R(int l, int t, int r, int b) => new() { Left = l, Top = t, Right = r, Bottom = b };

    [Fact]
    public void BottomTaskbar_NoShift() =>
        Assert.Equal(R(100, 50, 800, 550), Native.WorkspaceToScreen(R(100, 50, 800, 550), R(0, 0, 1920, 1080), R(0, 0, 1920, 1032)));

    [Fact]
    public void LeftTaskbar_ShiftsRight() =>
        Assert.Equal(R(148, 50, 848, 550), Native.WorkspaceToScreen(R(100, 50, 800, 550), R(0, 0, 1920, 1080), R(48, 0, 1920, 1080)));

    [Fact]
    public void TopTaskbarOnSecondMonitor_ShiftsDown() =>
        Assert.Equal(R(2020, 98, 2720, 598), Native.WorkspaceToScreen(R(2020, 50, 2720, 550), R(1920, 0, 3840, 1080), R(1920, 48, 3840, 1080)));
}
