using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace SiNetProjectManager.Converters;

/// <summary>
/// Two-way converter between hex color strings (e.g., "#FF5722") and <see cref="Color"/>.
/// Used by xctk:ColorPicker bindings in status color management UIs.
/// </summary>
public class HexToColorConverter : IValueConverter
{
    private static readonly Color FallbackColor = Color.FromRgb(0x80, 0x80, 0x80);

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is string hex && !string.IsNullOrWhiteSpace(hex))
        {
            try
            {
                return (Color)System.Windows.Media.ColorConverter.ConvertFromString(hex);
            }
            catch { /* fall through to fallback */ }
        }
        return FallbackColor;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is Color color)
        {
            return $"#{color.R:X2}{color.G:X2}{color.B:X2}";
        }
        return "#808080";
    }
}
