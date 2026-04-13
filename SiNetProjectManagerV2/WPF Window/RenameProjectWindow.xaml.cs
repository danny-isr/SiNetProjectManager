using System.Windows;
using SiNetSQL.MVVM;

namespace SiNetProjectManagerV2.WPF_Window
{
    public partial class RenameProjectWindow : Window
    {
        public RenameProjectWindow(RenameProjectDialogViewModel vm)
        {
            InitializeComponent();
            DataContext = vm;
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is RenameProjectDialogViewModel vm && vm.CanOk)
            {
                vm.DialogResult = true;
                DialogResult = true;
            }
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is RenameProjectDialogViewModel vm)
                vm.DialogResult = false;
            DialogResult = false;
        }
    }
}
