using SiNetSQL.MVVM;
using System.Windows;

namespace SiNetProjectManagerV2.WPF_Window
{
    /// <summary>
    /// Interaction logic for AlternativeNameWindow.xaml
    /// </summary>
    public partial class AlternativeNameWindow : Window
    {
        public AlternativeNameWindow(AlternativeNameViewModel vm)
        {
            InitializeComponent();
            DataContext = vm;
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            if (sender is UIElement element)
            {
                element.Focus();
            }

            if (DataContext is AlternativeNameViewModel vm && vm.IsOkEnabled)
            {
                vm.DialogResult = true;
                DialogResult = true;
            }
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is AlternativeNameViewModel vm)
                vm.DialogResult = false;
            DialogResult = false;
        }
    }
}
