#if DEBUG
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
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
        bool en = Loc.IsEnglish;
        // Описания exe берутся из Windows на её языке — для английских скриншотов задаём имена сами.
        var english = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["notepad.exe"] = "Notepad", ["calc.exe"] = "Calculator", ["cmd.exe"] = "Command Prompt",
            ["explorer.exe"] = "File Explorer", ["mspaint.exe"] = "Paint", ["taskmgr.exe"] = "Task Manager", ["Windows"] = "Windows",
        };
        LaunchItem Item(string path)
        {
            var item = ItemFactory.FromPath(path);
            if (en && english.TryGetValue(Path.GetFileName(path), out var name)) item.Name = name;
            return item;
        }

        var sys = Environment.SystemDirectory;
        var work = new Group { Name = en ? "Work" : "Работа", Color = "#3B82F6" };
        foreach (var p in new[] { Path.Combine(sys, "notepad.exe"), Path.Combine(sys, "calc.exe"), Path.Combine(sys, "cmd.exe"),
                     Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe"), Path.Combine(sys, "mspaint.exe"),
                     Path.Combine(sys, "taskmgr.exe"), @"C:\Windows" })
            if (File.Exists(p) || System.IO.Directory.Exists(p)) work.Items.Add(Item(p));
        work.Items.Add(ItemFactory.FromUrl("https://github.com", "GitHub"));
        store.Config.Groups.Add(work);
        store.Config.Groups.Add(new Group { Name = en ? "Games" : "Игры", Color = "#EF4444" });
        var tools = new Group { Name = en ? "Tools" : "Инструменты", Color = "#22C55E", ShowLabels = true, TileSize = TileSize.Large };
        tools.Items.Add(Item(Path.Combine(sys, "cmd.exe")));
        tools.Items.Add(Item(Path.Combine(sys, "notepad.exe")));
        store.Config.Groups.Add(tools);
    }

    public static void Run(App app)
    {
        var dir = Directory!;
        System.IO.Directory.CreateDirectory(dir);
        if (Environment.GetEnvironmentVariable("FUTURETRAY_CHECK") == "workspace")
        {
            app.Dispatcher.BeginInvoke(async () =>
            {
                try { await CheckWorkspaceAsync(dir); }
                catch (Exception ex) { File.WriteAllText(Path.Combine(dir, "error.txt"), ex.ToString()); }
                app.Shutdown();
            });
            return;
        }
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
                    await CheckDockLayoutAsync(app, dir);
                    SaveRunning(app, dir);
                    await CheckFeaturesAsync(app, dir);
                    SavePopup(app, dir);
                    SaveDocks(app, dir);
                    SaveSearch(dir);
                    SaveHero(app, dir);
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

    /// <summary>
    /// Живая проверка рабочих пространств на двух формах PowerShell: запомнить → закрыть → разложить
    /// с запуском (вторая форма появляется через 3 с и сначала показывает заставку) → разложить уже открытые.
    /// Итог — workspace.txt.
    /// </summary>
    private static async Task CheckWorkspaceAsync(string dir)
    {
        var sb = new System.Text.StringBuilder();
        var script = Path.Combine(dir, "form.ps1");
        File.WriteAllText(script, """
            param([string]$Name, [int]$Delay = 0, [switch]$Splash)
            Add-Type -AssemblyName System.Windows.Forms
            Start-Sleep -Seconds $Delay
            if ($Splash) {
                $s = New-Object System.Windows.Forms.Form
                $s.Text = "$Name splash"; $s.StartPosition = 'Manual'; $s.Location = '700,300'; $s.Size = '300,200'
                $s.Show(); [System.Windows.Forms.Application]::DoEvents(); Start-Sleep -Milliseconds 2500; $s.Close()
            }
            $f = New-Object System.Windows.Forms.Form
            $f.Text = $Name; $f.StartPosition = 'Manual'; $f.Location = '200,200'; $f.Size = '400,300'
            [System.Windows.Forms.Application]::Run($f)
            """);
        // Своя копия powershell.exe: уникальный путь, чтобы не спутать формы с консолями пользователя.
        var ps = Path.Combine(dir, "ftform.exe");
        File.Copy(Path.Combine(Environment.SystemDirectory, @"WindowsPowerShell\v1.0\powershell.exe"), ps, true);
        LaunchItem Form(string name, string extra) => new()
        {
            Kind = ItemKind.Path, Target = ps, Name = name,
            Arguments = $"-NoProfile -WindowStyle Hidden -ExecutionPolicy Bypass -File \"{script}\" -Name {name} {extra}",
        };
        var group = new Group { Name = "ws" };
        group.Items.Add(Form("FormA", ""));
        group.Items.Add(Form("FormB", "-Delay 3 -Splash"));
        var running = RunningApps.Current!;
        var work = Monitors.Find(null).Work;

        async Task WaitWindows(int count)
        {
            for (int t = 0; t < 100 && running.WindowsFor(group.Items[0]).Count < count; t++) { await Task.Delay(200); running.Refresh(); }
        }
        void CloseAll() { foreach (var p in System.Diagnostics.Process.GetProcessesByName("ftform")) p.Kill(); }
        string Rects() => string.Join("; ", running.WindowsFor(group.Items[0]).Select(w => { var r = Native.VisibleBounds(w); return $"{r.Left},{r.Top},{r.Width}x{r.Height}"; }).Order());

        CloseAll();

        // 1. Запомнить: одна открытая форма, вторая — нет.
        Launcher.Launch(new LaunchItem { Kind = ItemKind.Path, Target = ps, Name = "FormA", Arguments = group.Items[0].Arguments });
        await WaitWindows(1);
        var a = running.WindowsFor(group.Items[0]).First();
        foreach (var w in running.WindowsFor(group.Items[0]))
        {
            Native.GetWindowThreadProcessId(w, out var wp);
            sb.AppendLine($"found: {w} class={Native.ClassName(w)} pid={wp} iconic={Native.IsIconic(w)}");
        }
        Native.SetWindowPos(a, IntPtr.Zero, work.Left + 50, work.Top + 60, 640, 480, Native.SWP_NOZORDER | Native.SWP_NOACTIVATE);
        await Task.Delay(300);
        var capture = Workspaces.Capture(group);
        sb.AppendLine($"capture: {Workspaces.Describe(capture)}");
        var la = group.Items[0].Layout;
        sb.AppendLine($"capture-ok: {capture.Saved == 1 && capture.NotRunning.Count == 1 && la != null}");
        var restored = la == null ? default : Workspaces.ToRect(la, work);
        var actual = Native.VisibleBounds(a);
        sb.AppendLine($"capture-roundtrip: {restored.Left},{restored.Top},{restored.Width}x{restored.Height} vs {actual.Left},{actual.Top},{actual.Width}x{actual.Height}");
        CloseAll();
        await Task.Delay(800);

        // 2. Разложить с запуском: левая и правая половины рабочей области.
        group.Items[0].Layout = new WindowLayout { X = 0, Y = 0, Width = 0.5, Height = 0.6 };
        group.Items[1].Layout = new WindowLayout { X = 0.5, Y = 0.2, Width = 0.5, Height = 0.6 };
        var expected = group.Items.Select(i => Workspaces.ToRect(i.Layout!, work)).Select(r => $"{r.Left},{r.Top},{r.Width}x{r.Height}").Order().ToList();
        sb.AppendLine($"expected: {string.Join("; ", expected)}");

        async Task<Workspaces.ArrangeResult> ArrangeAndWait(int settle)
        {
            var tcs = new TaskCompletionSource<Workspaces.ArrangeResult>();
            var started = DateTime.Now;
            Workspaces.Arrange(group, r => tcs.TrySetResult(r));
            var result = await tcs.Task;
            sb.AppendLine($"  done after {(DateTime.Now - started).TotalSeconds:0.0}s: {Workspaces.Describe(result)}");
            await Task.Delay(settle);
            running.Refresh();
            return result;
        }

        var r1 = await ArrangeAndWait(14000); // ждём конец наблюдения за заставкой
        var got = Rects();
        sb.AppendLine($"launch-arrange: {got}");
        sb.AppendLine($"launch-arrange-ok: {r1.Placed == 2 && got == string.Join("; ", expected)}");

        // 3. Уже открыты, но сдвинуты — раскладываются сразу.
        foreach (var w in running.WindowsFor(group.Items[0]))
            Native.SetWindowPos(w, IntPtr.Zero, work.Left + 300, work.Top + 300, 300, 200, Native.SWP_NOZORDER | Native.SWP_NOACTIVATE);
        var r2 = await ArrangeAndWait(300);
        got = Rects();
        sb.AppendLine($"rearrange: {got}");
        sb.AppendLine($"rearrange-ok: {r2.Placed == 2 && r2.NotFound.Count == 0 && got == string.Join("; ", expected)}");

        // 4. Развёрнутое окно.
        group.Items[1].Layout!.Maximized = true;
        await ArrangeAndWait(300);
        sb.AppendLine($"maximized-ok: {running.WindowsFor(group.Items[0]).Count(Native.IsZoomed) == 1}");

        CloseAll();
        File.WriteAllText(Path.Combine(dir, "workspace.txt"), sb.ToString());
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

            // То же окно, как его увидит пользователь с панелью задач слева.
            popup.Tiles.AlongVerticalTaskbar = true;
            popup.Tiles.Rebuild();
            popup.UpdateSize();
            popup.UpdateLayout();
            Save(popup, Path.Combine(dir, $"popup-{idx}-vertical-taskbar.png"));
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

    /// <summary>Обложка для README: рабочий стол, панель задач, раскрытая группа и трей сбоку.</summary>
    private static void SaveHero(App app, string dir)
    {
        var group = app.Store.Config.Groups[0];
        var tools = app.Store.Config.Groups[2];
        var dict = ThemeManager.PopupDictionary(true);

        var root = new Grid { Width = 1100, Height = 620, ClipToBounds = true };
        root.Resources.MergedDictionaries.Add(dict);
        root.SetResourceReference(TextElement.ForegroundProperty, "Brush.Text");
        root.Children.Add(new Border { Background = new LinearGradientBrush(
            Color.FromRgb(0x1B, 0x1F, 0x3B), Color.FromRgb(0x0F, 0x4C, 0x75), 35) });
        root.Children.Add(new Border { Background = new RadialGradientBrush(Color.FromArgb(0x90, 0x6D, 0x5D, 0xFC), Colors.Transparent)
            { Center = new Point(0.7, 0.25), GradientOrigin = new Point(0.7, 0.25), RadiusX = 0.6, RadiusY = 0.7 } });

        UIElement Panel(Group g, Orientation? line, bool labels)
        {
            var tiles = new GroupTilesView { IsPreview = true, Line = line, Margin = new Thickness(6) };
            g.ShowLabels = labels;
            tiles.Bind(g);
            var shade = ColorUtil.Resolve(g.Color);
            var panel = new Grid();
            panel.Children.Add(new Border
            {
                CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1),
                Background = new SolidColorBrush(Color.FromArgb(0xE6, 0x28, 0x28, 0x2C)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF)),
                Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 30, ShadowDepth = 8, Direction = 270, Opacity = 0.45 },
            });
            panel.Children.Add(new Border { CornerRadius = new CornerRadius(8), Background = new SolidColorBrush(ColorUtil.WithAlpha(shade, 0.12)) });
            panel.Children.Add(tiles);
            return panel;
        }

        // Трей группы «Инструменты» у левого края.
        var dock = Panel(tools, Orientation.Vertical, false);
        ((FrameworkElement)dock).HorizontalAlignment = HorizontalAlignment.Left;
        ((FrameworkElement)dock).VerticalAlignment = VerticalAlignment.Center;
        ((FrameworkElement)dock).Margin = new Thickness(14, 0, 0, 40);
        root.Children.Add(dock);

        // Раскрытая группа над значком на панели задач.
        var popup = Panel(group, null, true);
        ((FrameworkElement)popup).HorizontalAlignment = HorizontalAlignment.Center;
        ((FrameworkElement)popup).VerticalAlignment = VerticalAlignment.Bottom;
        ((FrameworkElement)popup).Margin = new Thickness(60, 0, 0, 62);
        root.Children.Add(popup);

        var taskbar = new Border { Height = 48, VerticalAlignment = VerticalAlignment.Bottom, Background = new SolidColorBrush(Color.FromArgb(0xEE, 0x1C, 0x1C, 0x1E)) };
        var icons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        var start = new UniformGrid { Rows = 2, Columns = 2, Width = 18, Height = 18, Margin = new Thickness(11, 0, 11, 0) };
        for (int i = 0; i < 4; i++) start.Children.Add(new Border { Background = new SolidColorBrush(Color.FromRgb(0x2D, 0x9C, 0xDB)), Margin = new Thickness(0.75), CornerRadius = new CornerRadius(1) });
        icons.Children.Add(start);
        foreach (var item in group.Items.Take(2))
            icons.Children.Add(new Image { Source = item.Icon, Width = 24, Height = 24, Margin = new Thickness(10, 0, 10, 0) });
        var active = new Grid { Width = 40, Height = 40, Margin = new Thickness(4, 0, 4, 0) };
        active.Children.Add(new Border { CornerRadius = new CornerRadius(4), Background = new SolidColorBrush(Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF)) });
        active.Children.Add(new Image { Source = group.Icon, Width = 26, Height = 26 });
        active.Children.Add(new Border { Width = 16, Height = 3, CornerRadius = new CornerRadius(1.5), VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 0, 2), Background = new SolidColorBrush(ThemeManager.AccentLight) });
        icons.Children.Add(active);
        icons.Children.Add(new Image { Source = tools.Icon, Width = 24, Height = 24, Margin = new Thickness(10, 0, 10, 0) });
        taskbar.Child = icons;
        root.Children.Add(taskbar);

        root.Measure(new Size(root.Width, root.Height));
        root.Arrange(new Rect(0, 0, root.Width, root.Height));
        root.UpdateLayout();
        var rtb = new RenderTargetBitmap(1650, 930, 144, 144, PixelFormats.Pbgra32);
        rtb.Render(root);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(rtb));
        using var fs = File.Create(Path.Combine(dir, "hero.png"));
        encoder.Save(fs);
    }

    /// <summary>Ориентация трея после старта и после смены края (ловим «вертикальный трей снизу»).</summary>
    private static async Task CheckDockLayoutAsync(App app, string dir)
    {
        var sb = new System.Text.StringBuilder();
        var g = app.Store.Config.Groups[0];

        async Task Report(DockWindow dock, string title)
        {
            for (int i = 0; i < 6; i++) await Task.Delay(150); // даём отработать отложенным перерисовкам
            var tiles = dock.Tiles.Tiles;
            string layout = tiles.Count < 2 ? "?" :
                Math.Abs(tiles[0].TranslatePoint(new Point(), dock.Body).Y - tiles[1].TranslatePoint(new Point(), dock.Body).Y) < 1 ? "ряд" : "столбец";
            Native.GetWindowRect(new System.Windows.Interop.WindowInteropHelper(dock).Handle, out var r);
            sb.AppendLine($"{title}: край={g.DockEdge} Body={dock.Body.Orientation} Line={dock.Tiles.Line} плитки={layout} окно={r.Width}x{r.Height}");
        }

        // 1) Старт сразу «снизу».
        g.DockEdge = DockEdge.Bottom;
        var fresh = new DockWindow(g);
        fresh.Apply();
        await Report(fresh, "старт снизу");
        fresh.CloseDock();

        // 2) Переход «слева → снизу» на одном окне — как при смене положения в редакторе.
        g.DockEdge = DockEdge.Left;
        var dock = new DockWindow(g);
        dock.Apply();
        await Report(dock, "слева");
        g.DockEdge = DockEdge.Bottom;
        dock.Apply();
        await Report(dock, "слева → снизу");

        // 3) «Свободно → снизу».
        g.DockEdge = DockEdge.Free;
        dock.Apply();
        await Report(dock, "свободно");
        g.DockEdge = DockEdge.Bottom;
        dock.Apply();
        await Report(dock, "свободно → снизу");
        dock.CloseDock();

        // 4) Настоящий путь программы: DockManager реагирует на изменения группы, как из редактора.
        var other = app.Store.Config.Groups[2];
        other.DockEdge = DockEdge.Bottom;
        var manager = new DockManager(app.Store);
        DockWindow Find() => Application.Current.Windows.OfType<DockWindow>().First(d => d.Group == other);

        other.DockEnabled = true;
        await Report2("менеджер: включили, снизу");
        other.DockEdge = DockEdge.Left;
        await Report2("менеджер: снизу → слева");
        other.DockEdge = DockEdge.Bottom;
        await Report2("менеджер: слева → снизу");
        other.DockEnabled = false;
        await Task.Delay(500);
        manager.SyncAll();
        var restarted = new DockManager(app.Store);
        other.DockEnabled = true;
        restarted.SyncAll();
        await Report2("менеджер: перезапуск, снизу");
        manager.CloseAll();
        restarted.CloseAll();

        async Task Report2(string title)
        {
            for (int i = 0; i < 8; i++) await Task.Delay(150);
            var dock = Find();
            var tiles = dock.Tiles.Tiles;
            string layout = tiles.Count < 2 ? "?" :
                Math.Abs(tiles[0].TranslatePoint(new Point(), dock.Body).Y - tiles[1].TranslatePoint(new Point(), dock.Body).Y) < 1 ? "ряд" : "столбец";
            Native.GetWindowRect(new System.Windows.Interop.WindowInteropHelper(dock).Handle, out var r);
            sb.AppendLine($"{title}: край={other.DockEdge} Body={dock.Body.Orientation} Line={dock.Tiles.Line} плитки={layout} окно={r.Width}x{r.Height} @{r.Left},{r.Top}");
        }

        File.WriteAllText(Path.Combine(dir, "dock-layout.txt"), sb.ToString());
    }

    private static void SaveSearch(string dir)
    {
        var search = new SearchWindow();
        search.ShowNearCursor();
        search.Query.Text = Loc.IsEnglish ? "e" : "о";
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
