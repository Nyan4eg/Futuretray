using System.Reflection;
using Futuretray.Core;

namespace Futuretray.Tests;

/// <summary>Синхронизация меняет общий путь к config.json — эти тесты не идут параллельно с другими тестами настроек.</summary>
[Collection("Config")]
public class SyncTests
{
    [Fact]
    public void Sync_MovesGroupsAndPicturesToFolder_AndBack()
    {
        var store = new ConfigStore();
        store.Load();
        var group = new Group { Name = "Sync me" };
        Directory.CreateDirectory(ConfigStore.UserIconsDir);
        var picture = Path.Combine(ConfigStore.UserIconsDir, "pic.png");
        File.WriteAllBytes(picture, [1, 2, 3]);
        group.CustomIconPath = picture;
        store.Config.Groups.Add(group);
        store.SaveNow();

        var folder = TestSetup.TempDir("sync");
        try
        {
            Assert.False(store.EnableSync(folder, useExisting: false));
            Assert.Equal(folder, ConfigStore.SyncFolder);
            Assert.True(File.Exists(Path.Combine(folder, "config.json")));
            Assert.Equal(Path.Combine(folder, "UserIcons", "pic.png"), group.CustomIconPath);
            Assert.True(File.Exists(group.CustomIconPath));

            // Другой экземпляр (как после перезапуска) читает группы из папки.
            var again = new ConfigStore();
            again.Load();
            Assert.Contains(again.Config.Groups, g => g.Name == "Sync me");

            Assert.True(ConfigStore.FolderHasConfig(folder));
            Assert.True(store.EnableSync(folder, useExisting: true)); // «взять из папки» — нужен перезапуск
        }
        finally
        {
            store.DisableSync();
        }
        Assert.Null(ConfigStore.SyncFolder);
        Assert.StartsWith(ConfigStore.DataDir, ConfigStore.ConfigPath);
        var local = new ConfigStore();
        local.Load();
        Assert.Contains(local.Config.Groups, g => g.Name == "Sync me");
        local.Config.Groups.Remove(local.Config.Groups.First(g => g.Name == "Sync me"));
        local.SaveNow();
    }

    [Fact]
    public void PicturePathFromAnotherComputer_IsFoundNextToConfig()
    {
        Directory.CreateDirectory(ConfigStore.UserIconsDir);
        var name = Guid.NewGuid().ToString("N") + ".png";
        File.WriteAllBytes(Path.Combine(ConfigStore.UserIconsDir, name), [1]);
        var config = new AppConfig();
        var group = new Group { CustomIconPath = $@"C:\Users\someone-else\OneDrive\Futuretray\UserIcons\{name}" };
        group.Items.Add(new LaunchItem { Target = "x", CustomIconPath = @"C:\nowhere\missing.png" });
        config.Groups.Add(group);

        ConfigStore.RemapUserIcons(config);

        Assert.Equal(Path.Combine(ConfigStore.UserIconsDir, name), group.CustomIconPath);
        Assert.Equal(@"C:\nowhere\missing.png", group.Items[0].CustomIconPath); // нет такой картинки — путь не трогаем
    }
}

public class UpdateAssetTests
{
    [Theory]
    [InlineData("FuturetraySetup-1.10.0.exe", false, true)]
    [InlineData("Futuretray.zip", false, false)]
    [InlineData("Futuretray.zip", true, true)]
    [InlineData("FuturetraySetup-1.10.0.exe", true, false)]
    public void InstalledCopyTakesInstaller_PortableTakesZip(string asset, bool portable, bool expected) =>
        Assert.Equal(expected, Updater.IsOurAsset(asset, portable));
}

public class WhatsNewTests
{
    private const string Sample = """
        # Изменения

        ## 1.3.0

        - **Жирно**: первая строка
          продолжение
        - второй пункт

        ## 1.2.0

        - старое
        """;

    [Fact]
    public void Changelog_IsParsedIntoVersionsAndBullets()
    {
        var sections = WhatsNew.Parse(Sample.Replace("\n        ", "\n"));
        Assert.Equal([new Version(1, 3, 0), new Version(1, 2, 0)], sections.Select(s => s.Version));
        Assert.Equal(["**Жирно**: первая строка продолжение", "второй пункт"], sections[0].Bullets);
    }

    [Fact]
    public void Pending_ShowsOnlyUnseenVersions()
    {
        var sections = WhatsNew.Parse(Sample.Replace("\n        ", "\n"));
        var current = new Version(1, 3, 0);
        Assert.Equal([new Version(1, 3, 0)], WhatsNew.Pending("1.2.0", false, current, sections).Select(s => s.Version));
        Assert.Empty(WhatsNew.Pending("1.3.0", false, current, sections));
        Assert.Equal(2, WhatsNew.Pending("1.0.0", false, current, sections).Count);
        // Свежая установка — ничего; обновление со старой версии без отметки — только текущая.
        Assert.Empty(WhatsNew.Pending(null, true, current, sections));
        Assert.Equal([new Version(1, 3, 0)], WhatsNew.Pending(null, false, current, sections).Select(s => s.Version));
    }

    [Fact]
    public void BoldParts_AreSplitOut() =>
        Assert.Equal([("Жирно", true), (": текст", false)], WhatsNew.Runs("**Жирно**: `текст`"));

    [Fact]
    public void BothChangelogs_AreEmbedded_AndListTheSameVersions()
    {
        var assembly = typeof(WhatsNew).Assembly;
        string Read(string name)
        {
            using var stream = assembly.GetManifestResourceStream(name);
            Assert.NotNull(stream);
            return new StreamReader(stream!).ReadToEnd();
        }
        var ru = WhatsNew.Parse(Read("CHANGELOG.md")).Select(s => s.Version).ToList();
        var en = WhatsNew.Parse(Read("CHANGELOG.en.md")).Select(s => s.Version).ToList();
        Assert.Equal(ru, en);
        // Текущая версия программы описана в CHANGELOG — иначе «Что нового» после обновления будет пустым.
        var current = assembly.GetName().Version!;
        Assert.Contains(new Version(current.Major, current.Minor, current.Build), ru);
    }
}
