using System.Windows.Controls;

namespace SiNetProjectManagerV2.Controls
{
    /// <summary>
    /// Action bar shown below the EmailViewer: File Email / Move to Project / Unfile Email.
    /// DataContext is inherited from the parent EmailManagementView (EmailManagementViewModel).
    /// </summary>
    public partial class EmailActionBarControl : UserControl
    {
        public EmailActionBarControl()
        {
            InitializeComponent();
        }
    }
}
