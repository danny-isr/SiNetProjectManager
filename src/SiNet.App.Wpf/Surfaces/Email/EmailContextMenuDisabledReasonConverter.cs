using System.Globalization;
using System.Windows.Data;

namespace SiNet.App.Wpf.Surfaces.Email;

/// <summary>
/// Resolves a Hebrew disabled-reason tooltip for context menu items
/// (MultiBinding: EmailListRow + EmailListViewModel from ListBoxItem.Tag).
/// </summary>
public sealed class EmailContextMenuDisabledReasonConverter : IMultiValueConverter
{
    public object? Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length < 2 || values[1] is not EmailListViewModel viewModel)
        {
            return null;
        }

        var row = values[0] as EmailListRow;

        if (parameter is EmailContextMenuAction typedAction)
        {
            return viewModel.GetContextMenuDisabledReason(row, typedAction);
        }

        if (parameter is string actionName
            && Enum.TryParse(actionName, ignoreCase: true, out EmailContextMenuAction parsedAction))
        {
            return viewModel.GetContextMenuDisabledReason(row, parsedAction);
        }

        return null;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
