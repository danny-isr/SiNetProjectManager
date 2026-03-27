using System.Globalization;
using System.Windows.Data;

namespace SiNetProjectManager.Converters;

/// <summary>
/// Scales a font size by a percentage factor supplied as the converter parameter.
/// Usage: <c>FontSize="{Binding Source={StaticResource AppFontSize}, Converter={StaticResource FontScale}, ConverterParameter=0.8}"</c>
/// </summary>
[ValueConversion(typeof(double), typeof(double))]
public sealed class FontSizePercentageConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not double fontSize)
            return 12.0;

        double factor = parameter switch
        {
            double d => d,
            string s when double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) => parsed,
            _ => 0.8
        };

        return Math.Max(8.0, Math.Round(fontSize * factor, 1));
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
