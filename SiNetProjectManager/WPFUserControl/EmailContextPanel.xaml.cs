using System.Windows.Controls;

namespace SiNetProjectManager.WPFUserControl;

/// <summary>
/// Interaction logic for EmailContextPanel.xaml.
/// The DataContext (<see cref="SiNetSQL.MVVM.EmailContextViewModel"/>) is set
/// by the parent view that hosts this panel.
/// </summary>
public partial class EmailContextPanel : UserControl
{
    public EmailContextPanel()
    {
        InitializeComponent();
    }
}
