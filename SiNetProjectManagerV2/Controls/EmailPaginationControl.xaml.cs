using System.Windows.Controls;

namespace SiNetProjectManagerV2.Controls
{
    /// <summary>
    /// Pagination buttons (First / Previous / Next) and PageInfo label for the email list.
    /// DataContext is inherited from the parent EmailManagementView (EmailManagementViewModel).
    /// </summary>
    public partial class EmailPaginationControl : UserControl
    {
        public EmailPaginationControl()
        {
            InitializeComponent();
        }
    }
}
