using System.Windows.Controls;

namespace SiNetProjectManagerV2.Controls
{
    /// <summary>
    /// Refresh button + Calendar visibility ToggleButton. Does NOT contain the
    /// CalendarWebView itself — that stays in EmailManagementView so its
    /// code-behind can keep accessing it by x:Name.
    /// DataContext is inherited from the parent EmailManagementView (EmailManagementViewModel).
    /// The ToggleButton style relies on the parent's implicit ToggleButton style
    /// (BasedOn={StaticResource {x:Type ToggleButton}}), which is resolved at the
    /// site where this control is used (UserControl.Resources of EmailManagementView).
    /// </summary>
    public partial class EmailRefreshAndCalendarToggleControl : UserControl
    {
        public EmailRefreshAndCalendarToggleControl()
        {
            InitializeComponent();
        }
    }
}
