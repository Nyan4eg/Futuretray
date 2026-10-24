#if DEBUG
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Futuretray.Core;
using Futuretray.UI;

namespace Futuretray;

/// <summary>
/// Только для отладки: Futuretray.exe --snapshot &lt;папка&gt; рисует окна в PNG и выходит.
/// Данные берутся из FUTURETRAY_DATA, ярлыки и автозапуск не трогаются.
/// </summary>
internal static class DebugSnapshot
{
    public static string? Directory { get; set; }

    public static void SeedDemo(ConfigStore store)
    {
        if (store.Config.Groups.Count > 0) return;
        var sys = Environment.SystemDirectory;
        var work = new Group { Name = "Работа", Color = "#3B82F6" };
        foreach (var p in new[] { Path.Combine(sys, "notepad.exe"), Path.Combine(sys, "calc.exe"), Path.Combine(sys, "cmd.exe"),
                     Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe"), Path.Combine(sys, "mspaint.exe"),
                     Path.Combine(sys, "taskmgr.exe"), @"C:\Windows" })
            if (File.Exists(p) || System.IO.Directory.Exists(p)) work.Items.Add(ItemFactory.FromPath(p));
        work.Items.Add(ItemFactory.FromUrl("https://github.com"));
        store.Config.Groups.Add(work);
        store.Config.Groups.Add(new Group { Name = "Игры", Color = "#EF4444" });
        var tools = new Group { Name = "Инструменты", Color = "#22C55E", ShowLabels = true, TileSize = TileSize.Large };
        tools.Items.Add(ItemFactory.FromPath(Path.Combine(sys, "cmd.exe")));
        tools.Items.Add(ItemFactory.FromPath(Path.Combine(sys, "notepad.exe")));
        store.Config.Groups.Add(tools);
    }

    public static void Run(App app)
    {
        var dir = Directory!;
        System.IO.Directory.CreateDirectory(dir);
        var steps = new List<(string name, Action action)>();
        MainWindow? editor = null;

        steps.Add(("editor", () => { app.ShowEditor(); editor = Current<MainWindow>(); editor!.Left = -4000; }));
        steps.Add(("editor-item", () => editor!.SnapshotSelectFirstItem()));
        steps.Add(("editor-scrolled", () => editor!.SnapshotScroll(900)));
        steps.Add(("editor-dock", () => { app.Store.Config.Groups[0].DockEnabled = true; editor!.SnapshotScroll(1500); }));
        steps.Add(("pin", () => editor!.SnapshotShowPin()));
        steps.Add(("settings", () => editor!.SnapshotShowSettings()));
        steps.Add(("empty-group", () => editor!.SnapshotSelectGroup(1)));

        int i = 0;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(900) };
        timer.Tick += async (_, _) =>
        {
            try
            {
                if (i > 0) Save(editor!, Path.Combine(dir, $"{i:00}-{steps[i - 1].name}.png"));
                if (i == steps.Count)
                {
                    timer.Stop();
                    SaveRunning(app, dir);
                    await CheckFeaturesAsync(app, dir);
                    SavePopup(app, dir);
                    SaveDocks(app, dir);
                    SaveSearch(dir);
                    DiagnosticsReport.Create(app.Store, dir);
                    app.Shutdown();
                    return;
                }
                steps[i].action();
                i++;
            }
            catch (Exception ex)
            {
                File.WriteAllText(Path.Combine(dir, "error.txt"), ex.ToString());
                timer.Stop();
                app.Shutdown();
            }
        };
        timer.Start();
    }

    private static T? Current<T>() where T : Window => Application.Current.Windows.OfType<T>().FirstOrDefault();

    private static void SavePopup(App app, string dir)
    {
        foreach (var (g, idx) in app.Store.Config.Groups.Select((g, idx) => (g, idx)))
        {
            var popup = new PopupWindow(g);
            popup.ShowAt(new Native.POINT(900, 900));
            popup.Body.BeginAnimation(UIElement.OpacityProperty, null);
            popup.Body.Opacity = 1;
            var t = (TranslateTransform)popup.Body.RenderTransform;
            t.BeginAnimation(TranslateTransform.XProperty, null);
            t.BeginAnimation(TranslateTransform.YProperty, null);
            popup.UpdateLayout();
            Save(popup, Path.Combine(dir, $"popup-{idx}.png"));
            popup.Close();
        }
    }

    /// <summary>Импорт из taskbar-groups, группа-папка и значок сайта — результаты в features.txt.</summary>
    private static async Task CheckFeaturesAsync(App app, string dir)
    {
        var sb = new System.Text.StringBuilder();

        // --- импорт taskbar-groups на образце их формата ---
        var tbg = Path.Combine(dir, "tbg", "config", "wow");
        System.IO.Directory.CreateDirectory(tbg);
        File.WriteAllText(Path.Combine(tbg, "ObjectData.xml"), $"""
            <?xml version="1.0" encoding="utf-8"?>
            <Category xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance" xmlns:xsd="http://www.w3.org/2001/XMLSchema">
              <Name>wow</Name>
              <ColorString>#1F1F1F</ColorString>
              <allowOpenAll>false</allowOpenAll>
              <ShortcutList>
                <ProgramShortcut>
                  <Arguments>--test</Arguments>
                  <WorkingDirectory>{Path.Combine(dir, "tbg")}\client.exe</WorkingDirectory>
                  <FilePath>{Path.Combine(Environment.SystemDirectory, "notepad.exe")}</FilePath>
                  <isWindowsApp>false</isWindowsApp>
                  <name>Мой блокнот</name>
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
            """);
        File.Copy(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Assets", "logo.png"), Path.Combine(tbg, "GroupImage.png"), true);
        foreach (var g in TaskbarGroupsImport.Load(Path.Combine(dir, "tbg")))
        {
            sb.AppendLine($"import: «{g.Name}» color={g.Color} columns={g.Columns} icon={g.IconMode} custom={File.Exists(g.CustomIconPath)}");
            foreach (var i in g.Items) sb.AppendLine($"    {i.Kind} {i.Name} → {i.Target} args={i.Arguments} wd={i.WorkingDirectory}");
        }

        // --- группа-папка ---
        var folder = Path.Combine(dir, "watched");
        System.IO.Directory.CreateDirectory(Path.Combine(folder, "Подпапка"));
        File.WriteAllText(Path.Combine(folder, "первый.txt"), "1");
        File.WriteAllText(Path.Combine(folder, "desktop.ini"), "");
        var folderGroup = new Group { Name = "Папка" };
        folderGroup.Items.Add(ItemFactory.FromUrl("https://example.com"));
        app.Store.Config.Groups.Add(folderGroup);
        var sync = new FolderSync(app.Store);
        folderGroup.SourceFolder = folder;
        sb.AppendLine("folder after link: " + string.Join(", ", folderGroup.Items.Select(i => i.Name)));
        File.WriteAllText(Path.Combine(folder, "второй.txt"), "2");
        File.Delete(Path.Combine(folder, "первый.txt"));
        await Task.Delay(2000);
        sb.AppendLine("folder after add/delete: " + string.Join(", ", folderGroup.Items.Select(i => i.Name)));

        // --- значок сайта ---
        var github = app.Store.Config.Groups[0].Items.First(i => i.Kind == ItemKind.Url);
        _ = github.Icon;
        for (int t = 0; t < 20 && Favicons.Stamp(github.Target) == ""; t++) await Task.Delay(500);
        sb.AppendLine($"favicon github.com: {(Favicons.Stamp(github.Target) == "" ? "нет" : "скачан")}");

        File.WriteAllText(Path.Combine(dir, "features.txt"), sb.ToString());
    }

    private static void SaveSearch(string dir)
    {
        var search = new SearchWindow();
        search.ShowNearCursor();
        search.Query.Text = "о";
        search.UpdateLayout();
        Save(search, Path.Combine(dir, "search.png"));
        search.Close();
    }

    /// <summary>Какие окна видит трекер и какие элементы групп он считает запущенными.</summary>
    private static void SaveRunning(App app, string dir)
    {
        var running = app.Running;
        running.Refresh();
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("== Окна ==");
        foreach (var w in running.Windows)
            sb.AppendLine($"{w.Handle} pid={w.ProcessId} {w.Path} appid={w.AppId} process-appid={w.ProcessAppId}");

        void Report(string title, IEnumerable<Group> groups)
        {
            sb.AppendLine().AppendLine($"== {title} ==");
            foreach (var g in groups)
                foreach (var item in g.Items)
                    sb.AppendLine($"[{g.Name}] {item.Name} ({item.Kind}: {item.Target}) → окон: {running.WindowsFor(item).Count}, активна: {running.IsActive(item)}");
        }

        Report("Демо-группы", app.Store.Config.Groups);
        var real = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Futuretray", "config.json");
        if (File.Exists(real) &&
            System.Text.Json.JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(real), ConfigStore.JsonOptions) is { } config)
            Report("Настоящие группы (только чтение)", config.Groups);
        // Все ярлыки «Пуска»: какие из запущенных программ узнаются.
        var startMenu = new[] { Environment.SpecialFolder.Programs, Environment.SpecialFolder.CommonPrograms }
            .SelectMany(f => System.IO.Directory.GetFiles(Environment.GetFolderPath(f), "*.lnk", SearchOption.AllDirectories))
            .Select(ItemFactory.FromPath).ToList();
        var found = startMenu.Where(i => running.WindowsFor(i).Count > 0).ToList();
        sb.AppendLine().AppendLine($"== Ярлыки «Пуска»: {startMenu.Count}, узнаны как запущенные: {found.Count} ==");
        foreach (var item in found) sb.AppendLine($"{item.Name} → окон: {running.WindowsFor(item).Count}");
        File.WriteAllText(Path.Combine(dir, "running.txt"), sb.ToString());
    }

    private static void SaveDocks(App app, string dir)
    {
        var groups = app.Store.Config.Groups;
        var cases = new[] { (groups[0], DockEdge.Left), (groups[2], DockEdge.Bottom), (groups[0], DockEdge.Free) };
        int n = 0;
        foreach (var (g, edge) in cases)
        {
            g.DockEdge = edge;
            var dock = new DockWindow(g);
            dock.Apply();
            dock.UpdateLayout();
            Save(dock, Path.Combine(dir, $"dock-{n++}-{edge}.png"));
            dock.CloseDock();
        }
    }

    private static void Save(Window window, string path)
    {
        window.UpdateLayout();
        var root = (FrameworkElement)window.Content;
        double w = Math.Max(1, root.ActualWidth), h = Math.Max(1, root.ActualHeight);
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            var bg = window.TryFindResource("Brush.WindowFallback") as Brush ?? Brushes.Black;
            dc.DrawRectangle(bg, null, new Rect(0, 0, w, h));
            dc.DrawRectangle(new VisualBrush(root), null, new Rect(0, 0, w, h));
        }
        var rtb = new RenderTargetBitmap((int)Math.Ceiling(w * 1.5), (int)Math.Ceiling(h * 1.5), 144, 144, PixelFormats.Pbgra32);
        rtb.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(rtb));
        using var fs = File.Create(path);
        encoder.Save(fs);
    }
}
#endif
