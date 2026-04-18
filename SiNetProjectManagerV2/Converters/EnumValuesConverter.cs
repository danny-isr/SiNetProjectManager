using System;
using System.Globalization;
using System.Windows.Data;

namespace SiNetProjectManagerV2.Converters;

/// <summary>
/// Returns all values of an enum type for use as ItemsSource in ComboBoxes.
/// Usage: &lt;ComboBox ItemsSource="{Binding Source={x:Type models:MyEnum}, Converter={StaticResource EnumValues}}"
///                  SelectedItem="{Binding MyEnumProperty}"/&gt;
/// </summary>
public class EnumValuesConverter : IValueConverter
{
    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is Type enumType && enumType.IsEnum)
            return Enum.GetValues(enumType);

        return null;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => Binding.DoNothing;
}
