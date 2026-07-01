using System.Globalization;
using System.Windows.Data;

namespace SiNet.App.Wpf.Shared.Projects;

/// <summary>
/// Returns the logical negation of a <see cref="bool"/> binding. Used by the shared Project Selector
/// to disable inputs while a load is in progress (bind <c>IsEnabled</c> to <c>IsBusy</c> inverted).
/// </summary>
public sealed class InverseBooleanConverter : IValueConverter
{
    /// <inheritdoc />
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is bool b ? !b : true;

    /// <inheritdoc />
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is bool b ? !b : false;
}
