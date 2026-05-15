using System.Windows.Controls;

namespace SiNetProjectManagerV2.Controls
{
    /// <summary>
    /// Compact "Selected Project" info banner. DataContext is inherited from the
    /// parent EmailManagementView (EmailManagementViewModel.SelectedProject).
    /// </summary>
    public partial class EmailSelectedProjectInfoControl : UserControl
    {
        public EmailSelectedProjectInfoControl()
        {
            InitializeComponent();
        }
    }
}
