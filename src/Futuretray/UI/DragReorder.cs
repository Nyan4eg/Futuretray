using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Futuretray.UI;

/// <summary>Перетаскивание элементов списка мышью с «живой» перестановкой.</summary>
public static class DragReorder
{
    public static void Attach<T>(ListBox list, Func<ObservableCollection<T>?> source, string format) where T : class
    {
        Point start = default;
        T? pressed = null;
        list.AllowDrop = true;

        list.PreviewMouseLeftButtonDown += (_, e) =>
        {
            start = e.GetPosition(list);
            pressed = (ItemsControl.ContainerFromElement(list, (DependencyObject)e.OriginalSource) as ListBoxItem)?.DataContext as T;
        };

        list.PreviewMouseMove += (_, e) =>
        {
            if (pressed == null || e.LeftButton != MouseButtonState.Pressed) return;
            var delta = e.GetPosition(list) - start;
            if (Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance &&
                Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance) return;
            var item = pressed;
            pressed = null;
            DragDrop.DoDragDrop(list, new DataObject(format, item), DragDropEffects.Move);
        };

        list.PreviewMouseLeftButtonUp += (_, _) => pressed = null;

        list.DragOver += (_, e) =>
        {
            if (!e.Data.GetDataPresent(format)) return;
            e.Effects = DragDropEffects.Move;
            e.Handled = true;
            var collection = source();
            if (collection == null || e.Data.GetData(format) is not T item) return;
            var target = (ItemsControl.ContainerFromElement(list, (DependencyObject)e.OriginalSource) as ListBoxItem)?.DataContext as T;
            if (target == null || ReferenceEquals(target, item)) return;
            int from = collection.IndexOf(item), to = collection.IndexOf(target);
            if (from >= 0 && to >= 0) collection.Move(from, to);
        };

        list.Drop += (_, e) =>
        {
            if (!e.Data.GetDataPresent(format)) return;
            e.Handled = true;
            if (e.Data.GetData(format) is T item) list.SelectedItem = item;
        };
    }
}
