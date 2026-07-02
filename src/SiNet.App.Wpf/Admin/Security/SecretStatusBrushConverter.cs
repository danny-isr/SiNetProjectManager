using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using SiNet.Application.Configuration;

namespace SiNet.App.Wpf.Admin.Security;

public sealed class SecretStatusBrushConverter : IValueConverter
{
    private static readonly SolidColorBrush Green = new(Color.FromRgb(76, 175, 80));
    private static readonly SolidColorBrush Orange = new(Color.FromRgb(255, 152, 0));
    private static readonly SolidColorBrush Red = new(Color.FromRgb(244, 67, 54));

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not SecretStatusLevel level)
        {
            return Red;
        }

        return level switch
        {
            SecretStatusLevel.Valid => Green,
            SecretStatusLevel.Incomplete or SecretStatusLevel.Invalid => Orange,
            _ => Red,
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
