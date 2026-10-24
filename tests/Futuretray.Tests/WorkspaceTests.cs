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

    [Fact]
    public void Layout_SurvivesSaveAndClone()
    {
        var item = ItemFactory.FromUrl("https://example.com");
        item.Layout = new WindowLayout { Monitor = @"\\.\DISPLAY2", X = 0.5, Y = 0.1, Width = 0.5, Height = 0.8, Maximized = true };

        var copy = item.Clone();
        Assert.NotSame(item.Layout, copy.Layout);
        Assert.Equal(item.Layout.X, copy.Layout!.X);

        var json = JsonSerializer.Serialize(item.Layout);
        var back = JsonSerializer.Deserialize<WindowLayout>(json)!;
        Assert.Equal(@"\\.\DISPLAY2", back.Monitor);
        Assert.True(back.Maximized);
    }

    [Fact]
    public void Group_KnowsWhetherItHasLayout()
    {
        var g = new Group();
        g.Items.Add(ItemFactory.FromUrl("https://example.com"));
        Assert.False(g.HasLayout);
        g.Items[0].Layout = new WindowLayout { Width = 1, Height = 1 };
        Assert.True(g.HasLayout);
        Workspaces.Forget(g);
        Assert.False(g.HasLayout);
    }

    [Fact]
    public void ArrangeArgument_IsParsedAndSurvivesPipe()
    {
        var c = HostCommand.Parse(new[] { "--arrange", "g1" });
        Assert.Equal(CommandKind.Arrange, c.Kind);
        Assert.Equal("g1", c.GroupId);
        Assert.Equal(c, HostCommand.Deserialize(c.Serialize()));
    }
}
