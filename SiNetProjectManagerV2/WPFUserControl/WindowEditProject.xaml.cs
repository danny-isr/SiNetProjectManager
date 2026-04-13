using Microsoft.Extensions.DependencyInjection;
using SiNetSQL.MVVM;
using System.Windows;
using System.Windows.Controls;

namespace SiNetProjectManagerV2.WPFUserControl
{
    public partial class WindowEditProject : UserControl
    {
        private readonly EditProjectViewModel _viewModel;

        public WindowEditProject()
        {
            InitializeComponent();

            // Resolve ViewModel from DI container
            _viewModel = App.ServiceProvider.GetRequiredService<EditProjectViewModel>();
            DataContext = _viewModel;
        }

        private void Submit_Click(object sender, RoutedEventArgs e)
        {
            if (!_viewModel.CanSave())
            {
                MessageBox.Show("יש למלא את השדה 'תאור למי הוגש'.", "שגיאה", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            _viewModel.SubmitProject();
            MessageBox.Show("פרויקט הוגש בהצלחה.", "הצלחה", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            if (!_viewModel.CanSave())
            {
                MessageBox.Show("יש למלא את השדה 'תאור למי הוגש'.", "שגיאה", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            _viewModel.SaveChanges();
            MessageBox.Show("השינויים נשמרו בהצלחה.", "הצלחה", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            _viewModel.ReloadSelectedProject();
            MessageBox.Show("הפעולה בוטלה");
        }
    }
}
