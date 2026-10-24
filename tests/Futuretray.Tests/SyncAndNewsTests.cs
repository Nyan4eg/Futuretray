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

    [Theory]
    [InlineData("missing")]  // OneDrive ещё не запущен при входе в Windows
    [InlineData("broken")]   // недокачанный или испорченный файл с другого компьютера
    public void UnavailableSyncFolder_NeverGetsOverwritten(string kind)
    {
        var store = new ConfigStore();
        store.Load();
        store.Config.Groups.Add(new Group { Name = "Mine" });
        var folder = TestSetup.TempDir("sync");
        store.EnableSync(folder, useExisting: false);
        var shared = Path.Combine(folder, "config.json");
        try
        {
            if (kind == "missing")
            {
                Directory.Delete(folder, true);
            }
            else
            {
                File.WriteAllText(shared, "{ \"Groups\": [ {\"Name\": \"half");
            }

            var next = new ConfigStore();
            next.Load();
            Assert.True(next.SyncUnavailable);
            Assert.Contains(next.Config.Groups, g => g.Name == "Mine"); // локальная копия
            next.Config.Groups.Add(new Group { Name = "Offline edit" });
            Assert.True(next.SaveNow());

            if (kind == "missing") Assert.False(Directory.Exists(folder));
            else Assert.StartsWith("{ \"Groups\": [ {\"Name\": \"half", File.ReadAllText(shared));
        }
        finally
        {
            Directory.CreateDirectory(folder);
            store.DisableSync();
            Cleanup();
        }
    }

    [Fact]
    public void FrozenStore_DoesNotSaveStaleGroups()
    {
        var store = new ConfigStore();
        store.Load();
        store.SaveNow();
        var before = File.ReadAllText(ConfigStore.ConfigPath);
        store.Freeze();
        store.Config.Groups.Add(new Group { Name = "Stale" });
        store.SaveNow();
        Assert.Equal(before, File.ReadAllText(ConfigStore.ConfigPath));
    }

    [Fact]
    public void PerComputerSettings_StayOutOfSharedConfig()
    {
        var store = new ConfigStore();
        store.Load();
        store.Config.Settings.LastSeenVersion = "9.9.9";
        store.Config.Settings.LastUpdateCheck = new DateTime(2026, 1, 2);
        store.SaveNow();
        Assert.DoesNotContain("LastSeenVersion", File.ReadAllText(ConfigStore.ConfigPath));
        Assert.DoesNotContain("LastUpdateCheck", File.ReadAllText(ConfigStore.ConfigPath));

        var again = new ConfigStore();
        again.Load();
        Assert.Equal("9.9.9", again.Config.Settings.LastSeenVersion);
        Assert.Equal(new DateTime(2026, 1, 2), again.Config.Settings.LastUpdateCheck);
    }

    [Fact]
    public void NullListsInConfig_DoNotBreakLoading()
    {
        Directory.CreateDirectory(ConfigStore.DataDir);
        var path = ConfigStore.ConfigPath;
        var backup = File.Exists(path) ? File.ReadAllText(path) : null;
        try
        {
            File.WriteAllText(path, """{ "Settings": null, "Groups": [ { "Name": "x", "Items": null }, null ] }""");
            var store = new ConfigStore();
            store.Load();
            Assert.NotNull(store.Config.Settings);
            Assert.Single(store.Config.Groups);
            Assert.Empty(store.Config.Groups[0].Items);
        }
        finally
        {
            if (backup != null) File.WriteAllText(path, backup);
            else File.Delete(path);
        }
    }

    /// <summary>Убрать группы, которые добавили тесты, из общего тестового config.json.</summary>
    private static void Cleanup()
    {
        var store = new ConfigStore();
        store.Load();
        foreach (var g in store.Config.Groups.Where(g => g.Name is "Mine" or "Offline edit" or "Stale").ToList())
            store.Config.Groups.Remove(g);
        store.SaveNow();
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
