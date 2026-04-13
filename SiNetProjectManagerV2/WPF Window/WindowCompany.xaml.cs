using Microsoft.Extensions.DependencyInjection;
using SiNetProjectManagerV2;
using SiNetSQL.Models;
using SiNetSQL.MVVM;
using System.Windows;

namespace WpfSiData.WPF_Window
{
    /// <summary>
    /// Interaction logic for WindowCompany.xaml
    /// </summary>
    public partial class WindowCompany : Window
    {
        private readonly CompanyViewModel _companyViewModel;

        public WindowCompany()
        {
            InitializeComponent();
            // Resolve ViewModel from DI container
            _companyViewModel = App.ServiceProvider.GetRequiredService<CompanyViewModel>();
            DataContext = _companyViewModel;
        }

        public Contact? SelectedContact
        {
            get
            {
                if (DataContext is CompanyViewModel vm)
                    return vm.SelectedContact;
                return null;
            }
        }
        private void OK_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedContact == null)
            {
                MessageBox.Show("יש לבחור איש קשר.", "שגיאה", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            _companyViewModel.SaveChanges();
            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            // Discard any changes made and close the window
            this.Close();
        }
    }
}

