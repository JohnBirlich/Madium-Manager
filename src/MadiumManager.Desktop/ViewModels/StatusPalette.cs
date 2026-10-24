using System.Windows.Media;

namespace MadiumManager.Desktop.ViewModels;

public static class StatusPalette
{
    public static readonly Brush Info = Freeze("#4F46E5");
    public static readonly Brush Success = Freeze("#087F5B");
    public static readonly Brush Warning = Freeze("#A15C00");
    public static readonly Brush Danger = Freeze("#C4324B");
    public static readonly Brush Muted = Freeze("#667085");
    public static readonly Brush InfoBackground = Freeze("#EEF2FF");
    public static readonly Brush SuccessBackground = Freeze("#E8F7F1");
    public static readonly Brush WarningBackground = Freeze("#FFF4E0");
    public static readonly Brush DangerBackground = Freeze("#FDECEF");
    public static readonly Brush MutedBackground = Freeze("#F1F5F9");

    private static Brush Freeze(string color)
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
