using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Futuretray.Core;

namespace Futuretray.UI;

/// <summary>Сетка плиток группы. Используется во всплывающем окне и в предпросмотре редактора.</summary>
public sealed class GroupTilesView : ContentControl
{
    private readonly UniformGrid _grid = new();
    private readonly List<Button> _tiles = new();
    private readonly Dictionary<Button, Border> _indicators = new();
    private Group? _group;
    private bool _trackingRunning;
    private bool _rebuildQueued;

    public GroupTilesView()
    {
        Content = _grid;
        Focusable = false;
        KeyboardNavigation.SetDirectionalNavigation(_grid, KeyboardNavigationMode.Cycle);
        KeyboardNavigation.SetTabNavigation(_grid, KeyboardNavigationMode.Cycle);
    }

    public bool IsPreview { get; set; }

    /// <summary>Для трея: все плитки в один ряд (Horizontal) или столбец (Vertical); null — сетка.</summary>
    public Orientation? Line { get; set; }

    /// <summary>В трее плитки не забирают фокус клавиатуры.</summary>
    public bool TilesFocusable { get; set; } = true;

    /// <summary>Клик по элементу: (элемент, не закрывать окно — Ctrl, новая копия — Shift или средняя кнопка).</summary>
    public event Action<LaunchItem, bool, bool>? ItemInvoked;

    /// <summary>С какой стороны плитки рисовать индикатор запущенной программы (в трее — к краю экрана).</summary>
    public Dock IndicatorSide { get; set; } = Dock.Bottom;
    public event Action<LaunchItem, Button>? ItemMenuRequested;

    /// <summary>Принимать файлы, перетащенные на плитку программы (в трее).</summary>
    public bool AcceptFileDrop { get; set; }

    public event Action<LaunchItem, string[]>? FilesDropped;

    public IReadOnlyList<Button> Tiles => _tiles;

    public void Bind(Group group)
    {
        if (_group != null) _group.Changed -= OnGroupChanged;
        _group = group;
        _group.Changed += OnGroupChanged;
        if (!IsPreview && !_trackingRunning && RunningApps.Current is { } running)
        {
            running.Changed += UpdateIndicators;
            _trackingRunning = true;
        }
        Rebuild();
    }

    public void Unbind()
    {
        if (_group != null) _group.Changed -= OnGroupChanged;
        if (_trackingRunning && RunningApps.Current is { } running) running.Changed -= UpdateIndicators;
        _trackingRunning = false;
        _indicators.Clear();
        _group = null;
        _grid.Children.Clear();
        _tiles.Clear();
    }

    private void OnGroupChanged(object? sender, EventArgs e)
    {
        if (_rebuildQueued) return;
        _rebuildQueued = true;
        Dispatcher.BeginInvoke(() =>
        {
            _rebuildQueued = false;
            Rebuild();
        });
    }

    public static (double width, double height, double icon) Metrics(Group g) => g.TileSize switch
    {
        TileSize.Small => (g.ShowLabels ? 72 : 42, g.ShowLabels ? 70 : 42, 24),
        TileSize.Large => (g.ShowLabels ? 92 : 68, g.ShowLabels ? 96 : 68, 44),
        _ => (g.ShowLabels ? 80 : 54, g.ShowLabels ? 82 : 54, 32),
    };

    public static int ColumnsFor(Group g)
    {
        int n = Math.Max(g.Items.Count, 1);
        if (g.Columns > 0) return Math.Min(g.Columns, n);
        return n <= 6 ? n : (int)Math.Ceiling(Math.Sqrt(n * 1.6));
    }

    public void Rebuild()
    {
        _grid.Children.Clear();
        _tiles.Clear();
        _indicators.Clear();
        if (_group == null) return;

        var g = _group;
        _grid.Columns = Line switch
        {
            Orientation.Horizontal => Math.Max(g.Items.Count, 1),
            Orientation.Vertical => 1,
            _ => ColumnsFor(g),
        };

        if (g.Items.Count == 0)
        {
            _grid.Children.Add(new TextBlock
            {
                Text = "Группа пуста.\nДобавьте приложения в редакторе.",
                TextAlignment = TextAlignment.Center,
                Margin = new Thickness(18, 14, 18, 14),
                FontSize = 13,
                Opacity = 0.75,
            });
            return;
        }

        var (w, h, iconSize) = Metrics(g);
        for (int i = 0; i < g.Items.Count; i++)
        {
            var tile = CreateTile(g.Items[i], i, w, h, iconSize, g.ShowLabels);
            _tiles.Add(tile);
            _grid.Children.Add(tile);
        }
        UpdateIndicators();
    }

    /// <summary>Чёрточка у плитки: короткая — программа открыта, длинная цветом акцента — она сейчас активна.</summary>
    public void UpdateIndicators()
    {
        var running = RunningApps.Current;
        foreach (var (tile, pill) in _indicators)
        {
            var item = (LaunchItem)tile.Tag;
            bool isRunning = running?.IsRunning(item) == true;
            pill.Visibility = isRunning ? Visibility.Visible : Visibility.Collapsed;
            if (!isRunning) continue;

            bool active = running!.IsActive(item);
            double length = active ? 14 : 6;
            bool across = IndicatorSide is Dock.Left or Dock.Right;
            pill.Width = across ? 3 : length;
            pill.Height = across ? length : 3;
            pill.SetResourceReference(Border.BackgroundProperty, active ? "Brush.Accent" : "Brush.TextSecondary");
        }
    }

    private Button CreateTile(LaunchItem item, int index, double w, double h, double iconSize, bool labels)
    {
        var image = new Image { Source = item.Icon, Width = iconSize, Height = iconSize };
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);

        var panel = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        panel.Children.Add(image);
        if (labels)
        {
            panel.Children.Add(new TextBlock
            {
                Text = item.Name,
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxHeight = 32,
                Width = w - 8,
                FontSize = 11.5,
                LineHeight = 15,
                Margin = new Thickness(0, 5, 0, 0),
            });
        }

        var content = new Grid();
        content.Children.Add(panel);
        Border? pill = null;
        if (!IsPreview)
        {
            pill = new Border
            {
                CornerRadius = new CornerRadius(1.5),
                Visibility = Visibility.Collapsed,
                IsHitTestVisible = false,
                HorizontalAlignment = IndicatorSide switch { Dock.Left => HorizontalAlignment.Left, Dock.Right => HorizontalAlignment.Right, _ => HorizontalAlignment.Center },
                VerticalAlignment = IndicatorSide switch { Dock.Top => VerticalAlignment.Top, Dock.Bottom => VerticalAlignment.Bottom, _ => VerticalAlignment.Center },
                Margin = new Thickness(2),
            };
            content.Children.Add(pill);
        }

        var tile = new Button
        {
            Content = content,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Stretch,
            Width = w,
            Height = h,
            Margin = new Thickness(2),
            Tag = item,
            Style = (Style)FindResource("PopupTile"),
        };
        if (!labels) tile.ToolTip = index < 9 ? $"{item.Name}  ·  {index + 1}" : item.Name;
        AutomationName(tile, item.Name);

        if (pill != null) _indicators[tile] = pill;
        if (!TilesFocusable) tile.Focusable = false;
        if (IsPreview)
        {
            tile.Focusable = false;
            return tile;
        }

        tile.Click += (_, _) => ItemInvoked?.Invoke(item,
            Keyboard.Modifiers.HasFlag(ModifierKeys.Control), Keyboard.Modifiers.HasFlag(ModifierKeys.Shift));
        tile.PreviewMouseUp += (_, e) =>
        {
            if (e.ChangedButton != MouseButton.Middle) return;
            e.Handled = true;
            ItemInvoked?.Invoke(item, true, true);
        };
        tile.MouseRightButtonUp += (_, e) =>
        {
            e.Handled = true;
            ItemMenuRequested?.Invoke(item, tile);
        };
        if (AcceptFileDrop && Launcher.FileHandler(item) != null) EnableFileDrop(tile, item);
        return tile;
    }

    /// <summary>Файл, брошенный на значок программы, открывается этой программой.</summary>
    private void EnableFileDrop(Button tile, LaunchItem item)
    {
        tile.AllowDrop = true;
        tile.ToolTip = $"{item.Name} — можно перетащить сюда файлы, чтобы открыть их в этой программе";

        void Highlight(bool on)
        {
            if (on) tile.SetResourceReference(BackgroundProperty, "Brush.AccentSubtle");
            else tile.ClearValue(BackgroundProperty);
        }

        tile.DragEnter += (_, e) =>
        {
            bool files = e.Data.GetDataPresent(DataFormats.FileDrop);
            e.Effects = files ? DragDropEffects.Copy : DragDropEffects.None;
            Highlight(files);
            e.Handled = true;
        };
        tile.DragOver += (_, e) =>
        {
            e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        };
        tile.DragLeave += (_, _) => Highlight(false);
        tile.Drop += (_, e) =>
        {
            Highlight(false);
            e.Handled = true;
            if (e.Data.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } files) FilesDropped?.Invoke(item, files);
        };
    }

    private static void AutomationName(DependencyObject o, string name) =>
        System.Windows.Automation.AutomationProperties.SetName(o, name);

    /// <summary>Подсвечивает совпадения поиска, остальные плитки приглушает. Возвращает первое совпадение.</summary>
    public Button? ApplyFilter(string filter)
    {
        Button? first = null;
        foreach (var tile in _tiles)
        {
            var item = (LaunchItem)tile.Tag;
            bool match = string.IsNullOrEmpty(filter) || Matches(item.Name, filter);
            tile.Opacity = match ? 1 : 0.22;
            if (match && first == null && !string.IsNullOrEmpty(filter)) first = tile;
        }
        return first;
    }

    private static bool Matches(string name, string filter)
    {
        if (name.Contains(filter, StringComparison.CurrentCultureIgnoreCase)) return true;
        // «vsc» → «Visual Studio Code»
        var initials = string.Concat(name.Split(new[] { ' ', '-', '_', '.' }, StringSplitOptions.RemoveEmptyEntries).Select(p => p[0]));
        return initials.StartsWith(filter, StringComparison.CurrentCultureIgnoreCase);
    }

    public void FocusFirst()
    {
        if (_tiles.Count > 0) _tiles[0].Focus();
    }
}
