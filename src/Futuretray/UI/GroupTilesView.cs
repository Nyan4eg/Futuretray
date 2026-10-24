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
    private Group? _group;
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

    /// <summary>Запуск элемента; второй аргумент — «не закрывать окно» (Ctrl/средняя кнопка).</summary>
    public event Action<LaunchItem, bool>? ItemInvoked;
    public event Action<LaunchItem, Button>? ItemMenuRequested;

    public IReadOnlyList<Button> Tiles => _tiles;

    public void Bind(Group group)
    {
        if (_group != null) _group.Changed -= OnGroupChanged;
        _group = group;
        _group.Changed += OnGroupChanged;
        Rebuild();
    }

    public void Unbind()
    {
        if (_group != null) _group.Changed -= OnGroupChanged;
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

        var tile = new Button
        {
            Content = panel,
            Width = w,
            Height = h,
            Margin = new Thickness(2),
            Tag = item,
            Style = (Style)FindResource("PopupTile"),
        };
        if (!labels) tile.ToolTip = index < 9 ? $"{item.Name}  ·  {index + 1}" : item.Name;
        AutomationName(tile, item.Name);

        if (!TilesFocusable) tile.Focusable = false;
        if (IsPreview)
        {
            tile.Focusable = false;
            return tile;
        }

        tile.Click += (_, _) => ItemInvoked?.Invoke(item, Keyboard.Modifiers.HasFlag(ModifierKeys.Control));
        tile.PreviewMouseUp += (_, e) =>
        {
            if (e.ChangedButton != MouseButton.Middle) return;
            e.Handled = true;
            ItemInvoked?.Invoke(item, true);
        };
        tile.MouseRightButtonUp += (_, e) =>
        {
            e.Handled = true;
            ItemMenuRequested?.Invoke(item, tile);
        };
        return tile;
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
