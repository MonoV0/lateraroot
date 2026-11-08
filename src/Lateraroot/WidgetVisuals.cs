using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace Lateraroot;

public static class WidgetVisuals
{
    public static Brush Ink(Brush background)
    {
        var color = ((SolidColorBrush)background).Color;
        double luminance = Luminance(color);
        return (luminance + .05) / .05 >= 1.05 / (luminance + .05) ? Brushes.Black : Brushes.White;
    }
    public static double Luminance(Color color) => .2126 * Linear(color.R) + .7152 * Linear(color.G) + .0722 * Linear(color.B);
    private static double Linear(byte channel) { double c = channel / 255d; return c <= .04045 ? c / 12.92 : Math.Pow((c + .055) / 1.055, 2.4); }

    // Balance short CJK labels into two lines instead of leaving one orphan character.
    // Approximate character widths avoid creating a text formatter for every off-screen file.
    public static string Balance(string text, double width, double fontSize = 12)
    {
        int[] starts = StringInfo.ParseCombiningCharacters(text);
        var parts = starts.Select((start, i) => text[start..(i + 1 < starts.Length ? starts[i + 1] : text.Length)]).ToArray();
        if (parts.Any(x => x[0] <= 0x7f)) return text; // Leave mixed/Latin text to native line breaking.
        double capacity = width / fontSize;
        if (parts.Length <= capacity || parts.Length > capacity * 2 || parts.Length < 4) return text;
        int split = parts.Length / 2;
        if ("ぁぃぅぇぉっゃゅょァィゥェォッャュョー、。）」』".Contains(parts[split][0])) split++;
        return split > capacity || parts.Length - split > capacity ? text : string.Concat(parts.Take(split)) + "\n" + string.Concat(parts.Skip(split));
    }
}

public sealed class StateLayerConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value is SolidColorBrush b && b.Color == Colors.White ? Brushes.Black : Brushes.White;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => DependencyProperty.UnsetValue;
}

public sealed class ColorInkConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value is string color ? WidgetVisuals.Ink(DesktopEdgeWindow.ColorBrush(color)) : Brushes.Black;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => DependencyProperty.UnsetValue;
}
