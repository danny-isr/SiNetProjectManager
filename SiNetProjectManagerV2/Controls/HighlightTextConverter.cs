using System.Globalization;
using System.Windows.Data;

namespace SiNetProjectManagerV2.Controls;

/// <summary>
/// MultiValueConverter for text highlighting scenarios.
/// Can be used as an alternative to the attached behavior approach.
/// </summary>
public class HighlightTextConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length < 2)
            return string.Empty;

        var sourceText = values[0] as string;
        var highlightText = values[1] as string;

        // For simple binding scenarios, just return the source text
        // The actual highlighting is done by HighlightBehavior
        return sourceText ?? string.Empty;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
