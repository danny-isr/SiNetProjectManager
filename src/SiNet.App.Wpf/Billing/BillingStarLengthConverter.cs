using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace SiNet.App.Wpf.Billing;

public sealed class BillingStarLengthConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var n = value switch
        {
            decimal d => (double)d,
            double x => x,
            int i => i,
            _ => 0d
        };
        return new GridLength(Math.Max(0d, n), GridUnitType.Star);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
