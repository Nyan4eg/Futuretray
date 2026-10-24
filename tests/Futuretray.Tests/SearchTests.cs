using Futuretray.Core;
using Futuretray.UI;

namespace Futuretray.Tests;

public class SearchTests
{
    [Theory]
    [InlineData("Uninstall Foo", null)]
    [InlineData("Удалить Foo", @"C:\Foo\unins000.exe")]
    [InlineData("Foo Readme", @"C:\Foo\readme.txt")]
    [InlineData("Foo manual", @"C:\Foo\manual.chm")]
    [InlineData("Foo website", @"C:\Foo\site.html")]
    public void JunkStartMenuEntries_AreHidden(string name, string? path) =>
        Assert.True(AppIndex.IsJunk(name, path));

    [Theory]
    [InlineData("Telegram", @"C:\Users\me\AppData\Roaming\Telegram Desktop\Telegram.exe")]
    [InlineData("Калькулятор", null)]
    [InlineData("Управление компьютером", @"C:\Windows\System32\compmgmt.msc")]
    public void RealApps_AreKept(string name, string? path) =>
        Assert.False(AppIndex.IsJunk(name, path));

    [Fact]
    public void Window_IsFoundByTitleOrByItsApp()
    {
        // Заголовок окна Telegram — имя чата, но «тел» всё равно находит его по программе.
        Assert.True(SearchWindow.WindowScore("Мама", "Telegram", "tel") > 0);
        Assert.Equal(100, SearchWindow.WindowScore("Отчёт.docx — Word", "Microsoft Word", "отч"));
        Assert.True(SearchWindow.WindowScore("Отчёт", "Microsoft Word", "word") < SearchWindow.WindowScore("Word", "", "word"));
        Assert.Equal(0, SearchWindow.WindowScore("Мама", "Telegram", "word"));
    }

    [Fact]
    public void SingleWordName_IsNotMatchedByInitials()
    {
        // «t» — первая буква и так; инициалы имеют смысл только для нескольких слов.
        Assert.Equal(0, SearchWindow.Score("Notepad", "", "np"));
        Assert.Equal(50, SearchWindow.Score("Visual Studio Code", "", "vsc"));
    }
}
