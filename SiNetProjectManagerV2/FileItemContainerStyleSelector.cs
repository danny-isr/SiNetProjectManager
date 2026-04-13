using SiNetSQL.MVVM;
using System.Windows;
using System.Windows.Controls;

namespace SiNetProjectManagerV2.WPFUserControl
{
    public class FileItemContainerStyleSelector : StyleSelector
    {
        public Style? FileStyle { get; set; }
        public Style? DefaultStyle { get; set; }

        public override Style SelectStyle(object item, DependencyObject container)
        {
            return item is ProjectFileNode && FileStyle != null
                ? FileStyle
                : DefaultStyle ?? base.SelectStyle(item, container);
        }
    }
}
