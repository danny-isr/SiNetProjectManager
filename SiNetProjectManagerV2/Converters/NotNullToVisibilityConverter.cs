using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace SiNetProjectManagerV2.Converters;

/// <summary>
/// Returns <see cref="Visibility.Visible"/> when the bound value is not null,
/// <see cref="Visibility.Collapsed"/> otherwise.
/// Used to show MasterPlan sync indicators on mapped entities.
/// </summary>
public sealed class NotNullToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is not null ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
