using System.Windows.Controls;

namespace SiNetProjectManagerV2.Controls
{
    /// <summary>
    /// Self-contained help button + popup used in the EmailManagementView top bar.
    /// Has no bindings to the parent DataContext; the popup state is driven by
    /// the internal HelpToggle (ToggleButton) only.
    /// </summary>
    public partial class EmailHelpPopupControl : UserControl
    {
        public EmailHelpPopupControl()
        {
            InitializeComponent();
        }
    }
}
