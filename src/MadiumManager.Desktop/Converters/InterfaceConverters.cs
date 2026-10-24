using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using MadiumManager.Desktop.Models;

namespace MadiumManager.Desktop.Converters;

public sealed class AppPageToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        IsCurrentPage(value, parameter) ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();

    private static bool IsCurrentPage(object value, object parameter) =>
        value is AppPage current &&
        parameter is string target &&
        Enum.TryParse<AppPage>(target, ignoreCase: true, out var expected) &&
        current == expected;
}

public sealed class AppPageToBrushConverter : IValueConverter
{
    private static readonly Brush SelectedBrush = CreateBrush("#E8EAFF");

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is AppPage current &&
            parameter is string target &&
            Enum.TryParse<AppPage>(target, ignoreCase: true, out var expected) &&
            current == expected)
        {
            return SelectedBrush;
        }

        return Brushes.Transparent;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();

    private static Brush CreateBrush(string color)
    {
        if (ColorConverter.ConvertFromString(color) is not Color parsedColor)
        {
            throw new FormatException($"Invalid color value: {color}.");
        }

        var brush = new SolidColorBrush(parsedColor);
        brush.Freeze();
        return brush;
    }
}

public sealed class InverseBooleanToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is bool visible && visible ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class NonEmptyStringToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is string text && !string.IsNullOrWhiteSpace(text) ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
