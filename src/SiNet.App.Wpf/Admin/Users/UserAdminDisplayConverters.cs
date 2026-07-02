using System.Globalization;
using System.Windows.Data;
using SiNet.Application.Identity;

namespace SiNet.App.Wpf.Admin.Users;

/// <summary>Displays <see cref="AppAccUserType"/> with Hebrew-friendly labels in ComboBox items.</summary>
public sealed class AppAccUserTypeDisplayConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is AppAccUserType accUserType
            ? AppAccUserTypeDisplay.GetDisplayName(accUserType)
            : string.Empty;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Displays <see cref="AppRole"/> with Hebrew-friendly labels in ComboBox items.</summary>
public sealed class AppRoleDisplayConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is AppRole role
            ? AppRoleDisplay.GetDisplayName(role)
            : string.Empty;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
