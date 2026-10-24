using System.Windows.Media;

namespace MadiumManager.Desktop.ViewModels;

public static class StatusPalette
{
    public static readonly Brush Info = Freeze("#7EA4FF");
    public static readonly Brush Success = Freeze("#43D7B0");
    public static readonly Brush Warning = Freeze("#F2BE5C");
    public static readonly Brush Danger = Freeze("#F17886");
    public static readonly Brush Muted = Freeze("#8998B2");
    public static readonly Brush InfoBackground = Freeze("#1B2D52");
    public static readonly Brush SuccessBackground = Freeze("#123C38");
    public static readonly Brush WarningBackground = Freeze("#40321B");
    public static readonly Brush DangerBackground = Freeze("#44232E");
    public static readonly Brush MutedBackground = Freeze("#202B3D");

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
