using Futuretray.Core;
using Futuretray.UI;

namespace Futuretray.Tests;

public class LayoutTests
{
    [Theory]
    [InlineData(1, 1)]
    [InlineData(3, 3)]
    [InlineData(6, 6)]
    [InlineData(7, 4)]
    [InlineData(12, 5)]
    public void HorizontalTaskbar_LaysTilesInRows(int items, int columns) =>
        Assert.Equal(columns, GroupTilesView.AutoColumns(items, alongVerticalTaskbar: false));

    [Theory]
    [InlineData(1, 1)]
    [InlineData(3, 1)]
    [InlineData(6, 1)]
    [InlineData(7, 2)]
    [InlineData(12, 3)]
    public void VerticalTaskbar_LaysTilesInColumnAlongIt(int items, int columns) =>
        Assert.Equal(columns, GroupTilesView.AutoColumns(items, alongVerticalTaskbar: true));

    [Fact]
    public void ExplicitColumns_AreRespectedEvenNextToVerticalTaskbar()
    {
        var g = new Group { Columns = 4 };
        for (int i = 0; i < 6; i++) g.Items.Add(ItemFactory.FromUrl($"https://example{i}.com"));
        Assert.Equal(4, GroupTilesView.ColumnsFor(g, alongVerticalTaskbar: true));
    }

    [Theory]
    [InlineData(TaskbarEdge.Bottom, DockEdge.Bottom)]
    [InlineData(TaskbarEdge.Left, DockEdge.Left)]
    [InlineData(TaskbarEdge.Right, DockEdge.Right)]
    [InlineData(TaskbarEdge.Top, DockEdge.Top)]
    [InlineData(TaskbarEdge.None, DockEdge.Bottom)]
    public void FirstEnable_PutsTrayNextToTaskbar(TaskbarEdge taskbar, DockEdge expected)
    {
        var g = new Group();
        g.PlaceDockNextToTaskbar(taskbar);
        Assert.Equal(expected, g.DockEdge);
        Assert.True(g.DockPlaced);
    }

    [Fact]
    public void FirstEnable_KeepsUsersOwnChoice()
    {
        var chosen = new Group { DockEdge = DockEdge.Right };
        chosen.PlaceDockNextToTaskbar(TaskbarEdge.Bottom);
        Assert.Equal(DockEdge.Right, chosen.DockEdge);

        var placedBefore = new Group { DockPlaced = true };
        placedBefore.PlaceDockNextToTaskbar(TaskbarEdge.Bottom);
        Assert.Equal(DockEdge.Left, placedBefore.DockEdge); // второй раз уже не трогаем
    }
}
