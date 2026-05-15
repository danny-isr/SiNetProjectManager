using System.Windows.Controls;

namespace SiNetProjectManagerV2.Controls
{
    /// <summary>
    /// Top status bar fragment for EmailManagementView: shows connection state,
    /// connected Gmail address, status text, error count, and context-enrichment indicator.
    /// DataContext is inherited from the parent EmailManagementView (EmailManagementViewModel).
    /// </summary>
    public partial class EmailTopStatusBarControl : UserControl
    {
        public EmailTopStatusBarControl()
        {
            InitializeComponent();
        }
    }
}
