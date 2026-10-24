using System.Windows.Input;
using Futuretray.Core;
using Futuretray.UI;

namespace Futuretray.Tests;

public class HostCommandTests
{
    [Fact]
    public void GroupShortcutArguments_OpenPopup()
    {
        var c = HostCommand.Parse(new[] { "--group", "abc123" });
        Assert.Equal(CommandKind.Popup, c.Kind);
        Assert.Equal("abc123", c.GroupId);
    }

    [Theory]
    [InlineData(new string[0], CommandKind.Editor)]
    [InlineData(new[] { "--background" }, CommandKind.Background)]
    [InlineData(new[] { "--exit" }, CommandKind.Exit)]
    [InlineData(new[] { "--updated" }, CommandKind.Updated)]
    [InlineData(new[] { "--uninstall" }, CommandKind.Uninstall)]
    [InlineData(new[] { "--whatever" }, CommandKind.Editor)]
    public void Arguments_MapToCommands(string[] args, CommandKind kind) =>
        Assert.Equal(kind, HostCommand.Parse(args).Kind);

    [Fact]
    public void PipeMessage_RoundTrips()
    {
        var c = new HostCommand(CommandKind.Popup, "g1", -1200, 1050);
        Assert.Equal(c, HostCommand.Deserialize(c.Serialize()));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("garbage")]
    [InlineData("Nope\tg\t1\t2")]
    public void BrokenPipeMessage_IsIgnored(string? line) => Assert.Null(HostCommand.Deserialize(line));
}

public class HotkeyTests
{
    [Fact]
    public void Parse_ReadsModifiersAndKey()
    {
        Assert.True(Hotkey.TryParse("Ctrl+Alt+Space", out var h));
        Assert.Equal(ModifierKeys.Control | ModifierKeys.Alt, h.Modifiers);
        Assert.Equal(Key.Space, h.Key);
    }

    [Theory]
    [InlineData("Win+Alt+Space")]
    [InlineData("Ctrl+Shift+F5")]
    [InlineData("Alt+D1")]
    [InlineData("F12")]
    public void ToString_RoundTrips(string text)
    {
        Assert.True(Hotkey.TryParse(text, out var h));
        Assert.True(Hotkey.TryParse(h.ToString(), out var again));
        Assert.Equal(h, again);
    }

    [Fact]
    public void Parse_IsCaseInsensitive() =>
        Assert.True(Hotkey.TryParse("ctrl + shift + q", out var h) && h.Key == Key.Q && h.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Ctrl+")]
    [InlineData("Ctrl+Alt")]
    [InlineData("Ctrl+Banana")]
    public void Parse_RejectsGarbage(string? text) => Assert.False(Hotkey.TryParse(text, out _));

    [Fact]
    public void Display_ShowsDigitsAsDigits()
    {
        Hotkey.TryParse("Ctrl+Alt+D1", out var h);
        Assert.Equal("Ctrl + Alt + 1", h.Display);
    }
}

public class SearchScoreTests
{
    [Theory]
    [InlineData("vis", 100)]
    [InlineData("studio", 80)]
    [InlineData("dio", 60)]
    [InlineData("vsc", 50)]
    [InlineData("dev", 20)]
    [InlineData("zzz", 0)]
    [InlineData("", 1)]
    public void Score_RanksKindsOfMatches(string query, int expected) =>
        Assert.Equal(expected, SearchWindow.Score("Visual Studio Code", "Dev tools", query));

    [Fact]
    public void Score_IgnoresCaseAndWorksWithCyrillic()
    {
        Assert.Equal(100, SearchWindow.Score("Блокнот", "Работа", "БЛОК"));
        Assert.Equal(80, SearchWindow.Score("Диспетчер задач", "Работа", "зад"));
    }
}

public class SmallHelpersTests
{
    [Theory]
    [InlineData(1, "приложение")]
    [InlineData(2, "приложения")]
    [InlineData(4, "приложения")]
    [InlineData(5, "приложений")]
    [InlineData(11, "приложений")]
    [InlineData(14, "приложений")]
    [InlineData(21, "приложение")]
    [InlineData(112, "приложений")]
    public void RussianPlural(int n, string expected) =>
        Assert.Equal(expected, AppsCountConverter.Plural(n, "приложение", "приложения", "приложений"));

    [Theory]
    [InlineData("#6D5DFC", true)]
    [InlineData("6d5dfc", true)]
    [InlineData("#FF6D5DFC", true)]
    [InlineData("#12", false)]
    [InlineData("красный", false)]
    [InlineData(null, false)]
    public void ColorParsing(string? text, bool ok) => Assert.Equal(ok, ColorUtil.TryParse(text, out _));

    [Fact]
    public void Url_WithoutScheme_GetsHttps_AndNiceName()
    {
        var item = ItemFactory.FromUrl("www.github.com");
        Assert.Equal(ItemKind.Url, item.Kind);
        Assert.Equal("https://www.github.com", item.Target);
        Assert.Equal("Github.com", item.Name);
    }

    [Theory]
    [InlineData("steam://rungameid/1")]
    [InlineData("tg://resolve?domain=x")]
    [InlineData("https://example.com")]
    public void CustomSchemes_AreKept(string url)
    {
        Assert.True(ItemFactory.LooksLikeUrl(url));
        Assert.Equal(url, ItemFactory.FromUrl(url).Target);
    }
}
