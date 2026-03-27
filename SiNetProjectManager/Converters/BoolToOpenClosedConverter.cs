using System.Globalization;
using System.Windows.Data;

namespace SiNetProjectManager.Converters;

/// <summary>
/// Converts a boolean (IsOpen) to a Hebrew display string: "פתוח" / "סגור".
/// </summary>
public class BoolToOpenClosedConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is true ? "פתוח" : "סגור";

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
