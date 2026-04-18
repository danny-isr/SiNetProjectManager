using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace SiNetProjectManagerV2.Converters;

/// <summary>
/// Returns the 1-based index of an item inside its parent ItemsControl.
/// Usage: bind to the item itself with this converter; the converter walks
/// up the visual tree to find the owning ItemsControl and calculates the position.
/// 
/// Example output: "1", "2", "3"
/// With ConverterParameter="טריגר": "טריגר 1", "טריגר 2"
/// </summary>
public class ItemIndexConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not DependencyObject container)
            return "?";

        var itemsControl = ItemsControl.ItemsControlFromItemContainer(container);
        if (itemsControl is null)
            return "?";

        var index = itemsControl.ItemContainerGenerator.IndexFromContainer(container);
        if (index < 0)
            return "?";

        var oneBasedIndex = index + 1;

        return parameter is string prefix && !string.IsNullOrEmpty(prefix)
            ? $"{prefix} {oneBasedIndex}"
            : oneBasedIndex.ToString();
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
