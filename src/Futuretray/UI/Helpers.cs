using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace Futuretray.UI;

/// <summary>Вложенные свойства, которые используют шаблоны контролов.</summary>
public static class Ui
{
    public static readonly DependencyProperty HoverBackgroundProperty = DependencyProperty.RegisterAttached(
        "HoverBackground", typeof(Brush), typeof(Ui), new PropertyMetadata(null));
    public static Brush? GetHoverBackground(DependencyObject o) => (Brush?)o.GetValue(HoverBackgroundProperty);
    public static void SetHoverBackground(DependencyObject o, Brush? v) => o.SetValue(HoverBackgroundProperty, v);

    public static readonly DependencyProperty PressedBackgroundProperty = DependencyProperty.RegisterAttached(
        "PressedBackground", typeof(Brush), typeof(Ui), new PropertyMetadata(null));
    public static Brush? GetPressedBackground(DependencyObject o) => (Brush?)o.GetValue(PressedBackgroundProperty);
    public static void SetPressedBackground(DependencyObject o, Brush? v) => o.SetValue(PressedBackgroundProperty, v);

    public static readonly DependencyProperty CornerRadiusProperty = DependencyProperty.RegisterAttached(
        "CornerRadius", typeof(CornerRadius), typeof(Ui), new PropertyMetadata(new CornerRadius(4)));
    public static CornerRadius GetCornerRadius(DependencyObject o) => (CornerRadius)o.GetValue(CornerRadiusProperty);
    public static void SetCornerRadius(DependencyObject o, CornerRadius v) => o.SetValue(CornerRadiusProperty, v);

    public static readonly DependencyProperty GlyphProperty = DependencyProperty.RegisterAttached(
        "Glyph", typeof(string), typeof(Ui), new PropertyMetadata(null));
    public static string? GetGlyph(DependencyObject o) => (string?)o.GetValue(GlyphProperty);
    public static void SetGlyph(DependencyObject o, string? v) => o.SetValue(GlyphProperty, v);

    public static readonly DependencyProperty PlaceholderProperty = DependencyProperty.RegisterAttached(
        "Placeholder", typeof(string), typeof(Ui), new PropertyMetadata(null));
    public static string? GetPlaceholder(DependencyObject o) => (string?)o.GetValue(PlaceholderProperty);
    public static void SetPlaceholder(DependencyObject o, string? v) => o.SetValue(PlaceholderProperty, v);
}

public sealed class EnumBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value != null && string.Equals(value.ToString(), parameter?.ToString(), StringComparison.OrdinalIgnoreCase);

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is true && parameter != null ? Enum.Parse(targetType, parameter.ToString()!) : Binding.DoNothing;
}

/// <summary>Visible, когда значение enum совпадает с параметром (Invert — наоборот).</summary>
public sealed class EnumVisibilityConverter : IValueConverter
{
    public bool Invert { get; set; }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        (value != null && string.Equals(value.ToString(), parameter?.ToString(), StringComparison.OrdinalIgnoreCase)) ^ Invert
            ? Visibility.Visible
            : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

public sealed class NullToCollapsedConverter : IValueConverter
{
    public bool Invert { get; set; }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        (value == null || value is string { Length: 0 }) ^ Invert ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

public sealed class BoolToVisibleConverter : IValueConverter
{
    public bool Invert { get; set; }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        (value is true) ^ Invert ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

public sealed class IntDoubleConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        System.Convert.ToDouble(value, CultureInfo.InvariantCulture);

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        (int)Math.Round(System.Convert.ToDouble(value, CultureInfo.InvariantCulture));
}

public sealed class ColumnsTextConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is int i && i > 0 ? i.ToString(culture) : "Авто";

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

public sealed class PercentConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is double d ? $"{Math.Round(d * 100)}%" : "";

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

/// <summary>«1 приложение», «3 приложения», «7 приложений».</summary>
public sealed class AppsCountConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is int n ? (n == 0 ? "Пусто" : $"{n} {Plural(n, "приложение", "приложения", "приложений")}") : "";

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;

    public static string Plural(int n, string one, string few, string many)
    {
        n = Math.Abs(n) % 100;
        if (n is >= 11 and <= 14) return many;
        return (n % 10) switch { 1 => one, 2 or 3 or 4 => few, _ => many };
    }
}
