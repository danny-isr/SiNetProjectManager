using System.Diagnostics;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace SiNetProjectManagerV2.Converters
{
    /// <summary>
    /// Converts hex color strings (e.g., "#E0E0E0") to SolidColorBrush for WPF bindings.
    /// Used by EmailListItemStyle to set background colors based on ContextColor property.
    /// </summary>
    public class ColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            // Debug output to verify converter is being called
            Debug.WriteLine($"[ColorConverter] Converting value: '{value}' (Type: {value?.GetType().Name ?? "null"})");

            if (value is string hex && !string.IsNullOrWhiteSpace(hex))
            {
                try
                {
                    var obj = new BrushConverter().ConvertFromString(hex);
                    if (obj is SolidColorBrush brush)
                    {
                        Debug.WriteLine($"[ColorConverter] Successfully converted '{hex}' to brush");
                        return brush;
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[ColorConverter] Error converting '{hex}': {ex.Message}");
                }
            }

            // Default to transparent so the ListBoxItem's own background shows through
            // Or use White for explicit white background
            Debug.WriteLine($"[ColorConverter] Returning default White brush");
            return Brushes.White;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is SolidColorBrush brush)
                return brush.Color.ToString();
            return "#FFFFFF";
        }
    }
}
