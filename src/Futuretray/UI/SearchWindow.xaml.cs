using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Futuretray.Core;

namespace Futuretray.UI;

public sealed record SearchResult(LaunchItem Item, Group Group, int Score)
{
    public string Name => Item.Name;
    public string Subtitle => Item.Kind == ItemKind.Url ? Item.Target : $"{Group.Name} · {Item.KindLabel}";
    public string GroupName => Group.Name;
    public ImageSource? Icon => Item.Icon;
    public ImageSource? GroupIcon => Group.Icon;
    public bool IsRunning => RunningApps.Current?.IsRunning(Item) == true;
}

/// <summary>Поиск по всем группам: сочетание клавиш → набрали пару букв → Enter.</summary>
public partial class SearchWindow : Window
{
    private const int MaxResults = 40;
    private ResourceDictionary? _theme;

    public SearchWindow()
    {
        InitializeComponent();
        WindowEffects.InstallChrome(this, 0, 0);
        SourceInitialized += (_, _) => WindowEffects.MakeToolWindow(this);
        Deactivated += (_, _) =>
        {
            if (IsVisible) Hide();
        };
    }

    private IntPtr Hwnd => new WindowInteropHelper(this).EnsureHandle();

    public void ShowNearCursor()
    {
        Native.GetCursorPos(out var cursor);
        var monitor = Monitors.FromPoint(cursor);

        bool dark = ThemeManager.PopupDark;
        var dict = ThemeManager.PopupDictionary(dark);
        if (!ReferenceEquals(_theme, dict))
        {
            if (_theme != null) Resources.MergedDictionaries.Remove(_theme);
            Resources.MergedDictionaries.Add(dict);
            _theme = dict;
        }

        // Сначала на нужный монитор — чтобы применился его масштаб.
        Native.SetWindowPos(Hwnd, IntPtr.Zero, monitor.Work.Left, monitor.Work.Top, 0, 0,
            Native.SWP_NOSIZE | Native.SWP_NOZORDER | Native.SWP_NOACTIVATE);
        bool backdrop = WindowEffects.Apply(this, BackdropKind.Acrylic, dark);
        SolidLayer.Visibility = backdrop ? Visibility.Collapsed : Visibility.Visible;

        Query.Text = "";
        Refresh();
        UpdateSize();

        double scale = monitor.Dpi / 96.0;
        int w = (int)Math.Round(Width * scale);
        int x = (monitor.Work.Left + monitor.Work.Right - w) / 2;
        int y = monitor.Work.Top + (int)(monitor.Work.Height * 0.2);
        Native.SetWindowPos(Hwnd, Native.HWND_TOPMOST, x, y, 0, 0, Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);

        Show();
        WindowEffects.ForceForeground(this);
        Query.Focus();
        Keyboard.Focus(Query);
    }

    private void UpdateSize()
    {
        Body.Measure(new Size(Width, double.PositiveInfinity));
        Height = Math.Ceiling(Body.DesiredSize.Height + Body.Margin.Top + Body.Margin.Bottom);
    }

    private void Query_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        Refresh();
        if (IsVisible) Dispatcher.BeginInvoke(UpdateSize, DispatcherPriority.Loaded);
    }

    private void Refresh()
    {
        var query = Query.Text.Trim();
        var results = new List<SearchResult>();
        int order = 0;
        foreach (var group in App.Host.Store.Config.Groups)
        {
            foreach (var item in group.Items)
            {
                int score = Score(item.Name, group.Name, query);
                // Порядок в группах — второй критерий, чтобы выдача была стабильной.
                if (score > 0) results.Add(new SearchResult(item, group, score * 10000 - order));
                order++;
            }
        }

        var top = results.OrderByDescending(r => r.Score).Take(MaxResults).ToList();
        Results.ItemsSource = top;
        Results.SelectedIndex = top.Count > 0 ? 0 : -1;
        EmptyText.Visibility = top.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        Results.Visibility = top.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>0 — не подходит; больше — лучше.</summary>
    public static int Score(string name, string groupName, string query)
    {
        if (query.Length == 0) return 1;
        var cmp = StringComparison.CurrentCultureIgnoreCase;
        if (name.StartsWith(query, cmp)) return 100;
        var words = name.Split(new[] { ' ', '-', '_', '.', '(', ')' }, StringSplitOptions.RemoveEmptyEntries);
        if (words.Any(w => w.StartsWith(query, cmp))) return 80;
        if (name.Contains(query, cmp)) return 60;
        if (string.Concat(words.Select(w => w[0])).StartsWith(query, cmp)) return 50; // «vsc» → Visual Studio Code
        if (groupName.Contains(query, cmp)) return 20;
        return 0;
    }

    private void Query_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        int count = Results.Items.Count;
        switch (e.Key)
        {
            case Key.Escape:
                if (Query.Text.Length > 0) Query.Text = "";
                else Hide();
                e.Handled = true;
                break;
            case Key.Down when count > 0:
                Results.SelectedIndex = (Results.SelectedIndex + 1) % count;
                Results.ScrollIntoView(Results.SelectedItem);
                e.Handled = true;
                break;
            case Key.Up when count > 0:
                Results.SelectedIndex = (Results.SelectedIndex - 1 + count) % count;
                Results.ScrollIntoView(Results.SelectedItem);
                e.Handled = true;
                break;
            case Key.Enter:
                OpenSelected(Keyboard.Modifiers.HasFlag(ModifierKeys.Shift));
                e.Handled = true;
                break;
        }
    }

    private void Results_MouseUp(object sender, MouseButtonEventArgs e) =>
        OpenSelected(Keyboard.Modifiers.HasFlag(ModifierKeys.Shift));

    private void OpenSelected(bool newInstance)
    {
        if (Results.SelectedItem is not SearchResult result) return;
        // Сначала открываем (пока у нас есть право вывести окно вперёд), потом прячемся.
        var error = Launcher.Open(result.Item, newInstance);
        Hide();
        if (error != null) App.Host.ShowEditor(result.Group.Id, error);
    }
}
