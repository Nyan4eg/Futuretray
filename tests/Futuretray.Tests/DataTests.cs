using Futuretray.Core;

namespace Futuretray.Tests;

[Collection("Config")]
public class TaskbarGroupsImportTests
{
    private static string WriteGroup(string root, string name, string xml)
    {
        var dir = Path.Combine(root, "config", name);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "ObjectData.xml"), xml);
        return dir;
    }

    private const string Sample = """
        <?xml version="1.0" encoding="utf-8"?>
        <Category xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance" xmlns:xsd="http://www.w3.org/2001/XMLSchema">
          <Name>wow</Name>
          <ColorString>#1F1F1F</ColorString>
          <allowOpenAll>false</allowOpenAll>
          <ShortcutList>
            <ProgramShortcut>
              <Arguments>--test</Arguments>
              <WorkingDirectory>C:\taskbar-groups\client.exe</WorkingDirectory>
              <FilePath>C:\Games\Battle.net\Battle.net Launcher.exe</FilePath>
              <isWindowsApp>false</isWindowsApp>
              <name>Battle.net</name>
            </ProgramShortcut>
            <ProgramShortcut>
              <Arguments />
              <FilePath>Microsoft.WindowsCalculator_8wekyb3d8bbwe!App</FilePath>
              <isWindowsApp>true</isWindowsApp>
              <name>Calculator</name>
            </ProgramShortcut>
          </ShortcutList>
          <Width>5</Width>
          <Opacity>10</Opacity>
        </Category>
        """;

    [Fact]
    public void ImportsGroupsItemsAndPicture()
    {
        var root = TestSetup.TempDir("tbg");
        var dir = WriteGroup(root, "wow", Sample);
        File.WriteAllBytes(Path.Combine(dir, "GroupImage.png"), new byte[] { 0x89, 0x50, 0x4E, 0x47 });

        var group = Assert.Single(TaskbarGroupsImport.Load(root));
        Assert.Equal("wow", group.Name);
        Assert.Equal("#1F1F1F", group.Color);
        Assert.Equal(5, group.Columns);
        Assert.Equal(GroupIconMode.Custom, group.IconMode);
        Assert.True(File.Exists(group.CustomIconPath));
        Assert.StartsWith(ConfigStore.DataDir, group.CustomIconPath);

        Assert.Equal(2, group.Items.Count);
        var program = group.Items[0];
        Assert.Equal(ItemKind.Path, program.Kind);
        Assert.Equal("Battle.net", program.Name);
        Assert.Equal("--test", program.Arguments);
        Assert.Null(program.WorkingDirectory); // папку самого taskbar-groups не переносим

        var store = group.Items[1];
        Assert.Equal(ItemKind.ShellApp, store.Kind);
        Assert.Equal("Microsoft.WindowsCalculator_8wekyb3d8bbwe!App", store.Target);
    }

    [Fact]
    public void BrokenGroupFile_IsSkipped_OthersStillImported()
    {
        var root = TestSetup.TempDir("tbg-broken");
        WriteGroup(root, "ok", Sample);
        WriteGroup(root, "broken", "<Category><Name>oops");

        var groups = TaskbarGroupsImport.Load(root);
        Assert.Single(groups);
    }

    [Fact]
    public void EmptyFolder_GivesNothing() => Assert.Empty(TaskbarGroupsImport.Load(TestSetup.TempDir("tbg-empty")));
}

[Collection("Config")]
public class FolderSyncTests
{
    [Fact]
    public void Sync_AddsFolderContents_SkipsHiddenAndSystem_KeepsManualItems()
    {
        var folder = TestSetup.TempDir("watched");
        Directory.CreateDirectory(Path.Combine(folder, "Подпапка"));
        File.WriteAllText(Path.Combine(folder, "b.txt"), "");
        File.WriteAllText(Path.Combine(folder, "a.txt"), "");
        File.WriteAllText(Path.Combine(folder, "desktop.ini"), "");
        var hidden = Path.Combine(folder, "secret.txt");
        File.WriteAllText(hidden, "");
        File.SetAttributes(hidden, FileAttributes.Hidden);

        var group = new Group { SourceFolder = folder };
        group.Items.Add(ItemFactory.FromUrl("https://example.com"));
        FolderSync.Sync(group);

        Assert.Equal(new[] { "Example.com", "Подпапка", "a", "b" }, group.Items.Select(i => i.Name));
    }

    [Fact]
    public void Sync_FollowsChanges_AndKeepsCustomizedItems()
    {
        var folder = TestSetup.TempDir("watched2");
        File.WriteAllText(Path.Combine(folder, "a.txt"), "");
        File.WriteAllText(Path.Combine(folder, "b.txt"), "");
        var group = new Group { SourceFolder = folder };
        FolderSync.Sync(group);

        var b = group.Items.Single(i => i.Name == "b");
        b.Name = "Мой b";
        File.Delete(Path.Combine(folder, "a.txt"));
        File.WriteAllText(Path.Combine(folder, "c.txt"), "");
        FolderSync.Sync(group);

        Assert.Equal(new[] { "Мой b", "c" }, group.Items.Select(i => i.Name));
        Assert.Same(b, group.Items[0]);
    }

    [Fact]
    public void Sync_WithMissingFolder_DoesNothing()
    {
        var group = new Group { SourceFolder = @"Z:\definitely\not\here" };
        group.Items.Add(ItemFactory.FromUrl("https://example.com"));
        FolderSync.Sync(group);
        Assert.Single(group.Items);
    }
}

[Collection("Config")]
public class ConfigTests
{
    private static ConfigStore LoadFrom(string json)
    {
        Directory.CreateDirectory(ConfigStore.DataDir);
        File.WriteAllText(ConfigStore.ConfigPath, json);
        var store = new ConfigStore();
        store.Load();
        return store;
    }

    // Конфиг в том виде, как его сохраняла версия 1.0.0 — без полей, появившихся позже.
    private const string Version100 = """
        {
          "Version": 1,
          "Settings": { "Theme": "Dark", "RunAtStartup": true, "ShowTrayIcon": true, "WelcomeShown": false },
          "Groups": [
            {
              "Id": "b9b98a8ceac5",
              "Name": "Рабочее",
              "Items": [
                { "Id": "6ba1a8b10f03", "Kind": "Path", "Target": "C:\\WINDOWS\\system32\\notepad.exe", "Name": "Блокнот", "RunAsAdmin": false },
                { "Id": "x1", "Kind": "ShellApp", "Target": "Claude_pzs8sxrjxfjjc!Claude", "Name": "Claude", "RunAsAdmin": false }
              ],
              "IconMode": "Auto", "ShowIconBackground": true, "Color": "#6D5DFC", "Columns": 0, "TileSize": "Medium",
              "ShowLabels": false, "Backdrop": "Acrylic", "TintOpacity": 0.12, "CloseAfterLaunch": true
            }
          ]
        }
        """;

    [Fact]
    public void OldConfig_LoadsWithSensibleDefaultsForNewSettings()
    {
        var config = LoadFrom(Version100).Config;
        Assert.Equal(ThemePreference.Dark, config.Settings.Theme);
        Assert.Equal("Win+Alt+Space", config.Settings.SearchHotkey);
        Assert.True(config.Settings.SwitchToRunning);
        Assert.True(config.Settings.CheckUpdates);
        Assert.True(config.Settings.DownloadFavicons);

        var group = Assert.Single(config.Groups);
        Assert.Equal("Рабочее", group.Name);
        Assert.Equal(2, group.Items.Count);
        Assert.False(group.DockEnabled);
        Assert.Null(group.Hotkey);
        Assert.Null(group.SourceFolder);
    }

    [Fact]
    public void BrokenConfig_IsBackedUp_AndAppStartsEmpty()
    {
        var store = LoadFrom("{ this is not json");
        Assert.Empty(store.Config.Groups);
        Assert.NotEmpty(Directory.GetFiles(ConfigStore.DataDir, "config.json.broken-*"));
    }

    [Fact]
    public void DuplicateIdsAndEmptyItems_AreRepaired()
    {
        var store = LoadFrom("""
            { "Groups": [
                { "Id": "same", "Name": "A", "Items": [ { "Kind": "Path", "Target": "", "Name": "пусто" } ] },
                { "Id": "same", "Name": "B", "Items": [] }
            ] }
            """);
        var groups = store.Config.Groups;
        Assert.Equal(2, groups.Count);
        Assert.NotEqual(groups[0].Id, groups[1].Id);
        Assert.Empty(groups[0].Items);
    }

    [Fact]
    public void SaveAndLoad_RoundTripsEverything()
    {
        var store = LoadFrom("{}");
        var group = new Group
        {
            Name = "Игры", Hotkey = "Ctrl+Alt+G", DockEnabled = true, DockEdge = DockEdge.Right,
            DockAutoHide = true, SourceFolder = @"C:\Games",
        };
        group.Items.Add(ItemFactory.FromUrl("https://store.steampowered.com", "Steam"));
        store.Config.Groups.Add(group);
        store.Config.Settings.SearchHotkey = null;
        store.SaveNow();

        var again = new ConfigStore();
        again.Load();
        var loaded = Assert.Single(again.Config.Groups);
        Assert.Equal("Ctrl+Alt+G", loaded.Hotkey);
        Assert.Equal(DockEdge.Right, loaded.DockEdge);
        Assert.True(loaded.DockAutoHide);
        Assert.Equal(@"C:\Games", loaded.SourceFolder);
        Assert.Equal("Steam", Assert.Single(loaded.Items).Name);
        Assert.Null(again.Config.Settings.SearchHotkey);
    }
}
