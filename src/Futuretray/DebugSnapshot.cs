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
        timer.Tick += (_, _) =>
        {
            try
            {
                if (i > 0) Save(editor!, Path.Combine(dir, $"{i:00}-{steps[i - 1].name}.png"));
                if (i == steps.Count)
                {
                    SavePopup(app, dir);
                    SaveDocks(app, dir);
                    DiagnosticsReport.Create(app.Store, dir);
                    timer.Stop();
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
