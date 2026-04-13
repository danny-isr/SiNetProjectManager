using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using SiNetSQL.MVVM;

namespace SiNetProjectManagerV2.Converters;

/// <summary>
/// Converts a StatusId (int) to a <see cref="SolidColorBrush"/> using the
/// <see cref="StatusColorServiceLocator"/> cached color map.
/// Priority chain: User Override → Global Default → #808080.
/// </summary>
public class StatusIdToColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is int statusId)
        {
            var service = StatusColorServiceLocator.Instance;
            if (service != null)
            {
                var hex = service.GetStatusColor(statusId);
                try
                {
                    var obj = new BrushConverter().ConvertFromString(hex);
                    if (obj is SolidColorBrush brush)
                        return brush;
                }
                catch { /* fall through to default */ }
            }
        }

        return new SolidColorBrush(Color.FromRgb(0x80, 0x80, 0x80));
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
