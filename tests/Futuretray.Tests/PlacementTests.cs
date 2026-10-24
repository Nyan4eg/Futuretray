using Futuretray.Core;

namespace Futuretray.Tests;

/// <summary>Где окно группы появляется относительно панели задач (главная боль оригинального taskbar-groups).</summary>
public class PlacementTests
{
    private const int W = 300, H = 200, Gap = 12;

    private static Native.RECT Rect(int l, int t, int r, int b) => new() { Left = l, Top = t, Right = r, Bottom = b };

    private static Native.POINT Place(Native.RECT available, TaskbarEdge edge, bool fromTaskbar, int x, int y) =>
        new Placement(IntPtr.Zero, available, edge, fromTaskbar).Place(W, H, new Native.POINT(x, y), Gap);

    [Fact]
    public void BottomTaskbar_CentersOverCursor_AboveTaskbar()
    {
        var p = Place(Rect(0, 0, 1920, 1032), TaskbarEdge.Bottom, true, 500, 1050);
        Assert.Equal(500 - W / 2, p.X);
        Assert.Equal(1032 - Gap - H, p.Y);
    }

    [Fact]
    public void LeftTaskbar_OpensNextToTaskbar_CenteredOnCursor()
    {
        var p = Place(Rect(48, 0, 1920, 1080), TaskbarEdge.Left, true, 20, 500);
        Assert.Equal(48 + Gap, p.X);
        Assert.Equal(500 - H / 2, p.Y);
    }

    [Fact]
    public void RightTaskbar_OpensToTheLeftOfTaskbar()
    {
        var p = Place(Rect(0, 0, 1872, 1080), TaskbarEdge.Right, true, 1900, 500);
        Assert.Equal(1872 - Gap - W, p.X);
        Assert.Equal(400, p.Y);
    }

    [Fact]
    public void TopTaskbar_OpensBelowTaskbar()
    {
        var p = Place(Rect(0, 48, 1920, 1080), TaskbarEdge.Top, true, 960, 20);
        Assert.Equal(48 + Gap, p.Y);
        Assert.Equal(960 - W / 2, p.X);
    }

    [Fact]
    public void NearScreenCorner_StaysInsideWorkArea()
    {
        var p = Place(Rect(0, 0, 1920, 1032), TaskbarEdge.Bottom, true, 5, 1050);
        Assert.Equal(Gap, p.X);

        var q = Place(Rect(48, 0, 1920, 1080), TaskbarEdge.Left, true, 20, 1075);
        Assert.Equal(1080 - Gap - H, q.Y);
    }

    [Fact]
    public void NotFromTaskbar_OpensAboveCursor()
    {
        var p = Place(Rect(0, 0, 1920, 1032), TaskbarEdge.Bottom, false, 960, 540);
        Assert.Equal(540 - H - Gap, p.Y);
        Assert.Equal(960 - W / 2, p.X);
    }

    [Fact]
    public void NotFromTaskbar_NearTop_OpensBelowCursor()
    {
        var p = Place(Rect(0, 0, 1920, 1032), TaskbarEdge.Bottom, false, 960, 100);
        Assert.Equal(100 + Gap, p.Y);
    }

    [Fact]
    public void SecondMonitor_UsesItsOwnCoordinates()
    {
        var p = Place(Rect(1920, 0, 3840, 1032), TaskbarEdge.Bottom, true, 1925, 1050);
        Assert.Equal(1920 + Gap, p.X);
        Assert.Equal(1032 - Gap - H, p.Y);
    }

    [Fact]
    public void PopupLargerThanScreen_DoesNotThrow_AndPinsToTopLeft()
    {
        var p = new Placement(IntPtr.Zero, Rect(0, 0, 200, 100), TaskbarEdge.Bottom, true)
            .Place(500, 400, new Native.POINT(100, 90), Gap);
        Assert.Equal(Gap, p.X);
        Assert.Equal(Gap, p.Y);
    }
}
