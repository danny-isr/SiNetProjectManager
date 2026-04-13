using System.Collections;
using System.Globalization;
using System.Windows.Data;

namespace SiNetProjectManagerV2.WPFUserControl;

/// <summary>
/// MultiBinding converter that merges two ObservableCollections
/// (sub-folders + files) into a single CompositeCollection for unified TreeView display.
/// 
/// Bindings:
///   [0] = Children (ObservableCollection&lt;ProjectFolderNode&gt;)
///   [1] = FolderFiles (ObservableCollection&lt;ProjectFileNode&gt;)
///   [2] = Children.Count (trigger re-evaluation on add/remove)
///   [3] = FolderFiles.Count (trigger re-evaluation on add/remove)
/// </summary>
public class CompositeChildrenConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        var composite = new CompositeCollection();

        if (values.Length >= 1 && values[0] is IList folders)
            composite.Add(new CollectionContainer { Collection = folders });

        if (values.Length >= 2 && values[1] is IList files)
            composite.Add(new CollectionContainer { Collection = files });

        return composite;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
